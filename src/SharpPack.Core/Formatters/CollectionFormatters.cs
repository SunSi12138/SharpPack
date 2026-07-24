using SharpPack.Formatters;
using SharpPack.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Clear support collection formatters
// List, Stack, Queue, LinkedList, HashSet, PriorityQueue,
// ObservableCollection, Collection
// ConcurrentQueue, ConcurrentStack, ConcurrentBag
// Dictionary, SortedDictionary, SortedList, ConcurrentDictionary

// Not supported clear
// ReadOnlyCollection, ReadOnlyObservableCollection, BlockingCollection

namespace SharpPack.Formatters
{
    [Preserve]
    public static class ListFormatter
    {
        [Preserve]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SerializePackable<T, TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, List<T?>? value)
            where T : ISharpPackable<T>
            where TBufferWriter : IBufferWriter<byte>
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            writer.WritePackableSpan(CollectionsMarshal.AsSpan(value));
        }

        [Preserve]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static List<T?>? DeserializePackable<T>(ref SharpPackReader reader)
            where T : ISharpPackable<T>
        {
            List<T?>? value = default;
            DeserializePackable<T>(ref reader, ref value);
            return value;
        }

        [Preserve]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeserializePackable<T>(ref SharpPackReader reader, scoped ref List<T?>? value)
            where T : ISharpPackable<T>
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new List<T?>(
                    FormatterValidation.InitialCapacity(length));
            }

            if (length > FormatterValidation.MaximumInitialCollectionCapacity &&
                value.Capacity < length)
            {
                value.Clear();
                value.EnsureCapacity(
                    FormatterValidation.MaximumInitialCollectionCapacity);
                for (int i = 0; i < length; i++)
                {
                    value.Add(reader.ReadValue<T>());
                }
                return;
            }

            var span = CollectionsMarshalEx.CreateSpan(value, length);
            reader.ReadPackableSpanWithoutReadLengthHeader(length, ref span);
        }
    }

    [Preserve]
    public sealed class ListFormatter<T> : SharpPackFormatter<List<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref List<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }
            writer.WriteSpan(CollectionsMarshal.AsSpan(value));
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref List<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new List<T?>(
                    FormatterValidation.InitialCapacity(length));
            }

            if (length > FormatterValidation.MaximumInitialCollectionCapacity &&
                value.Capacity < length)
            {
                value.Clear();
                value.EnsureCapacity(
                    FormatterValidation.MaximumInitialCollectionCapacity);
                var formatter = reader.GetFormatter<T?>();
                for (int i = 0; i < length; i++)
                {
                    T? item = default;
                    formatter.Deserialize(ref reader, ref item);
                    value.Add(item);
                }
                return;
            }

            var span = CollectionsMarshalEx.CreateSpan(value, length);
            reader.ReadSpanWithoutReadLengthHeader(length, ref span);
        }
    }

    [Preserve]
    public sealed class StackFormatter<T> : SharpPackFormatter<Stack<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Stack<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            writer.WriteSpan(CollectionsMarshalEx.AsSpan(value));
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Stack<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new Stack<T?>(
                    FormatterValidation.InitialCapacity(length));
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? item = default;
                formatter.Deserialize(ref reader, ref item);
                value.Push(item);
            }
        }
    }

    [Preserve]
    public sealed class QueueFormatter<T> : SharpPackFormatter<Queue<T?>>
    {
        // Queue is circular buffer, can't optimize like List, Stack.

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Queue<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Queue<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new Queue<T?>(
                    FormatterValidation.InitialCapacity(length));
            }
            else
            {
                value.Clear();
                value.EnsureCapacity(
                    FormatterValidation.InitialCapacity(length));
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Enqueue(v);
            }
        }
    }

    [Preserve]
    public sealed class LinkedListFormatter<T> : SharpPackFormatter<LinkedList<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref LinkedList<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref LinkedList<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new LinkedList<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.AddLast(v);
            }
        }
    }

    [Preserve]
    public sealed class HashSetFormatter<T> : SharpPackFormatter<HashSet<T?>>
    {
        readonly IEqualityComparer<T?>? equalityComparer;

        public HashSetFormatter()
            : this(null)
        {
        }

        public HashSetFormatter(IEqualityComparer<T?>? equalityComparer)
        {
            this.equalityComparer = equalityComparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref HashSet<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref HashSet<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new HashSet<T?>(
                    FormatterValidation.InitialCapacity(length),
                    equalityComparer);
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }

    [Preserve]
    public sealed class SortedSetFormatter<T> : SharpPackFormatter<SortedSet<T?>>
    {
        readonly IComparer<T?>? comparer;

        public SortedSetFormatter()
            : this(null)
        {
        }

        public SortedSetFormatter(IComparer<T?>? comparer)
        {
            this.comparer = comparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref SortedSet<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref SortedSet<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new SortedSet<T?>(comparer);
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }


    [Preserve]
    public sealed class PriorityQueueFormatter<TElement, TPriority> : SharpPackFormatter<PriorityQueue<TElement?, TPriority?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref PriorityQueue<TElement?, TPriority?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<(TElement?, TPriority?)>();

            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value.UnorderedItems)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref PriorityQueue<TElement?, TPriority?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new PriorityQueue<TElement?, TPriority?>(
                    FormatterValidation.InitialCapacity(length));
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<(TElement?, TPriority?)>();
            for (int i = 0; i < length; i++)
            {
                (TElement?, TPriority?) v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Enqueue(v.Item1, v.Item2);
            }
        }
    }


    [Preserve]
    public sealed class CollectionFormatter<T> : SharpPackFormatter<Collection<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Collection<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Collection<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new Collection<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }

    [Preserve]
    public sealed class ObservableCollectionFormatter<T> : SharpPackFormatter<ObservableCollection<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ObservableCollection<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ObservableCollection<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new ObservableCollection<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }

    [Preserve]
    public sealed class ConcurrentQueueFormatter<T> : SharpPackFormatter<ConcurrentQueue<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ConcurrentQueue<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            // note: serializing ConcurretnCollection(Queue/Stack/Bag/Dictionary) is not thread-safe.
            // operate Add/Remove in iterating in other thread, not guranteed correct result

            var formatter = writer.GetFormatter<T?>();
            var count = value.Count;
            writer.WriteCollectionHeader(count);
            var i = 0;
            foreach (var item in value)
            {
                i++;
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }

            if (i != count) SharpPackSerializationException.ThrowInvalidConcurrrentCollectionOperation();
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ConcurrentQueue<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new ConcurrentQueue<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Enqueue(v);
            }
        }
    }

    [Preserve]
    public sealed class ConcurrentStackFormatter<T> : SharpPackFormatter<ConcurrentStack<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ConcurrentStack<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            // reverse order in serialize
            var count = value.Count;
            T?[] rentArray = ArrayPool<T?>.Shared.Rent(count);
            try
            {
                var i = 0;
                foreach (var item in value)
                {
                    rentArray[i++] = item;
                }
                if (i != count) SharpPackSerializationException.ThrowInvalidConcurrrentCollectionOperation();

                var formatter = writer.GetFormatter<T?>();
                writer.WriteCollectionHeader(count);
                for (i = i - 1; i >= 0; i--)
                {
                    formatter.Serialize(ref writer, ref rentArray[i]);
                }
            }
            finally
            {
                ArrayPool<T?>.Shared.Return(rentArray, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ConcurrentStack<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new ConcurrentStack<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Push(v);
            }
        }
    }

    [Preserve]
    public sealed class ConcurrentBagFormatter<T> : SharpPackFormatter<ConcurrentBag<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ConcurrentBag<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            var count = value.Count;
            writer.WriteCollectionHeader(count);
            var i = 0;
            foreach (var item in value)
            {
                i++;
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }

            if (i != count) SharpPackSerializationException.ThrowInvalidConcurrrentCollectionOperation();
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ConcurrentBag<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new ConcurrentBag<T?>();
            }
            else
            {
                value.Clear();
            }

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }

    [Preserve]
    public sealed class DictionaryFormatter<TKey, TValue> : SharpPackFormatter<Dictionary<TKey, TValue?>>
        where TKey : notnull
    {
        readonly IEqualityComparer<TKey>? equalityComparer;

        public DictionaryFormatter()
            : this(null)
        {

        }

        public DictionaryFormatter(IEqualityComparer<TKey>? equalityComparer)
        {
            this.equalityComparer = equalityComparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Dictionary<TKey, TValue?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var keyFormatter = writer.GetFormatter<TKey>();
            var valueFormatter = writer.GetFormatter<TValue>();

            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                KeyValuePairFormatter.Serialize(keyFormatter, valueFormatter, ref writer, item!);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref Dictionary<TKey, TValue?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new Dictionary<TKey, TValue?>(
                    FormatterValidation.InitialCapacity(length),
                    equalityComparer);
            }
            else
            {
                value.Clear();
            }

            var keyFormatter = reader.GetFormatter<TKey>();
            var valueFormatter = reader.GetFormatter<TValue>();
            for (int i = 0; i < length; i++)
            {
                KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var k, out var v);
                value.Add(k!, v);
            }
        }
    }

    [Preserve]
    public sealed class SortedDictionaryFormatter<TKey, TValue> : SharpPackFormatter<SortedDictionary<TKey, TValue?>>
        where TKey : notnull
    {
        readonly IComparer<TKey>? comparer;

        public SortedDictionaryFormatter()
            : this(null)
        {

        }

        public SortedDictionaryFormatter(IComparer<TKey>? comparer)
        {
            this.comparer = comparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref SortedDictionary<TKey, TValue?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var keyFormatter = writer.GetFormatter<TKey>();
            var valueFormatter = writer.GetFormatter<TValue>();

            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                KeyValuePairFormatter.Serialize(keyFormatter, valueFormatter, ref writer, item!);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref SortedDictionary<TKey, TValue?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new SortedDictionary<TKey, TValue?>(comparer);
            }
            else
            {
                value.Clear();
            }

            var keyFormatter = reader.GetFormatter<TKey>();
            var valueFormatter = reader.GetFormatter<TValue>();
            for (int i = 0; i < length; i++)
            {
                KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var k, out var v);
                value.Add(k!, v);
            }
        }
    }

    [Preserve]
    public sealed class SortedListFormatter<TKey, TValue> : SharpPackFormatter<SortedList<TKey, TValue?>>
        where TKey : notnull
    {
        readonly IComparer<TKey>? comparer;

        public SortedListFormatter()
            : this(null)
        {

        }

        public SortedListFormatter(IComparer<TKey>? comparer)
        {
            this.comparer = comparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref SortedList<TKey, TValue?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var keyFormatter = writer.GetFormatter<TKey>();
            var valueFormatter = writer.GetFormatter<TValue>();

            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                KeyValuePairFormatter.Serialize(keyFormatter, valueFormatter, ref writer, item!);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref SortedList<TKey, TValue?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = new SortedList<TKey, TValue?>(
                    FormatterValidation.InitialCapacity(length),
                    comparer);
            }
            else
            {
                value.Clear();
            }

            var keyFormatter = reader.GetFormatter<TKey>();
            var valueFormatter = reader.GetFormatter<TValue>();
            for (int i = 0; i < length; i++)
            {
                KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var k, out var v);
                value.Add(k!, v);
            }
        }
    }

    [Preserve]
    public sealed class ConcurrentDictionaryFormatter<TKey, TValue> : SharpPackFormatter<ConcurrentDictionary<TKey, TValue?>>
        where TKey : notnull
    {
        readonly IEqualityComparer<TKey>? equalityComparer;

        public ConcurrentDictionaryFormatter()
            : this(null)
        {

        }

        public ConcurrentDictionaryFormatter(IEqualityComparer<TKey>? equalityComparer)
        {
            this.equalityComparer = equalityComparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ConcurrentDictionary<TKey, TValue?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var keyFormatter = writer.GetFormatter<TKey>();
            var valueFormatter = writer.GetFormatter<TValue>();

            var count = value.Count;
            writer.WriteCollectionHeader(count);
            var i = 0;
            foreach (var item in value)
            {
                i++;
                KeyValuePairFormatter.Serialize(keyFormatter, valueFormatter, ref writer, item!);
            }

            if (i != count) SharpPackSerializationException.ThrowInvalidConcurrrentCollectionOperation();
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ConcurrentDictionary<TKey, TValue?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            if (value == null)
            {
                value = length == 0
                    ? new ConcurrentDictionary<TKey, TValue?>(equalityComparer)
                    : new ConcurrentDictionary<TKey, TValue?>(
                        Environment.ProcessorCount,
                        Math.Min(length, 4096),
                        equalityComparer ?? EqualityComparer<TKey>.Default);
            }
            else
            {
                value.Clear();
            }

            var keyFormatter = reader.GetFormatter<TKey>();
            var valueFormatter = reader.GetFormatter<TValue>();
            for (int i = 0; i < length; i++)
            {
                KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var k, out var v);
                value.TryAdd(k!, v);
            }
        }
    }

    [Preserve]
    public sealed class ReadOnlyCollectionFormatter<T> : SharpPackFormatter<ReadOnlyCollection<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ReadOnlyCollection<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ReadOnlyCollection<T?>? value)
        {
            var array = reader.ReadArray<T?>();

            if (array == null)
            {
                value = null;
            }
            else
            {
                value = new ReadOnlyCollection<T?>(array);
            }
        }
    }

    [Preserve]
    public sealed class ReadOnlyObservableCollectionFormatter<T> : SharpPackFormatter<ReadOnlyObservableCollection<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ReadOnlyObservableCollection<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref ReadOnlyObservableCollection<T?>? value)
        {
            var array = reader.ReadArray<T?>();

            if (array == null)
            {
                value = null;
            }
            else
            {
                value = new ReadOnlyObservableCollection<T?>(new ObservableCollection<T?>(array));
            }
        }
    }

    [Preserve]
    public sealed class BlockingCollectionFormatter<T> : SharpPackFormatter<BlockingCollection<T?>>
    {
        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref BlockingCollection<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref BlockingCollection<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            value = new BlockingCollection<T?>();

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                value.Add(v);
            }
        }
    }
}
