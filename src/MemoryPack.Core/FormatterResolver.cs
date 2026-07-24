using MemoryPack.Formatters;
using MemoryPack.Internal;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Frozen;

namespace MemoryPack;

internal static partial class FormatterResolver
{
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Cold compatibility fallback for closed generic shapes; generated or explicit registrations are required for NativeAOT.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Known formatter types all have public parameterless constructors.")]
    internal static object? CreateGenericFormatter(
        Type type,
        bool typeIsReferenceOrContainsReferences)
    {
        Type? formatterType = null;

        if (type.IsArray)
        {
            if (type.IsSZArray)
            {
                if (!typeIsReferenceOrContainsReferences)
                {
                    formatterType = typeof(DangerousUnmanagedArrayFormatter<>).MakeGenericType(type.GetElementType()!);
                    goto CREATE;
                }
                else
                {
                    formatterType = typeof(ArrayFormatter<>).MakeGenericType(type.GetElementType()!);
                    goto CREATE;
                }
            }
            else
            {
                var rank = type.GetArrayRank();
                switch (rank)
                {
                    case 2:
                        formatterType = typeof(TwoDimensionalArrayFormatter<>).MakeGenericType(type.GetElementType()!);
                        goto CREATE;
                    case 3:
                        formatterType = typeof(ThreeDimensionalArrayFormatter<>).MakeGenericType(type.GetElementType()!);
                        goto CREATE;
                    case 4:
                        formatterType = typeof(FourDimensionalArrayFormatter<>).MakeGenericType(type.GetElementType()!);
                        goto CREATE;
                    default:
                        return null; // not supported
                }
            }
        }

        if (type.IsEnum || !typeIsReferenceOrContainsReferences)
        {
            formatterType = typeof(DangerousUnmanagedFormatter<>).MakeGenericType(type);
            goto CREATE;
        }

        formatterType = TryCreateKnownGenericFormatterType(type);
        if (formatterType != null) goto CREATE;

        // Can't resolve formatter, return null(will create ErrorMemoryPackFormatter<T>).
        return null;

    CREATE:
        return Activator.CreateInstance(formatterType);
    }

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Cold compatibility fallback for closed generic shapes; generated or explicit registrations are required for NativeAOT.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2055",
        Justification = "Known formatter definitions are retained by direct typeof references in this resolver.")]
    static Type? TryCreateKnownGenericFormatterType(Type type)
    {
        if (!type.IsGenericType)
        {
            return null;
        }

        var definition = type.GetGenericTypeDefinition();
        var formatterType = GetKnownGenericFormatterDefinition(definition);
        return formatterType?.MakeGenericType(type.GetGenericArguments());
    }

    static Type? GetKnownGenericFormatterDefinition(Type type)
    {
        if (type == typeof(KeyValuePair<,>)) return typeof(KeyValuePairFormatter<,>);
        if (type == typeof(Lazy<>)) return typeof(LazyFormatter<>);
        if (type == typeof(Nullable<>)) return typeof(NullableFormatter<>);

        if (type == typeof(ArraySegment<>)) return typeof(ArraySegmentFormatter<>);
        if (type == typeof(Memory<>)) return typeof(MemoryFormatter<>);
        if (type == typeof(ReadOnlyMemory<>)) return typeof(ReadOnlyMemoryFormatter<>);
        if (type == typeof(ReadOnlySequence<>)) return typeof(ReadOnlySequenceFormatter<>);

        if (type == typeof(List<>)) return typeof(ListFormatter<>);
        if (type == typeof(Stack<>)) return typeof(StackFormatter<>);
        if (type == typeof(Queue<>)) return typeof(QueueFormatter<>);
        if (type == typeof(LinkedList<>)) return typeof(LinkedListFormatter<>);
        if (type == typeof(HashSet<>)) return typeof(HashSetFormatter<>);
        if (type == typeof(SortedSet<>)) return typeof(SortedSetFormatter<>);
        if (type == typeof(PriorityQueue<,>)) return typeof(PriorityQueueFormatter<,>);
        if (type == typeof(ObservableCollection<>)) return typeof(ObservableCollectionFormatter<>);
        if (type == typeof(Collection<>)) return typeof(CollectionFormatter<>);
        if (type == typeof(ConcurrentQueue<>)) return typeof(ConcurrentQueueFormatter<>);
        if (type == typeof(ConcurrentStack<>)) return typeof(ConcurrentStackFormatter<>);
        if (type == typeof(ConcurrentBag<>)) return typeof(ConcurrentBagFormatter<>);
        if (type == typeof(Dictionary<,>)) return typeof(DictionaryFormatter<,>);
        if (type == typeof(SortedDictionary<,>)) return typeof(SortedDictionaryFormatter<,>);
        if (type == typeof(SortedList<,>)) return typeof(SortedListFormatter<,>);
        if (type == typeof(ConcurrentDictionary<,>)) return typeof(ConcurrentDictionaryFormatter<,>);
        if (type == typeof(ReadOnlyCollection<>)) return typeof(ReadOnlyCollectionFormatter<>);
        if (type == typeof(ReadOnlyObservableCollection<>)) return typeof(ReadOnlyObservableCollectionFormatter<>);
        if (type == typeof(BlockingCollection<>)) return typeof(BlockingCollectionFormatter<>);

        if (type == typeof(IEnumerable<>)) return typeof(InterfaceEnumerableFormatter<>);
        if (type == typeof(ICollection<>)) return typeof(InterfaceCollectionFormatter<>);
        if (type == typeof(IReadOnlyCollection<>)) return typeof(InterfaceReadOnlyCollectionFormatter<>);
        if (type == typeof(IList<>)) return typeof(InterfaceListFormatter<>);
        if (type == typeof(IReadOnlyList<>)) return typeof(InterfaceReadOnlyListFormatter<>);
        if (type == typeof(IDictionary<,>)) return typeof(InterfaceDictionaryFormatter<,>);
        if (type == typeof(IReadOnlyDictionary<,>)) return typeof(InterfaceReadOnlyDictionaryFormatter<,>);
        if (type == typeof(ILookup<,>)) return typeof(InterfaceLookupFormatter<,>);
        if (type == typeof(IGrouping<,>)) return typeof(InterfaceGroupingFormatter<,>);
        if (type == typeof(ISet<>)) return typeof(InterfaceSetFormatter<>);
        if (type == typeof(IReadOnlySet<>)) return typeof(InterfaceReadOnlySetFormatter<>);

        if (type == typeof(ImmutableArray<>)) return typeof(ImmutableArrayFormatter<>);
        if (type == typeof(ImmutableList<>)) return typeof(ImmutableListFormatter<>);
        if (type == typeof(ImmutableQueue<>)) return typeof(ImmutableQueueFormatter<>);
        if (type == typeof(ImmutableStack<>)) return typeof(ImmutableStackFormatter<>);
        if (type == typeof(ImmutableDictionary<,>)) return typeof(ImmutableDictionaryFormatter<,>);
        if (type == typeof(ImmutableSortedDictionary<,>)) return typeof(ImmutableSortedDictionaryFormatter<,>);
        if (type == typeof(ImmutableSortedSet<>)) return typeof(ImmutableSortedSetFormatter<>);
        if (type == typeof(ImmutableHashSet<>)) return typeof(ImmutableHashSetFormatter<>);
        if (type == typeof(IImmutableList<>)) return typeof(InterfaceImmutableListFormatter<>);
        if (type == typeof(IImmutableQueue<>)) return typeof(InterfaceImmutableQueueFormatter<>);
        if (type == typeof(IImmutableStack<>)) return typeof(InterfaceImmutableStackFormatter<>);
        if (type == typeof(IImmutableDictionary<,>)) return typeof(InterfaceImmutableDictionaryFormatter<,>);
        if (type == typeof(IImmutableSet<>)) return typeof(InterfaceImmutableSetFormatter<>);

        if (type == typeof(FrozenDictionary<,>)) return typeof(FrozenDictionaryFormatter<,>);
        if (type == typeof(FrozenSet<>)) return typeof(FrozenSetFormatter<>);

        if (type == typeof(Tuple<>)) return typeof(TupleFormatter<>);
        if (type == typeof(ValueTuple<>)) return typeof(ValueTupleFormatter<>);
        if (type == typeof(Tuple<,>)) return typeof(TupleFormatter<,>);
        if (type == typeof(ValueTuple<,>)) return typeof(ValueTupleFormatter<,>);
        if (type == typeof(Tuple<,,>)) return typeof(TupleFormatter<,,>);
        if (type == typeof(ValueTuple<,,>)) return typeof(ValueTupleFormatter<,,>);
        if (type == typeof(Tuple<,,,>)) return typeof(TupleFormatter<,,,>);
        if (type == typeof(ValueTuple<,,,>)) return typeof(ValueTupleFormatter<,,,>);
        if (type == typeof(Tuple<,,,,>)) return typeof(TupleFormatter<,,,,>);
        if (type == typeof(ValueTuple<,,,,>)) return typeof(ValueTupleFormatter<,,,,>);
        if (type == typeof(Tuple<,,,,,>)) return typeof(TupleFormatter<,,,,,>);
        if (type == typeof(ValueTuple<,,,,,>)) return typeof(ValueTupleFormatter<,,,,,>);
        if (type == typeof(Tuple<,,,,,,>)) return typeof(TupleFormatter<,,,,,,>);
        if (type == typeof(ValueTuple<,,,,,,>)) return typeof(ValueTupleFormatter<,,,,,,>);
        if (type == typeof(Tuple<,,,,,,,>)) return typeof(TupleFormatter<,,,,,,,>);
        if (type == typeof(ValueTuple<,,,,,,,>)) return typeof(ValueTupleFormatter<,,,,,,,>);

        return null;
    }

}

internal sealed class ErrorMemoryPackFormatter<T> : MemoryPackFormatter<T>
{
    readonly Exception? exception;
    readonly string? message;

    public ErrorMemoryPackFormatter()
    {
        this.exception = null;
        this.message = null;
    }

    public ErrorMemoryPackFormatter(Exception exception)
    {
        this.exception = exception;
        this.message = null;
    }

    public ErrorMemoryPackFormatter(string message)
    {
        this.exception = null;
        this.message = message;
    }

    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        Throw();
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        Throw();
    }

    [DoesNotReturn]
    void Throw()
    {
        if (exception != null)
        {
            throw new MemoryPackSerializationException(
                $"Failed to resolve a formatter for {typeof(T).FullName}.",
                exception);
        }
        else if (message != null)
        {
            MemoryPackSerializationException.ThrowMessage(message);
        }
        else
        {
            throw new MemoryPackSerializationException(
                $"No formatter can be resolved for {typeof(T).FullName}.");
        }
    }
}
