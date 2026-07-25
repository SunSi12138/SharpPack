using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpPack.Internal;

internal struct FixedArrayBufferWriter : IBufferWriter<byte>
{
    byte[] buffer;
    int written;

    public FixedArrayBufferWriter(byte[] buffer)
    {
        this.buffer = buffer;
        this.written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        this.written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        var memory = buffer.AsMemory(written);
        if (memory.Length >= sizeHint)
        {
            return memory;
        }

        SharpPackSerializationException.ThrowMessage("Requested invalid sizeHint.");
        return memory;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        var span = buffer.AsSpan(written);
        if (span.Length >= sizeHint)
        {
            return span;
        }

        SharpPackSerializationException.ThrowMessage("Requested invalid sizeHint.");
        return span;
    }

    public byte[] GetFilledBuffer()
    {
        if (written != buffer.Length)
        {
            SharpPackSerializationException.ThrowMessage("Not filled buffer.");
        }

        return buffer;
    }
}

[System.ComponentModel.EditorBrowsable(
    System.ComponentModel.EditorBrowsableState.Never)]
public struct SharpPackExactArrayBufferWriter : IBufferWriter<byte>
{
    readonly byte[] buffer;
    int written;

    public SharpPackExactArrayBufferWriter(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        this.buffer = buffer;
        written = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(count);
        }
        if (count > buffer.Length - written)
        {
            SharpPackSerializationException.ThrowInvalidAdvance();
        }
        written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);
        return buffer.AsMemory(written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);
        return buffer.AsSpan(written);
    }

    public byte[] GetFilledBuffer()
    {
        if (written != buffer.Length)
        {
            SharpPackSerializationException.ThrowMessage(
                "The generated exact-size serializer did not fill its payload.");
        }
        return buffer;
    }

    internal byte[] DangerousGetBuffer() => buffer;

    void ValidateSizeHint(int sizeHint)
    {
        if (sizeHint < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(sizeHint);
        }
        if (sizeHint > buffer.Length - written)
        {
            SharpPackSerializationException.ThrowMessage(
                "The generated exact-size serializer underestimated its payload.");
        }
    }
}
