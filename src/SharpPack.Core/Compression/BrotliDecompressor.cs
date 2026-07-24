using SharpPack.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;

namespace SharpPack.Compression;

public struct BrotliDecompressor : IDisposable
{
    const int DefaultDecompressionSizeLimit = 1024 * 1024 * 128;

    ReusableReadOnlySequenceBuilder? sequenceBuilder;
    long sequenceBuilderLeaseId;
    readonly int decompressionSizeLimit;
    int decompressedLength;

    public BrotliDecompressor()
        : this(DefaultDecompressionSizeLimit)
    {
    }

    public BrotliDecompressor(int decompressionSizeLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decompressionSizeLimit);
        sequenceBuilder = null;
        sequenceBuilderLeaseId = 0;
        this.decompressionSizeLimit = decompressionSizeLimit;
        decompressedLength = 0;
    }

    public ReadOnlySequence<byte> Decompress(ReadOnlySpan<byte> compressedSpan)
    {
        return Decompress(compressedSpan, out _);
    }

    public ReadOnlySequence<byte> Decompress(ReadOnlySpan<byte> compressedSpan, out int consumed)
    {
        if (sequenceBuilder != null)
        {
            SharpPackSerializationException.ThrowAlreadyDecompressed();
        }

        sequenceBuilder = ReusableReadOnlySequenceBuilderPool.Rent(
            out sequenceBuilderLeaseId);
        decompressedLength = 0;
        var decoder = new BrotliDecoder();
        try
        {
            var status = OperationStatus.DestinationTooSmall;
            DecompressCore(ref status, ref decoder, compressedSpan, out consumed);
            if (status == OperationStatus.NeedMoreData)
            {
                SharpPackSerializationException.ThrowCompressionFailed(status);
            }
        }
        catch
        {
            ReleaseBuilder();
            throw;
        }
        finally
        {
            decoder.Dispose();
        }

        return sequenceBuilder.Build();
    }

    public ReadOnlySequence<byte> Decompress(ReadOnlySequence<byte> compressedSequence)
    {
        return Decompress(compressedSequence, out _);
    }

    public ReadOnlySequence<byte> Decompress(ReadOnlySequence<byte> compressedSequence, out int consumed)
    {
        if (sequenceBuilder != null)
        {
            SharpPackSerializationException.ThrowAlreadyDecompressed();
        }

        sequenceBuilder = ReusableReadOnlySequenceBuilderPool.Rent(
            out sequenceBuilderLeaseId);
        decompressedLength = 0;
        var decoder = new BrotliDecoder();
        try
        {
            var status = OperationStatus.DestinationTooSmall;
            consumed = 0;
            foreach (var item in compressedSequence)
            {
                DecompressCore(ref status, ref decoder, item.Span, out var bytesConsumed);
                if (bytesConsumed > int.MaxValue - consumed)
                {
                    SharpPackSerializationException.ThrowSizeOverflow();
                }
                consumed += bytesConsumed;
                if (status == OperationStatus.Done)
                {
                    break;
                }
            }

            if (status == OperationStatus.NeedMoreData)
            {
                SharpPackSerializationException.ThrowCompressionFailed(status);
            }
        }
        catch
        {
            ReleaseBuilder();
            throw;
        }
        finally
        {
            decoder.Dispose();
        }

        return sequenceBuilder.Build();
    }

    void DecompressCore(ref OperationStatus status, ref BrotliDecoder decoder, ReadOnlySpan<byte> source, out int consumed)
    {
        Debug.Assert(sequenceBuilder != null);
        consumed = 0;

        byte[]? buffer = null;
        var bufferLength = 0;
        try
        {
            status = OperationStatus.DestinationTooSmall;
            while (status == OperationStatus.DestinationTooSmall)
            {
                if (buffer == null)
                {
                    bufferLength = GetOutputBufferCapacity(source.Length);
                    buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
                }

                status = decoder.Decompress(
                    source,
                    buffer.AsSpan(0, bufferLength),
                    out var bytesConsumed,
                    out var bytesWritten);
                consumed += bytesConsumed;

                if (status == OperationStatus.InvalidData)
                {
                    SharpPackSerializationException.ThrowCompressionFailed(status);
                }

                if (bytesWritten > 0)
                {
                    AddOutput(bytesWritten);
                    sequenceBuilder.Add(buffer.AsMemory(0, bytesWritten), true);
                    buffer = null;
                    bufferLength = 0;
                }

                if (status == OperationStatus.NeedMoreData)
                {
                    if (bytesConsumed > 0)
                    {
                        source = source.Slice(bytesConsumed);
                    }
                    if (source.Length != 0)
                    {
                        SharpPackSerializationException.ThrowCompressionFailed();
                    }

                    return;
                }

                if (bytesConsumed > 0)
                {
                    source = source.Slice(bytesConsumed);
                }
            }
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public void Dispose()
    {
        ReleaseBuilder();
    }

    int GetOutputBufferCapacity(int compressedLength)
    {
        var limit = decompressionSizeLimit == 0
            ? DefaultDecompressionSizeLimit
            : decompressionSizeLimit;
        var remaining = limit - decompressedLength;
        if (remaining <= 0)
        {
            SharpPackSerializationException.ThrowDecompressionSizeLimitExceeded(
                limit,
                decompressedLength);
        }

        var requested = Math.Max(
            4096L,
            Math.Min((long)compressedLength * 2, 256 * 1024));
        return (int)Math.Min(requested, remaining);
    }

    void AddOutput(int bytesWritten)
    {
        var newLength = (long)decompressedLength + bytesWritten;
        var limit = decompressionSizeLimit == 0
            ? DefaultDecompressionSizeLimit
            : decompressionSizeLimit;
        if (newLength > limit)
        {
            SharpPackSerializationException.ThrowDecompressionSizeLimitExceeded(
                limit,
                newLength > int.MaxValue ? int.MaxValue : (int)newLength);
        }
        decompressedLength = (int)newLength;
    }

    void ReleaseBuilder()
    {
        if (sequenceBuilder is null)
        {
            return;
        }

        ReusableReadOnlySequenceBuilderPool.Return(
            sequenceBuilder,
            sequenceBuilderLeaseId);
        sequenceBuilder = null;
        sequenceBuilderLeaseId = 0;
        decompressedLength = 0;
    }
}
