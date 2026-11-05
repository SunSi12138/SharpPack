using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpPack.CompilerServices;

/// <summary>Fixed-output storage used by generated exact-size serializers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public struct SharpPackExactBufferWriter : IBufferWriter<byte>
{
    Internal.SharpPackExactArrayBufferWriter inner;

    public SharpPackExactBufferWriter(byte[] buffer) => inner = new(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => inner.Advance(count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] GetFilledBuffer() => inner.GetFilledBuffer();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SharpPackWriter<SharpPackExactBufferWriter> CreateWriter(
        ref SharpPackExactBufferWriter bufferWriter)
        => new(ref bufferWriter, bufferWriter.inner.DangerousGetBuffer(),
            SharpPackWriterOptionalState.NullState);
}
