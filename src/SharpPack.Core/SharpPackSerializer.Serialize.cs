using SharpPack.Internal;
using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPack;

using static MemoryMarshal;
using static GC;

public static partial class SharpPackSerializer
{
    static readonly Lock runtimeOptionsLock = new();
    static SharpPackSerializerRuntimeOptions runtimeOptions =
        SharpPackSerializerRuntimeOptions.Default;
    static bool runtimeOptionsFrozen;

    [ThreadStatic]
    static SerializerWriterThreadStaticState? threadStaticState;
    [ThreadStatic]
    static SharpPackWriterOptionalState? threadStaticWriterOptionalState;

    /// <summary>
    /// Configures process-wide resource settings for byte-array serialization.
    /// This must be called during startup, before the first retained byte-array
    /// serializer state is created.
    /// </summary>
    public static void ConfigureRuntime(
        SharpPackSerializerRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ThreadBufferSize <= 0 ||
            options.ThreadBufferSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"ThreadBufferSize must be between 1 and {Array.MaxLength}.");
        }

        lock (runtimeOptionsLock)
        {
            if (runtimeOptionsFrozen)
            {
                throw new InvalidOperationException(
                    "SharpPack runtime options are frozen after the retained byte-array serializer state is initialized.");
            }

            runtimeOptions = options;
        }
    }

    static SharpPackSerializerRuntimeOptions GetRuntimeOptionsAndFreeze()
    {
        lock (runtimeOptionsLock)
        {
            runtimeOptionsFrozen = true;
            return runtimeOptions;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(in T? value)
        => !RuntimeHelpers.IsReferenceOrContainsReferences<T>()
            ? SerializeUnmanaged(value)
            : SerializeReference(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte[] SerializeReference<T>(in T? value)
    {
        var typeKind = TypeHelpers.TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(
            out var elementSize);
        if (typeKind == TypeHelpers.TypeKind.UnmanagedSZArray)
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
        if (typeKind == TypeHelpers.TypeKind.FixedSizeSharpPackable)
        {
            var buffer = new byte[(value == null) ? 1 : elementSize];
            var bufferWriter = new FixedArrayBufferWriter(buffer);
            var writer = new SharpPackWriter<FixedArrayBufferWriter>(ref bufferWriter, buffer, SharpPackWriterOptionalState.NullState);
            Serialize(ref writer, value);
            return bufferWriter.GetFilledBuffer();
        }
        if (typeKind == TypeHelpers.TypeKind.ExactSizeSharpPackable)
        {
            if (value == null)
            {
                return [SharpPackCode.NullObject];
            }
            return ((ISharpPackExactSizeSerializable<T>)(object)value)
                .SerializeExact();
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
    static byte[] SerializeUnmanaged<T>(in T? value)
    {
        var array = AllocateUninitializedArray<byte>(Unsafe.SizeOf<T>());
        Unsafe.WriteUnaligned(ref GetArrayDataReference(array), value);
        return array;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T, TBufferWriter>(
        ref TBufferWriter bufferWriter,
        scoped in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return SerializeUnmanaged(ref bufferWriter, value);
        }

        var typeKind = TypeHelpers.TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(
            out var elementSize);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int SerializeUnmanaged<T, TBufferWriter>(
        ref TBufferWriter bufferWriter,
        scoped in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        var buffer = bufferWriter.GetSpan(Unsafe.SizeOf<T>());
        Unsafe.WriteUnaligned(
            ref MemoryMarshal.GetReference(buffer),
            value);
        bufferWriter.Advance(Unsafe.SizeOf<T>());
        return Unsafe.SizeOf<T>();
    }

    public static int Serialize<T, TBufferWriter>(
        TBufferWriter bufferWriter,
        scoped in T? value)
        where TBufferWriter : class, IBufferWriter<byte>
        => Serialize(ref bufferWriter, value);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SharpPackWriter<SharpPackExactArrayBufferWriter>
        CreateExactWriter(
            ref SharpPackExactArrayBufferWriter bufferWriter)
        => new(
            ref bufferWriter,
            bufferWriter.DangerousGetBuffer(),
            SharpPackWriterOptionalState.NullState);

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

        public SerializerWriterThreadStaticState(bool retainConfiguredBuffer)
        {
            if (retainConfiguredBuffer)
            {
                var options = GetRuntimeOptionsAndFreeze();
                BufferWriter = new ReusableLinkedArrayBufferWriter(
                    useFirstBuffer: true,
                    pinned: options.PinThreadBuffer,
                    firstBufferSize: options.ThreadBufferSize);
            }
            else
            {
                BufferWriter = new ReusableLinkedArrayBufferWriter(
                    useFirstBuffer: false,
                    pinned: false);
            }
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
            state = threadStaticState = new SerializerWriterThreadStaticState(
                retainConfiguredBuffer: true);
        }

        if (state.TryEnter())
        {
            return state;
        }

        var nestedState = new SerializerWriterThreadStaticState(
            retainConfiguredBuffer: false);
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
