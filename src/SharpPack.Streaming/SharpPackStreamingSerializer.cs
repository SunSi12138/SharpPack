using SharpPack.Internal;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace SharpPack.Streaming;

public static class SharpPackStreamingSerializer
{
    static readonly ConditionalWeakTable<PipeReader, LengthPrefixedReaderState>
        s_lengthPrefixedReaderStates = new();

    sealed class LengthPrefixedReaderState
    {
        public bool IsTerminal { get; set; }
    }

    /// <summary>
    /// Serializes one framed RPC payload directly into a pipe without an
    /// intermediate byte array. The caller owns the frame header.
    /// </summary>
    public static async ValueTask<int> SerializeFrameAsync<T>(
        PipeWriter pipeWriter,
        T? value,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeWriter);

        var written = context is null
            ? SharpPackSerializer.Serialize(ref pipeWriter, value)
            : SharpPackSerializer.Serialize(ref pipeWriter, value, context);

        var result = await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return written;
    }

    /// <summary>
    /// Deserializes one length-delimited RPC payload directly from a pipe.
    /// Exactly <paramref name="payloadLength"/> bytes are consumed.
    /// </summary>
    public static async ValueTask<T?> DeserializeFrameAsync<T>(
        PipeReader pipeReader,
        int payloadLength,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeReader);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadLength);

        var result = await pipeReader
            .ReadAtLeastAsync(payloadLength, cancellationToken)
            .ConfigureAwait(false);
        var buffer = result.Buffer;

        if (result.IsCanceled)
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.Start);
            throw new OperationCanceledException(cancellationToken);
        }

        if (buffer.Length < payloadLength)
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            throw new EndOfStreamException(
                $"The pipe completed with {buffer.Length} bytes available; " +
                $"{payloadLength} bytes were required.");
        }

        var payload = buffer.Slice(0, payloadLength);
        try
        {
            T? value = default;
            var consumed = context is null
                ? SharpPackSerializer.Deserialize(payload, ref value)
                : SharpPackSerializer.Deserialize(payload, ref value, context);

            if (consumed != payloadLength)
            {
                throw new SharpPackSerializationException(
                    $"The formatter consumed {consumed} of the " +
                    $"{payloadLength}-byte RPC payload.");
            }

            var payloadEnd = buffer.GetPosition(payloadLength);
            pipeReader.AdvanceTo(payloadEnd, payloadEnd);
            return value;
        }
        catch
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            throw;
        }
    }

    /// <summary>
    /// Serializes one SharpPack payload as a self-contained transport frame:
    /// a 4-byte little-endian payload length followed by the unchanged
    /// SharpPack payload bytes.
    /// </summary>
    /// <remarks>
    /// The payload is buffered at item granularity so its length is known
    /// before the frame is emitted. The caller owns <paramref name="pipeWriter"/>
    /// and the library does not complete it.
    /// </remarks>
    public static async ValueTask SerializeLengthPrefixedAsync<T>(
        PipeWriter pipeWriter,
        T? value,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeWriter);

        var payloadWriter = ReusableLinkedArrayBufferWriterPool.Rent(
            out var payloadWriterLeaseId);
        try
        {
            var payloadLength = context is null
                ? SharpPackSerializer.Serialize(ref payloadWriter, value)
                : SharpPackSerializer.Serialize(ref payloadWriter, value, context);

            if (payloadLength < 0)
            {
                throw new SharpPackSerializationException(
                    "The serialized payload length exceeded the supported Int32 range.");
            }

            var header = pipeWriter.GetSpan(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(
                header,
                checked((uint)payloadLength));
            pipeWriter.Advance(sizeof(uint));

            foreach (var segment in payloadWriter)
            {
                if (!segment.IsEmpty)
                {
                    pipeWriter.Write(segment.Span);
                }
            }

            var result = await pipeWriter
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            if (result.IsCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        finally
        {
            ReusableLinkedArrayBufferWriterPool.Return(
                payloadWriter,
                payloadWriterLeaseId);
        }
    }

    /// <summary>
    /// Deserializes one self-contained length-prefixed transport frame.
    /// </summary>
    /// <remarks>
    /// The 4-byte little-endian length prefix is transport metadata and is
    /// not part of the SharpPack payload. Core deserialization starts only
    /// after the complete payload is available, and it must consume exactly
    /// the declared payload length. The caller owns <paramref name="pipeReader"/>.
    /// If cancellation interrupts an in-progress payload after its frame header
    /// has been consumed, that reader is terminal for the length-prefixed APIs:
    /// subsequent framed reads throw <see cref="InvalidOperationException"/>
    /// rather than interpreting the remaining payload as a new frame.
    /// </remarks>
    public static async ValueTask<T?> DeserializeLengthPrefixedAsync<T>(
        PipeReader pipeReader,
        int maxFrameLength = int.MaxValue,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var result = await DeserializeLengthPrefixedCoreAsync<T>(
                pipeReader,
                allowCleanEndOfStream: false,
                maxFrameLength,
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Value;
    }

    /// <summary>
    /// Deserializes consecutive self-contained length-prefixed transport
    /// frames until the pipe completes cleanly at a frame boundary.
    /// </summary>
    public static async IAsyncEnumerable<T?> DeserializeLengthPrefixedItemsAsync<T>(
        PipeReader pipeReader,
        int maxFrameLength = int.MaxValue,
        SharpPackSerializerContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await DeserializeLengthPrefixedCoreAsync<T>(
                    pipeReader,
                    allowCleanEndOfStream: true,
                    maxFrameLength,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.HasValue)
            {
                yield break;
            }

            yield return result.Value;
        }
    }

    static async ValueTask<(bool HasValue, T? Value)>
        DeserializeLengthPrefixedCoreAsync<T>(
            PipeReader pipeReader,
            bool allowCleanEndOfStream,
            int maxFrameLength,
            SharpPackSerializerContext? context,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipeReader);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrameLength);
        ThrowIfLengthPrefixedReaderIsTerminal(pipeReader);

        var readResult = await pipeReader
            .ReadAtLeastAsync(sizeof(uint), cancellationToken)
            .ConfigureAwait(false);
        var buffer = readResult.Buffer;

        if (readResult.IsCanceled)
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.Start);
            throw new OperationCanceledException(cancellationToken);
        }

        if (buffer.Length < sizeof(uint))
        {
            if (allowCleanEndOfStream &&
                readResult.IsCompleted &&
                buffer.IsEmpty)
            {
                pipeReader.AdvanceTo(buffer.End, buffer.End);
                return (false, default);
            }

            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            throw new EndOfStreamException(
                $"The pipe completed with {buffer.Length} frame-header bytes available; " +
                $"{sizeof(uint)} bytes were required.");
        }

        var headerReader = new SequenceReader<byte>(buffer);
        if (!headerReader.TryReadLittleEndian(out int rawPayloadLength))
        {
            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            throw new EndOfStreamException(
                "The pipe did not contain a complete length-prefixed frame header.");
        }

        var payloadLengthValue = unchecked((uint)rawPayloadLength);
        if (payloadLengthValue > int.MaxValue)
        {
            pipeReader.AdvanceTo(buffer.Start, headerReader.Position);
            throw new SharpPackSerializationException(
                $"The frame payload length {payloadLengthValue} exceeds the supported Int32 range.");
        }

        var payloadLength = (int)payloadLengthValue;
        if (payloadLength > maxFrameLength)
        {
            pipeReader.AdvanceTo(buffer.Start, headerReader.Position);
            throw new SharpPackSerializationException(
                $"The frame payload length {payloadLength} exceeds the configured maximum " +
                $"{maxFrameLength}.");
        }

        if (payloadLength > Array.MaxLength)
        {
            pipeReader.AdvanceTo(buffer.Start, headerReader.Position);
            throw new SharpPackSerializationException(
                $"The frame payload length {payloadLength} exceeds the supported array length " +
                $"{Array.MaxLength}.");
        }

        byte[]? rentedPayload = null;
        try
        {
            Memory<byte> payload = payloadLength == 0
                ? Memory<byte>.Empty
                : (rentedPayload = ArrayPool<byte>.Shared.Rent(payloadLength))
                    .AsMemory(0, payloadLength);

            var payloadStart = headerReader.Position;
            var availablePayload = buffer.Slice(payloadStart);
            var copied = (int)Math.Min(
                availablePayload.Length,
                payloadLength);

            if (copied != 0)
            {
                availablePayload.Slice(0, copied).CopyTo(payload.Span);
            }

            var consumed = buffer.GetPosition(copied, payloadStart);
            pipeReader.AdvanceTo(consumed, consumed);

            while (copied < payloadLength)
            {
                try
                {
                    readResult = await pipeReader
                        .ReadAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    MarkLengthPrefixedReaderTerminal(pipeReader);
                    throw;
                }

                buffer = readResult.Buffer;

                if (readResult.IsCanceled)
                {
                    pipeReader.AdvanceTo(buffer.Start, buffer.Start);
                    MarkLengthPrefixedReaderTerminal(pipeReader);
                    throw new OperationCanceledException(cancellationToken);
                }

                if (buffer.IsEmpty)
                {
                    pipeReader.AdvanceTo(buffer.End, buffer.End);
                    if (readResult.IsCompleted)
                    {
                        throw new EndOfStreamException(
                            $"The pipe completed with {copied} payload bytes available; " +
                            $"{payloadLength} bytes were required.");
                    }

                    continue;
                }

                var remaining = payloadLength - copied;
                var toCopy = (int)Math.Min(buffer.Length, remaining);
                buffer.Slice(0, toCopy)
                    .CopyTo(payload.Span.Slice(copied, toCopy));
                copied += toCopy;

                var payloadEnd = buffer.GetPosition(toCopy);
                pipeReader.AdvanceTo(payloadEnd, payloadEnd);

                if (copied < payloadLength && readResult.IsCompleted)
                {
                    throw new EndOfStreamException(
                        $"The pipe completed with {copied} payload bytes available; " +
                        $"{payloadLength} bytes were required.");
                }
            }

            T? value = default;
            var consumedPayload = context is null
                ? SharpPackSerializer.Deserialize(payload.Span, ref value)
                : SharpPackSerializer.Deserialize(payload.Span, ref value, context);

            if (consumedPayload != payloadLength)
            {
                throw new SharpPackSerializationException(
                    $"The formatter consumed {consumedPayload} of the " +
                    $"{payloadLength}-byte framed payload.");
            }

            return (true, value);
        }
        finally
        {
            if (rentedPayload is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedPayload);
            }
        }
    }

    static void ThrowIfLengthPrefixedReaderIsTerminal(PipeReader pipeReader)
    {
        if (s_lengthPrefixedReaderStates.TryGetValue(pipeReader, out var state) &&
            state.IsTerminal)
        {
            throw new InvalidOperationException(
                "This PipeReader cannot continue length-prefixed deserialization because " +
                "cancellation interrupted an in-progress frame after its header was consumed. " +
                "The frame boundary is no longer recoverable; start a new framed transport.");
        }
    }

    static void MarkLengthPrefixedReaderTerminal(PipeReader pipeReader)
    {
        s_lengthPrefixedReaderStates.GetValue(
            pipeReader,
            static _ => new LengthPrefixedReaderState()).IsTerminal = true;
    }

    public static async ValueTask SerializeAsync<T>(
        PipeWriter pipeWriter,
        int count,
        IEnumerable<T> source,
        int flushRate = 4096,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        static void WriteCollectionHeader(PipeWriter pipeWriter, int count, SharpPackWriterOptionalState state)
        {
            var writer = new SharpPackWriter<PipeWriter>(ref pipeWriter, state);
            writer.WriteCollectionHeader(count);
            writer.Flush();
        }

        static bool WriteWhileReachFlushRate(PipeWriter pipeWriter, IEnumerator<T> enumerator, int flushRate, SharpPackWriterOptionalState state)
        {
            var writer = new SharpPackWriter<PipeWriter>(ref pipeWriter, state);
            while (enumerator.MoveNext())
            {
                writer.WriteValue(enumerator.Current);
                if (flushRate < writer.WrittenCount)
                {
                    writer.Flush();
                    return true;
                }
            }

            writer.Flush();
            return false; // false when completed.
        }

        using var state = SharpPackWriterOptionalStatePool.Rent(context);

        WriteCollectionHeader(pipeWriter, count, state);

        using var enumerator = source.GetEnumerator();

        while (WriteWhileReachFlushRate(pipeWriter, enumerator, flushRate, state))
        {
            await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask SerializeAsync<T>(
        Stream stream,
        int count,
        IEnumerable<T> source,
        int flushRate = 4096,
        SharpPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        static void WriteCollectionHeader(ReusableLinkedArrayBufferWriter bufferWriter, int count, SharpPackWriterOptionalState state)
        {
            var writer = new SharpPackWriter<ReusableLinkedArrayBufferWriter>(ref bufferWriter, state);
            writer.WriteCollectionHeader(count);
            writer.Flush();
        }

        static bool WriteWhileReachFlushRate(ReusableLinkedArrayBufferWriter bufferWriter, IEnumerator<T> enumerator, int flushRate, SharpPackWriterOptionalState state)
        {
            var writer = new SharpPackWriter<ReusableLinkedArrayBufferWriter>(ref bufferWriter, state);
            while (enumerator.MoveNext())
            {
                writer.WriteValue(enumerator.Current);
                if (flushRate < writer.WrittenCount)
                {
                    writer.Flush();
                    return true;
                }
            }

            writer.Flush();
            return false; // false when completed.
        }

        using var state = SharpPackWriterOptionalStatePool.Rent(context);

        var tempWriter = ReusableLinkedArrayBufferWriterPool.Rent(
            out var tempWriterLeaseId);
        try
        {
            WriteCollectionHeader(tempWriter, count, state);

            using var enumerator = source.GetEnumerator();

            while (WriteWhileReachFlushRate(tempWriter, enumerator, flushRate, state))
            {
                await tempWriter.WriteToAndResetAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            await tempWriter.WriteToAndResetAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReusableLinkedArrayBufferWriterPool.Return(
                tempWriter,
                tempWriterLeaseId);
        }
    }

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        PipeReader pipeReader,
        int bufferAtLeast = 4096,
        int readMinimumSize = 8192,
        SharpPackSerializerContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        static void ReadCollectionHeader(
            in ReadOnlySequence<byte> buffer,
            SharpPackReaderOptionalState state,
            out int length)
        {
            using var reader = new SharpPackReader(buffer, state);
            if (!reader.DangerousTryReadCollectionHeader(out length))
            {
                length = 0;
            }
        }

        static (int Consumed, int Remain) DeserializeAvailable(
            in ReadOnlySequence<byte> buffer,
            int bufferAtLeast,
            List<T?> itemBuffer,
            int remain,
            bool bufferIsFull,
            SharpPackReaderOptionalState state)
        {
            using var reader = new SharpPackReader(buffer, state);
            while (remain != 0 &&
                   (bufferIsFull || bufferAtLeast < reader.Remaining))
            {
                itemBuffer.Add(reader.ReadValue<T?>());
                remain--;
            }

            return (reader.Consumed, remain);
        }

        ArgumentNullException.ThrowIfNull(pipeReader);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferAtLeast);
        ArgumentOutOfRangeException.ThrowIfLessThan(readMinimumSize, bufferAtLeast);

        context?.EnsureRootType<T>();
        using var state = SharpPackReaderOptionalStatePool.Rent(context);
        var itemBuffer = new List<T?>();
        var remain = -1;
        var readResult = await pipeReader
            .ReadAtLeastAsync(4, cancellationToken)
            .ConfigureAwait(false);

        while (true)
        {
            var buffer = readResult.Buffer;
            if (readResult.IsCanceled)
            {
                pipeReader.AdvanceTo(buffer.Start, buffer.Start);
                throw new OperationCanceledException(cancellationToken);
            }

            var parseStart = buffer.Start;
            if (remain < 0)
            {
                if (buffer.Length < 4)
                {
                    pipeReader.AdvanceTo(buffer.Start, buffer.End);
                    if (readResult.IsCompleted)
                    {
                        throw new EndOfStreamException(
                            "The pipe completed before the collection header was available.");
                    }

                    readResult = await pipeReader
                        .ReadAtLeastAsync(4, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                ReadCollectionHeader(buffer, state, out remain);
                parseStart = buffer.GetPosition(4);
                if (remain > 0)
                {
                    itemBuffer.EnsureCapacity(Math.Min(remain, 256));
                }
            }

            if (remain == 0)
            {
                pipeReader.AdvanceTo(parseStart, parseStart);
                yield break;
            }

            int consumedByteCount;
            try
            {
                var result = DeserializeAvailable(
                    buffer.Slice(parseStart),
                    bufferAtLeast,
                    itemBuffer,
                    remain,
                    readResult.IsCompleted,
                    state);
                consumedByteCount = result.Consumed;
                remain = result.Remain;
            }
            catch
            {
                pipeReader.AdvanceTo(parseStart, buffer.End);
                throw;
            }

            var consumedPosition = buffer.GetPosition(
                consumedByteCount,
                parseStart);
            pipeReader.AdvanceTo(
                consumedPosition,
                remain == 0 ? consumedPosition : buffer.End);

            foreach (var item in itemBuffer)
            {
                yield return item;
            }
            itemBuffer.Clear();

            if (remain == 0)
            {
                yield break;
            }
            if (readResult.IsCompleted)
            {
                throw new EndOfStreamException(
                    $"The pipe completed with {remain} collection items remaining.");
            }

            readResult = await pipeReader
                .ReadAtLeastAsync(readMinimumSize, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        Stream stream,
        int bufferAtLeast = 4096,
        int readMinimumSize = 8192,
        SharpPackSerializerContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var pipeReader = PipeReader.Create(
            stream,
            new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await foreach (var item in DeserializeAsync<T>(
                               pipeReader,
                               bufferAtLeast,
                               readMinimumSize,
                               context,
                               cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            await pipeReader.CompleteAsync().ConfigureAwait(false);
        }
    }
}
