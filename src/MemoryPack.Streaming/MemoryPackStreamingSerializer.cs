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

        var tempWriter = ReusableLinkedArrayBufferWriterPool.Rent();
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
            ReusableLinkedArrayBufferWriterPool.Return(tempWriter);
        }
    }

    public static async IAsyncEnumerable<T?> DeserializeAsync<T>(
        PipeReader pipeReader,
        int bufferAtLeast = 4096,
        int readMinimumSize = 8192,
        MemoryPackSerializerContext? context = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        static bool ReadCollectionHeader(in ReadOnlySequence<byte> buffer, MemoryPackReaderOptionalState state, out int length)
        {
            using var reader = new MemoryPackReader(buffer, state);

            // allow to use `Dangerous` read header.
            return reader.DangerousTryReadCollectionHeader(out length);
        }

        static (int Consumed, int Remain) Deserialize(
            in ReadOnlySequence<byte> buffer,
            int bufferAtLeast,
            List<T?> itemBuffer,
            int remain,
            bool bufferIsFull,
            MemoryPackReaderOptionalState state)
        {
            using var reader = new MemoryPackReader(buffer, state);

            while (bufferIsFull || bufferAtLeast < reader.Remaining)
            {
                if (remain == 0)
                {
                    return (reader.Consumed, remain);
                }

                itemBuffer.Add(reader.ReadValue<T?>());
                remain--;
            }

            return (reader.Consumed, remain);
        }

        if (readMinimumSize < bufferAtLeast)
        {
            throw new ArgumentException($"readMinimumSize must larger than bufferAtLeast. readMinimumSize: {readMinimumSize} bufferAtLeast:{bufferAtLeast}");
        }

        using var state = MemoryPackReaderOptionalStatePool.Rent(context);

        var itemBuffer = new List<T?>();
        var readResult = await pipeReader.ReadAtLeastAsync(readMinimumSize, cancellationToken).ConfigureAwait(false);

        if (!readResult.IsCanceled)
        {
            var buffer = readResult.Buffer;
            if (ReadCollectionHeader(buffer, state, out var length))
            {
                pipeReader.AdvanceTo(buffer.GetPosition(4));
                if (readResult.IsCompleted)
                {
                    buffer = buffer.Slice(4);
                }

                var remain = length;
                if (remain > 0)
                {
                    itemBuffer.EnsureCapacity(Math.Min(remain, 256));
                }

                while (remain != 0)
                {
                    if (!readResult.IsCompleted)
                    {
                        readResult = await pipeReader.ReadAtLeastAsync(readMinimumSize, cancellationToken).ConfigureAwait(false);
                        buffer = readResult.Buffer;
                    }

                    if (readResult.IsCanceled)
                    {
                        yield break;
                    }

                    var result = Deserialize(
                        buffer,
                        bufferAtLeast,
                        itemBuffer,
                        remain,
                        readResult.IsCompleted,
                        state);
                    var consumedByteCount = result.Consumed;
                    remain = result.Remain;

                    if (itemBuffer.Count > 0)
                    {
                        foreach (var item in itemBuffer)
                        {
                            yield return item;
                        }
                        itemBuffer.Clear();
                    }

                    if (readResult.IsCompleted)
                    {
                        buffer = buffer.Slice(consumedByteCount);

                        if (consumedByteCount == 0 || buffer.Length == 0)
                        {
                            await pipeReader.CompleteAsync().ConfigureAwait(false);
                            yield break;
                        }
                    }
                    else
                    {
                        pipeReader.AdvanceTo(buffer.GetPosition(consumedByteCount));
                    }
                }
            }
        }

        foreach (var item in itemBuffer)
        {
            yield return item;
        }
    }

    public static IAsyncEnumerable<T?> DeserializeAsync<T>(
        Stream stream,
        int bufferAtLeast = 4096,
        int readMinimumSize = 8192,
        MemoryPackSerializerContext? context = null,
        CancellationToken cancellationToken = default)
    {
        return DeserializeAsync<T>(
            PipeReader.Create(stream),
            bufferAtLeast,
            readMinimumSize,
            context,
            cancellationToken);
    }
}
