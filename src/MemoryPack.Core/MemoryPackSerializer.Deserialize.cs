using MemoryPack.Internal;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryPack;

public static partial class MemoryPackSerializer
{
    [ThreadStatic]
    static MemoryPackReaderOptionalState? threadStaticReaderOptionalState;

    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(ReadOnlySpan<byte> buffer)
    {
        T? value = default;
        Deserialize(buffer, ref value);
        return value;
    }

    public static int Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(ReadOnlySpan<byte> buffer, ref T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
            {
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), buffer.Length);
            }
            value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));
            return Unsafe.SizeOf<T>();
        }

        var state = AcquireReaderOptionalState();
        state.InitDefault();

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
            state.Exit();
        }
    }

    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(in ReadOnlySequence<byte> buffer)
    {
        T? value = default;
        Deserialize<T>(buffer, ref value);
        return value;
    }

    public static int Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(in ReadOnlySequence<byte> buffer, ref T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            int sizeOfT = Unsafe.SizeOf<T>();
            if (buffer.Length < sizeOfT)
            {
                MemoryPackSerializationException.ThrowInvalidRange(Unsafe.SizeOf<T>(), (int)buffer.Length);
            }

            ReadOnlySequence<byte> sliced = buffer.Slice(0, sizeOfT);

            if (sliced.IsSingleSegment)
            {
                value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(sliced.FirstSpan));
                return sizeOfT;
            }
            else
            {
                // We can't read directly from ReadOnlySequence<byte> to T, so we copy to a temp array.
                // if less than 512 bytes, use stackalloc, otherwise use MemoryPool<byte>
                byte[]? tempArray = null;

                Span<byte> tempSpan = sizeOfT <= 512 ? stackalloc byte[sizeOfT] : default;

                try
                {
                    if (sizeOfT > 512)
                    {
                        tempArray = ArrayPool<byte>.Shared.Rent(sizeOfT);
                        tempSpan = tempArray;
                    }

                    sliced.CopyTo(tempSpan);
                    value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(tempSpan));
                    return sizeOfT;
                }
                finally
                {
                    if (tempArray is not null)
                    {
                        ArrayPool<byte>.Shared.Return(tempArray);
                    }
                }
            }
        }

        var state = AcquireReaderOptionalState();
        state.InitDefault();

        var reader = new MemoryPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.Reset();
            state.Exit();
        }
    }

    public static async ValueTask<T?> DeserializeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> streamBuffer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? value = default;
            var bytesRead = Deserialize<T>(streamBuffer.AsSpan(checked((int)ms.Position)), ref value);

            // Emulate that we had actually "read" from the stream.
            ms.Seek(bytesRead, SeekOrigin.Current);

            return value;
        }

        var builder = ReusableReadOnlySequenceBuilderPool.Rent();
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536); // initial 64K
            var offset = 0;
            do
            {
                if (offset == buffer.Length)
                {
                    builder.Add(buffer, returnToPool: true);
                    buffer = ArrayPool<byte>.Shared.Rent(MathEx.NewArrayCapacity(buffer.Length));
                    offset = 0;
                }

                int read = 0;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // buffer is not added in builder, so return here.
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }

                offset += read;

                if (read == 0)
                {
                    builder.Add(buffer.AsMemory(0, offset), returnToPool: true);
                    break;

                }
            } while (true);

            // If single buffer, we can avoid ReadOnlySequence build cost.
            if (builder.TryGetSingleMemory(out var memory))
            {
                return Deserialize<T>(memory.Span);
            }
            else
            {
                var seq = builder.Build();
                var result = Deserialize<T>(seq);
                return result;
            }
        }
        finally
        {
            ReusableReadOnlySequenceBuilderPool.Return(builder);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static MemoryPackReaderOptionalState AcquireReaderOptionalState()
    {
        var state = threadStaticReaderOptionalState;
        if (state is null)
        {
            state = threadStaticReaderOptionalState =
                new MemoryPackReaderOptionalState();
        }

        if (state.TryEnter())
        {
            return state;
        }

        var nestedState = new MemoryPackReaderOptionalState();
        _ = nestedState.TryEnter();
        return nestedState;
    }
}
