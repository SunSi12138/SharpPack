using MemoryPack.Internal;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace MemoryPack.Streaming;

public static class MemoryPackStreamingSerializer
{
    /// <summary>
    /// Serializes one framed RPC payload directly into a pipe without an
    /// intermediate byte array. The caller owns the frame header.
    /// </summary>
    public static async ValueTask<int> SerializeFrameAsync<T>(
        PipeWriter pipeWriter,
        T? value,
        MemoryPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeWriter);

        var written = context is null
            ? MemoryPackSerializer.Serialize(ref pipeWriter, value)
            : MemoryPackSerializer.Serialize(ref pipeWriter, value, context);

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
        MemoryPackSerializerContext? context = null,
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
                ? MemoryPackSerializer.Deserialize(payload, ref value)
                : MemoryPackSerializer.Deserialize(payload, ref value, context);

            if (consumed != payloadLength)
            {
                throw new MemoryPackSerializationException(
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

    public static async ValueTask SerializeAsync<T>(
        PipeWriter pipeWriter,
        int count,
        IEnumerable<T> source,
        int flushRate = 4096,
        MemoryPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        static void WriteCollectionHeader(PipeWriter pipeWriter, int count, MemoryPackWriterOptionalState state)
        {
            var writer = new MemoryPackWriter<PipeWriter>(ref pipeWriter, state);
            writer.WriteCollectionHeader(count);
            writer.Flush();
        }

        static bool WriteWhileReachFlushRate(PipeWriter pipeWriter, IEnumerator<T> enumerator, int flushRate, MemoryPackWriterOptionalState state)
        {
            var writer = new MemoryPackWriter<PipeWriter>(ref pipeWriter, state);
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

        using var state = MemoryPackWriterOptionalStatePool.Rent(context);

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
        MemoryPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        static void WriteCollectionHeader(ReusableLinkedArrayBufferWriter bufferWriter, int count, MemoryPackWriterOptionalState state)
        {
            var writer = new MemoryPackWriter<ReusableLinkedArrayBufferWriter>(ref bufferWriter, state);
            writer.WriteCollectionHeader(count);
            writer.Flush();
        }

        static bool WriteWhileReachFlushRate(ReusableLinkedArrayBufferWriter bufferWriter, IEnumerator<T> enumerator, int flushRate, MemoryPackWriterOptionalState state)
        {
            var writer = new MemoryPackWriter<ReusableLinkedArrayBufferWriter>(ref bufferWriter, state);
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

        using var state = MemoryPackWriterOptionalStatePool.Rent(context);

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
        MemoryPackSerializerContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        static void ReadCollectionHeader(
            in ReadOnlySequence<byte> buffer,
            MemoryPackReaderOptionalState state,
            out int length)
        {
            using var reader = new MemoryPackReader(buffer, state);
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
            MemoryPackReaderOptionalState state)
        {
            using var reader = new MemoryPackReader(buffer, state);
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

        using var state = MemoryPackReaderOptionalStatePool.Rent(context);
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
        MemoryPackSerializerContext? context = null,
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
