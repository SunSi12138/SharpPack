using SharpPack.Internal;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPack;

using static MemoryMarshal;
using static GC;

public static partial class SharpPackSerializer
{
    [ThreadStatic]
    static SerializerWriterThreadStaticState? threadStaticState;
    [ThreadStatic]
    static SharpPackWriterOptionalState? threadStaticWriterOptionalState;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(in T? value)
        => SerializeCore(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte[] SerializeCore<T>(in T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var array = AllocateUninitializedArray<byte>(Unsafe.SizeOf<T>());
            Unsafe.WriteUnaligned(ref GetArrayDataReference(array), value);
            return array;
        }
        var typeKind = TypeHelpers.TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(out var elementSize);
        if (typeKind == TypeHelpers.TypeKind.None)
        {
            // do nothing
        }
        else if (typeKind == TypeHelpers.TypeKind.UnmanagedSZArray)
        {
            if (value == null)
            {
                return SharpPackCode.NullCollectionData.ToArray();
            }

            var srcArray = ((Array)(object)value!);
            var length = srcArray.Length;
            if (length == 0)
            {
                return new byte[4] { 0, 0, 0, 0 };
            }

            var dataSize = checked(elementSize * length);
            var destArray = AllocateUninitializedArray<byte>(checked(dataSize + 4));
            ref var head = ref MemoryMarshal.GetArrayDataReference(destArray);

            Unsafe.WriteUnaligned(ref head, length);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref head, 4), ref MemoryMarshal.GetArrayDataReference(srcArray), (uint)dataSize);

            return destArray;
        }
        else if (typeKind == TypeHelpers.TypeKind.FixedSizeSharpPackable)
        {
            var buffer = new byte[(value == null) ? 1 : elementSize];
            var bufferWriter = new FixedArrayBufferWriter(buffer);
            var writer = new SharpPackWriter<FixedArrayBufferWriter>(ref bufferWriter, buffer, SharpPackWriterOptionalState.NullState);
            Serialize(ref writer, value);
            return bufferWriter.GetFilledBuffer();
        }

        var state = AcquireWriterState();

        try
        {
            var writer = new SharpPackWriter<ReusableLinkedArrayBufferWriter>(ref state.BufferWriter, state.BufferWriter.DangerousGetFirstBuffer(), state.OptionalState);
            Serialize(ref writer, value);
            return state.BufferWriter.ToArrayAndReset();
        }
        finally
        {
            state.Reset();
            state.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T, TBufferWriter>(
        ref TBufferWriter bufferWriter,
        scoped in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var buffer = bufferWriter.GetSpan(Unsafe.SizeOf<T>());
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
            bufferWriter.Advance(Unsafe.SizeOf<T>());
            return Unsafe.SizeOf<T>();
        }
        var typeKind = TypeHelpers.TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(out var elementSize);
        if (typeKind == TypeHelpers.TypeKind.UnmanagedSZArray)
        {
            if (value == null)
            {
                var span = bufferWriter.GetSpan(4);
                SharpPackCode.NullCollectionData.CopyTo(span);
                bufferWriter.Advance(4);
                return 4;
            }

            var srcArray = ((Array)(object)value!);
            var length = srcArray.Length;
            if (length == 0)
            {
                var span = bufferWriter.GetSpan(4);
                SharpPackCode.ZeroCollectionData.CopyTo(span);
                bufferWriter.Advance(4);
                return 4;
            }

            var dataSize = checked(elementSize * length);
            var totalSize = checked(dataSize + 4);
            var destSpan = bufferWriter.GetSpan(totalSize);
            ref var head = ref MemoryMarshal.GetReference(destSpan);

            Unsafe.WriteUnaligned(ref head, length);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref head, 4), ref MemoryMarshal.GetArrayDataReference(srcArray), (uint)dataSize);

            bufferWriter.Advance(totalSize);
            return totalSize;
        }

        var state = AcquireWriterOptionalState();

        try
        {
            var writer = new SharpPackWriter<TBufferWriter>(ref bufferWriter, state);
            return Serialize(ref writer, value);
        }
        finally
        {
            state.ResetAndExit();
        }
    }

    public static int Serialize<T, TBufferWriter>(
        TBufferWriter bufferWriter,
        scoped in T? value)
        where TBufferWriter : class, IBufferWriter<byte>
        => Serialize(ref bufferWriter, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T, TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.WriteValue(value);
        var written = writer.WrittenCount;
        writer.Flush();
        return written;
    }

    public static async ValueTask SerializeAsync<T>(
        Stream stream,
        T? value,
        CancellationToken cancellationToken = default)
    {
        var tempWriter = ReusableLinkedArrayBufferWriterPool.Rent(
            out var tempWriterLeaseId);
        try
        {
            _ = Serialize(ref tempWriter, value);
            await tempWriter.WriteToAndResetAsync(stream, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReusableLinkedArrayBufferWriterPool.Return(
                tempWriter,
                tempWriterLeaseId);
        }
    }

    sealed class SerializerWriterThreadStaticState
    {
        bool isInUse;

        public ReusableLinkedArrayBufferWriter BufferWriter;
        public SharpPackWriterOptionalState OptionalState;

        public SerializerWriterThreadStaticState()
        {
            BufferWriter = new ReusableLinkedArrayBufferWriter(
                useFirstBuffer: true,
                pinned: false);
            OptionalState = new SharpPackWriterOptionalState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter()
        {
            if (isInUse)
            {
                return false;
            }

            isInUse = true;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Exit()
            => isInUse = false;

        public void Init(SharpPackSerializerContext context)
        {
            OptionalState.Init(context);
        }

        public void Reset()
        {
            BufferWriter.Reset();
            OptionalState.ResetAndExit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SerializerWriterThreadStaticState AcquireWriterState()
    {
        var state = threadStaticState;
        if (state is null)
        {
            state = threadStaticState = new SerializerWriterThreadStaticState();
        }

        if (state.TryEnter())
        {
            return state;
        }

        var nestedState = new SerializerWriterThreadStaticState();
        _ = nestedState.TryEnter();
        return nestedState;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SharpPackWriterOptionalState AcquireWriterOptionalState()
    {
        var state = threadStaticWriterOptionalState;
        if (state is null)
        {
            state = threadStaticWriterOptionalState =
                new SharpPackWriterOptionalState();
        }

        if (state.TryEnter())
        {
            return state;
        }

        var nestedState = new SharpPackWriterOptionalState();
        _ = nestedState.TryEnter();
        return nestedState;
    }
}
