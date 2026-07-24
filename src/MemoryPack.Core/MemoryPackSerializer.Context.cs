
using MemoryPack.Internal;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MemoryPack;

public static partial class MemoryPackSerializer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(in T? value, MemoryPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            return SerializeCore(value);
        }

        return SerializeWithContext(value, context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static byte[] SerializeWithContext<T>(
        in T? value,
        MemoryPackSerializerContext context)
    {
        context.EnsureRootType<T>();
        var state = AcquireWriterState();
        state.BufferWriter.Reset();
        state.OptionalState.Init(context);
        try
        {
            var writer = new MemoryPackWriter<ReusableLinkedArrayBufferWriter>(
                ref state.BufferWriter,
                state.BufferWriter.DangerousGetFirstBuffer(),
                state.OptionalState);
            writer.WriteValue(value);
            writer.Flush();
            return state.BufferWriter.ToArrayAndReset();
        }
        finally
        {
            state.Reset();
            state.Exit();
        }
    }

    public static int Serialize<T, TBufferWriter>(
        ref TBufferWriter bufferWriter,
        scoped in T? value,
        MemoryPackSerializerContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            return Serialize(ref bufferWriter, value);
        }

        context.EnsureRootType<T>();

        var optionalState = AcquireWriterOptionalState();
        optionalState.Init(context);
        try
        {
            var writer = new MemoryPackWriter<TBufferWriter>(ref bufferWriter, optionalState);
            writer.WriteValue(value);
            var written = writer.WrittenCount;
            writer.Flush();
            return written;
        }
        finally
        {
            optionalState.Reset();
            optionalState.Exit();
        }
    }

    public static int Serialize<T, TBufferWriter>(
        TBufferWriter bufferWriter,
        scoped in T? value,
        MemoryPackSerializerContext context)
        where TBufferWriter : class, IBufferWriter<byte>
        => Serialize(ref bufferWriter, value, context);

    public static async ValueTask SerializeAsync<T>(
        Stream stream,
        T? value,
        MemoryPackSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            await SerializeAsync(
                stream,
                value,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        context.EnsureRootType<T>();
        var tempWriter = ReusableLinkedArrayBufferWriterPool.Rent();
        try
        {
            _ = Serialize(ref tempWriter, value, context);
            await tempWriter.WriteToAndResetAsync(stream, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReusableLinkedArrayBufferWriterPool.Return(tempWriter);
        }
    }

    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(ReadOnlySpan<byte> buffer, MemoryPackSerializerContext context)
    {
        T? value = default;
        Deserialize(buffer, ref value, context);
        return value;
    }

    public static int Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        ReadOnlySpan<byte> buffer,
        ref T? value,
        MemoryPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            return Deserialize(buffer, ref value);
        }

        context.EnsureRootType<T>();
        var state = AcquireReaderOptionalState();
        state.Init(context);
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
        T>(
        in ReadOnlySequence<byte> buffer,
        MemoryPackSerializerContext context)
    {
        T? value = default;
        Deserialize(buffer, ref value, context);
        return value;
    }

    public static int Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        in ReadOnlySequence<byte> buffer,
        ref T? value,
        MemoryPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            return Deserialize(buffer, ref value);
        }

        context.EnsureRootType<T>();
        var state = AcquireReaderOptionalState();
        state.Init(context);
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
        T>(
        Stream stream,
        MemoryPackSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CanUseDefaultPath<T>())
        {
            return await DeserializeAsync<T>(
                stream,
                cancellationToken).ConfigureAwait(false);
        }

        context.EnsureRootType<T>();
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? value = default;
            var consumed = Deserialize(
                segment.AsSpan(checked((int)memoryStream.Position)),
                ref value,
                context);
            memoryStream.Seek(consumed, SeekOrigin.Current);
            return value;
        }

        var builder = ReusableReadOnlySequenceBuilderPool.Rent();
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            var offset = 0;
            while (true)
            {
                if (offset == buffer.Length)
                {
                    builder.Add(buffer, returnToPool: true);
                    buffer = ArrayPool<byte>.Shared.Rent(
                        Math.Min(
                            MathEx.NewArrayCapacity(buffer.Length),
                            MaximumStreamSegmentSize));
                    offset = 0;
                }

                int read;
                try
                {
                    read = await stream.ReadAsync(
                        buffer.AsMemory(offset, buffer.Length - offset),
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }

                offset += read;
                if (read == 0)
                {
                    builder.Add(buffer.AsMemory(0, offset), returnToPool: true);
                    break;
                }
            }

            if (builder.TryGetSingleMemory(out var memory))
            {
                return Deserialize<T>(memory.Span, context);
            }

            var sequence = builder.Build();
            return Deserialize<T>(sequence, context);
        }
        finally
        {
            ReusableReadOnlySequenceBuilderPool.Return(builder);
        }
    }
}
