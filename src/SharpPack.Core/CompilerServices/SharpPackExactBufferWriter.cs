using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpPack.CompilerServices;

/// <summary>Fixed-output storage used by generated exact-size serializers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public struct SharpPackExactBufferWriter : IBufferWriter<byte>
{
    // Keep storage flat: nesting the legacy mutable struct adds a byref null
    // check in optimized x64/ARM64 CI samples of generated Flush -> Advance.
    // Validation parity with the retained legacy type is tested explicitly.
    readonly byte[] buffer;
    int written;

    public SharpPackExactBufferWriter(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        this.buffer = buffer;
        written = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0)
            SharpPackSerializationException.ThrowInvalidLength(count);
        if (count > buffer.Length - written)
            SharpPackSerializationException.ThrowInvalidAdvance();
        written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);
        return buffer.AsMemory(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ValidateSizeHint(sizeHint);
        return buffer.AsSpan(written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetFilledBuffer()
    {
        if (written != buffer.Length)
            SharpPackSerializationException.ThrowMessage(
                "The generated exact-size serializer did not fill its payload.");
        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SharpPackWriter<SharpPackExactBufferWriter> CreateWriter(
        ref SharpPackExactBufferWriter bufferWriter)
        => new(ref bufferWriter, bufferWriter.buffer, SharpPackWriterOptionalState.NullState);

    void ValidateSizeHint(int sizeHint)
    {
        if (sizeHint < 0)
            SharpPackSerializationException.ThrowInvalidLength(sizeHint);
        if (sizeHint > buffer.Length - written)
            SharpPackSerializationException.ThrowMessage(
                "The generated exact-size serializer underestimated its payload.");
    }
}
