using SharpPack.Formatters;
using SharpPack.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

// Array and Array-like type formatters
// T[]
// T[] where T: unmnaged
// Memory
// ReadOnlyMemory
// ArraySegment
// ReadOnlySequence

namespace SharpPack.Formatters
{
    [Preserve]
    public sealed class UnmanagedArrayFormatter<T> : SharpPackFormatter<T[]>
            where T : unmanaged
    {
        internal override SharpPackFormatter<T[]> BindContext(
            FormatterGraph graph)
            => graph.HasFormatterOverride<T>()
                ? new ContextArrayFormatter<T>()
                : new DangerousUnmanagedArrayFormatter<T>();

        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T[]? value)
        {
            writer.WriteArray(value);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref T[]? value)
        {
            reader.ReadArray(ref value);
        }
    }

    [Preserve]
    public sealed class DangerousUnmanagedArrayFormatter<T> : SharpPackFormatter<T[]>
    {
        internal override SharpPackFormatter<T[]> BindContext(
            FormatterGraph graph)
            => graph.HasFormatterOverride<T>()
                ? new ContextArrayFormatter<T>()
                : this;

        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T[]? value)
        {
            writer.DangerousWriteUnmanagedArray(value);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref T[]? value)
        {
            reader.DangerousReadUnmanagedArray(ref value);
        }
    }

    [Preserve]
    internal sealed class ContextArrayFormatter<T>
        : SharpPackFormatter<T[]>, ISharpPackContextOverrideFormatter
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(
            ref SharpPackWriter<TBufferWriter> writer,
            scoped ref T[]? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            writer.WriteCollectionHeader(value.Length);
            var formatter = writer.GetFormatter<T>();
            for (int i = 0; i < value.Length; i++)
            {
                formatter.Serialize(
                    ref writer,
                    ref Unsafe.As<T, T?>(ref value[i]));
            }
        }

        [Preserve]
        public override void Deserialize(
            ref SharpPackReader reader,
            scoped ref T[]? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Array.Empty<T>();
                return;
            }

            if (value == null || value.Length != length)
            {
                value = new T[length];
            }

            var formatter = reader.GetFormatter<T>();
            for (int i = 0; i < length; i++)
            {
                formatter.Deserialize(
                    ref reader,
                    ref Unsafe.As<T, T?>(ref value[i]));
            }
        }
    }

    [Preserve]
    public sealed class ArrayFormatter<T> : SharpPackFormatter<T?[]>
    {
        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T?[]? value)
        {
            writer.WriteArray(value);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref T?[]? value)
        {
            reader.ReadArray(ref value);
        }
    }

    [Preserve]
    public sealed class ArraySegmentFormatter<T> : SharpPackFormatter<ArraySegment<T?>>
    {
        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ArraySegment<T?> value)
        {
            writer.WriteSpan(value.AsMemory().Span);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ArraySegment<T?> value)
        {
            var array = reader.ReadArray<T>();
            value = (array == null) ? default : (ArraySegment<T?>)array;
        }
    }

    [Preserve]
    public sealed class MemoryFormatter<T> : SharpPackFormatter<Memory<T?>>
    {
        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Memory<T?> value)
        {
            writer.WriteSpan(value.Span);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Memory<T?> value)
        {
            value = reader.ReadArray<T>();
        }
    }

    [Preserve]
    public sealed class ReadOnlyMemoryFormatter<T> : SharpPackFormatter<ReadOnlyMemory<T?>>
    {
        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ReadOnlyMemory<T?> value)
        {
            writer.WriteSpan(value.Span);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ReadOnlyMemory<T?> value)
        {
            value = reader.ReadArray<T>();
        }
    }

    [Preserve]
    public sealed class ReadOnlySequenceFormatter<T> : SharpPackFormatter<ReadOnlySequence<T?>>
    {
        internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
            => graph.HasFormatterOverride<T>();

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ReadOnlySequence<T?> value)
        {
            if (value.IsSingleSegment)
            {
                writer.WriteSpan(value.FirstSpan);
                return;
            }

            writer.WriteCollectionHeader(checked((int)value.Length));
            foreach (var memory in value)
            {
                writer.WriteSpanWithoutLengthHeader(memory.Span);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ReadOnlySequence<T?> value)
        {
            var array = reader.ReadArray<T>();
            value = (array == null) ? default : new ReadOnlySequence<T?>(array);
        }
    }

    [Preserve]
    public sealed class MemoryPoolFormatter<T> : SharpPackFormatter<Memory<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Memory<T?> value)
        {
            writer.WriteSpan(value.Span);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Memory<T?> value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Memory<T?>.Empty;
                return;
            }

            var array = ArrayPool<T?>.Shared.Rent(length);
            try
            {
                var memory = array.AsMemory(0, length);
                var span = memory.Span;
                reader.ReadSpanWithoutReadLengthHeader(length, ref span);
                value = memory;
            }
            catch
            {
                ArrayPool<T?>.Shared.Return(
                    array,
                    clearArray:
                        RuntimeHelpers.IsReferenceOrContainsReferences<T?>());
                throw;
            }
        }
    }

    [Preserve]
    public sealed class ReadOnlyMemoryPoolFormatter<T> : SharpPackFormatter<ReadOnlyMemory<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ReadOnlyMemory<T?> value)
        {
            writer.WriteSpan(value.Span);
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ReadOnlyMemory<T?> value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (length == 0)
            {
                value = Memory<T?>.Empty;
                return;
            }

            var array = ArrayPool<T?>.Shared.Rent(length);
            try
            {
                var memory = array.AsMemory(0, length);
                var span = memory.Span;
                reader.ReadSpanWithoutReadLengthHeader(length, ref span);
                value = memory;
            }
            catch
            {
                ArrayPool<T?>.Shared.Return(
                    array,
                    clearArray:
                        RuntimeHelpers.IsReferenceOrContainsReferences<T?>());
                throw;
            }
        }
    }
}
