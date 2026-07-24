
using SharpPack.Internal;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharpPack;

public static partial class SharpPackSerializer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(in T? value, SharpPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SerializeWithContext(value, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static byte[] SerializeWithContext<T>(
        in T? value,
        SharpPackSerializerContext context)
    {
        var state = AcquireWriterState();
        state.OptionalState.Init(context);
        try
        {
            var writer = new SharpPackWriter<ReusableLinkedArrayBufferWriter>(
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T, TBufferWriter>(
        ref TBufferWriter bufferWriter,
        scoped in T? value,
        SharpPackSerializerContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        ArgumentNullException.ThrowIfNull(context);

        var optionalState = AcquireWriterOptionalState();
        optionalState.Init(context);
        try
        {
            var writer = new SharpPackWriter<TBufferWriter>(ref bufferWriter, optionalState);
            writer.WriteValue(value);
            var written = writer.WrittenCount;
            writer.Flush();
            return written;
        }
        finally
        {
            optionalState.ResetAndExit();
        }
    }

    public static int Serialize<T, TBufferWriter>(
        TBufferWriter bufferWriter,
        scoped in T? value,
        SharpPackSerializerContext context)
        where TBufferWriter : class, IBufferWriter<byte>
        => Serialize(ref bufferWriter, value, context);

    public static async ValueTask SerializeAsync<T>(
        Stream stream,
        T? value,
        SharpPackSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tempWriter = ReusableLinkedArrayBufferWriterPool.Rent(
            out var tempWriterLeaseId);
        try
        {
            _ = Serialize(ref tempWriter, value, context);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(ReadOnlySpan<byte> buffer, SharpPackSerializerContext context)
    {
        T? value = default;
        Deserialize(buffer, ref value, context);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        ReadOnlySpan<byte> buffer,
        ref T? value,
        SharpPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureRootType<T>();
        var state = AcquireReaderOptionalState();
        state.Init(context);
        var reader = new SharpPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.ResetAndExit();
        }
    }

    public static T? Deserialize<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        in ReadOnlySequence<byte> buffer,
        SharpPackSerializerContext context)
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
        SharpPackSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureRootType<T>();
        var state = AcquireReaderOptionalState();
        state.Init(context);
        var reader = new SharpPackReader(buffer, state);
        try
        {
            reader.ReadValue(ref value);
            return reader.Consumed;
        }
        finally
        {
            reader.Dispose();
            state.ResetAndExit();
        }
    }

    /// <summary>
    /// Deserializes from the stream's remaining contents using the supplied
    /// serializer context.
    /// </summary>
    /// <remarks>
    /// A buffer-backed <see cref="MemoryStream"/> advances by the bytes consumed
    /// by one value. Other streams are read through end-of-stream because a
    /// general <see cref="Stream"/> cannot return bytes read past that value.
    /// Use the payload-length overload for framed or concatenated messages.
    /// </remarks>
    public static async ValueTask<T?> DeserializeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        Stream stream,
        SharpPackSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
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

        var builder = ReusableReadOnlySequenceBuilderPool.Rent(
            out var builderLeaseId);
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
            ReusableReadOnlySequenceBuilderPool.Return(
                builder,
                builderLeaseId);
        }
    }

    /// <summary>
    /// Deserializes exactly one length-delimited payload using the supplied
    /// serializer context.
    /// </summary>
    public static ValueTask<T?> DeserializeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        T>(
        Stream stream,
        int payloadLength,
        SharpPackSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DeserializeLengthDelimitedAsync<T>(
            stream,
            payloadLength,
            context,
            cancellationToken);
    }
}
