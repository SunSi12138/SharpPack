using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpPack.CompilerServices;

/// <summary>Value-type adapter for a generated serializer's temporary buffer lease.</summary>
/// <remarks>Return the matching lease once in a finally block. Do not use a returned lease.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SharpPackPooledBufferWriter : IBufferWriter<byte>
{
    readonly Internal.ReusableLinkedArrayBufferWriter inner;

    SharpPackPooledBufferWriter(Internal.ReusableLinkedArrayBufferWriter inner)
        => this.inner = inner;

    public static SharpPackPooledBufferWriter Rent(out long leaseId)
        => new(Internal.ReusableLinkedArrayBufferWriterPool.Rent(out leaseId));

    public static void Return(SharpPackPooledBufferWriter writer, long leaseId)
        => Internal.ReusableLinkedArrayBufferWriterPool.Return(writer.inner, leaseId);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => inner.Advance(count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int sizeHint = 0) => inner.GetMemory(sizeHint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0) => inner.GetSpan(sizeHint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteToAndReset<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer)
        where TBufferWriter : IBufferWriter<byte>
        => inner.WriteToAndReset(ref writer);
}
