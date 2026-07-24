using System.Buffers;

namespace MemoryPack.Internal;

internal unsafe struct SpanBufferWriter : IBufferWriter<byte>
{
    readonly byte* pointer;
    readonly int length;
    int written;

    internal int WrittenCount => written;

    internal SpanBufferWriter(byte* pointer, int length)
    {
        this.pointer = pointer;
        this.length = length;
        written = 0;
    }

    public void Advance(int count)
    {
        if ((uint)count > (uint)(length - written))
        {
            throw new InsufficientDestinationBufferException();
        }

        written += count;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if ((uint)sizeHint > (uint)(length - written))
        {
            throw new InsufficientDestinationBufferException();
        }

        return new Span<byte>(pointer + written, length - written);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
        => throw new NotSupportedException(
            "MemoryPack's span writer only supports GetSpan.");
}

internal sealed class InsufficientDestinationBufferException : Exception;
