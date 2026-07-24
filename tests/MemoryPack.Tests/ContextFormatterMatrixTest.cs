using MemoryPack.Tests.Models;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace MemoryPack.Tests;

public class ContextFormatterMatrixTest
{
    readonly MemoryPackSerializerContext context = new();

    [Fact]
    public void WellKnownAndArrayFormatters_MatchDefaultPath()
    {
        AssertEquivalent(123);
        AssertEquivalent<int?>(456);
        AssertEquivalent(DayOfWeek.Friday);
        AssertEquivalent(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        AssertEquivalent(new DateTime(638500000000000000, DateTimeKind.Utc));
        AssertEquivalent(123.456m);
        AssertEquivalent(BigInteger.Parse("123456789012345678901234567890"));
        AssertEquivalent("显式 context");
        AssertEquivalent(new Version(1, 2, 3, 4));
        AssertEquivalent(new Uri("https://example.com/rpc"));
        AssertEquivalent(typeof(Dictionary<string, int>));
        AssertEquivalent(new[] { 1, 2, 3 });
        AssertEquivalent(new[] { "a", "b", "c" });
        AssertEquivalent(new int[,] { { 1, 2 }, { 3, 4 } });
        AssertEquivalent(new int[,,] { { { 1, 2 }, { 3, 4 } } });
        AssertEquivalent(new int[,,,] { { { { 1, 2 }, { 3, 4 } } } });
        AssertEquivalent(new ArraySegment<int>([1, 2, 3], 1, 2));
        AssertEquivalent(new Memory<int>([1, 2, 3]));
        AssertEquivalent(new ReadOnlyMemory<int>([1, 2, 3]));
        AssertEquivalent(new ReadOnlySequence<int>([1, 2, 3]));
    }

    [Fact]
    public void MutableCollectionFormatters_MatchDefaultPath()
    {
        AssertEquivalent(new List<string> { "a", "b" });
        AssertEquivalent(new Stack<int>([1, 2, 3]));
        AssertEquivalent(new Queue<int>([1, 2, 3]));
        AssertEquivalent(new LinkedList<int>([1, 2, 3]));
        AssertEquivalent(new HashSet<int> { 1, 2, 3 });
        AssertEquivalent(new SortedSet<int> { 3, 1, 2 });
        AssertEquivalent(CreatePriorityQueue());
        AssertEquivalent(new ObservableCollection<int> { 1, 2, 3 });
        AssertEquivalent(new Collection<int> { 1, 2, 3 });
        AssertEquivalent(new ConcurrentQueue<int>([1, 2, 3]));
        AssertEquivalent(new ConcurrentStack<int>([1, 2, 3]));
        AssertEquivalent(new ConcurrentBag<int>([1, 2, 3]));
        AssertEquivalent(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        AssertEquivalent(new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 });
        AssertEquivalent(new SortedList<string, int> { ["b"] = 2, ["a"] = 1 });
        AssertEquivalent(new ConcurrentDictionary<string, int>(
            new[] { new KeyValuePair<string, int>("a", 1) }));
        AssertEquivalent(new ReadOnlyCollection<int>([1, 2, 3]));
    }

    [Fact]
    public void InterfaceAndImmutableCollectionFormatters_MatchDefaultPath()
    {
        AssertEquivalent<IEnumerable<int>>(new List<int> { 1, 2, 3 });
        AssertEquivalent<ICollection<int>>(new List<int> { 1, 2, 3 });
        AssertEquivalent<IReadOnlyCollection<int>>(new List<int> { 1, 2, 3 });
        AssertEquivalent<IList<int>>(new List<int> { 1, 2, 3 });
        AssertEquivalent<IReadOnlyList<int>>(new List<int> { 1, 2, 3 });
        AssertEquivalent<IDictionary<string, int>>(
            new Dictionary<string, int> { ["a"] = 1 });
        AssertEquivalent<IReadOnlyDictionary<string, int>>(
            new Dictionary<string, int> { ["a"] = 1 });
        AssertEquivalent<ISet<int>>(new HashSet<int> { 1, 2, 3 });
        AssertEquivalent<IReadOnlySet<int>>(new HashSet<int> { 1, 2, 3 });

        AssertEquivalent(ImmutableArray.Create(1, 2, 3));
        AssertEquivalent(ImmutableList.Create(1, 2, 3));
        AssertEquivalent(ImmutableQueue.Create(1, 2, 3));
        AssertEquivalent(ImmutableStack.Create(1, 2, 3));
        AssertEquivalent(ImmutableDictionary.CreateRange(
            new[] { new KeyValuePair<string, int>("a", 1) }));
        AssertEquivalent(ImmutableSortedDictionary.CreateRange(
            new[] { new KeyValuePair<string, int>("a", 1) }));
        AssertEquivalent(ImmutableSortedSet.Create(1, 2, 3));
        AssertEquivalent(ImmutableHashSet.Create(1, 2, 3));
        AssertEquivalent(new Dictionary<string, int> { ["a"] = 1 }.ToFrozenDictionary());
        AssertEquivalent(new[] { 1, 2, 3 }.ToFrozenSet());
    }

    [Fact]
    public void TupleGeneratedVersionedAndUnionFormatters_MatchDefaultPath()
    {
        AssertEquivalent((42, "tuple"));
        AssertEquivalent(Tuple.Create(42, "tuple"));
        AssertEquivalent(new StandardTypeTwo { One = 1, Two = 2 });
        AssertEquivalent(new WithArray
        {
            One = [new StandardTypeOne { One = 10 }],
        });
        AssertEquivalent(new VersionTolerant3
        {
            MyProperty1 = 1,
            MyProperty2 = 2,
            MyProperty3 = 3,
        });

        IUnionInterface union = new Impl2
        {
            MyProperty = 10,
            Bar = "union",
        };
        AssertEquivalent(union);

        IForExternalUnion externalUnion = new AForTwo
        {
            BaseValue = 20,
            MyProperty = 30,
        };
        AssertEquivalent(externalUnion);
    }

    void AssertEquivalent<T>(T value)
    {
        var defaultPayload = MemoryPackSerializer.Serialize(value);
        var contextPayload = MemoryPackSerializer.Serialize(value, context);

        contextPayload.Should().Equal(defaultPayload);
        var defaultValue = MemoryPackSerializer.Deserialize<T>(defaultPayload);
        var contextValue = MemoryPackSerializer.Deserialize<T>(defaultPayload, context);
        MemoryPackSerializer.Serialize(contextValue)
            .Should().Equal(MemoryPackSerializer.Serialize(defaultValue));
        context.GetFormatter<T>().Should().BeSameAs(context.GetFormatter<T>());
    }

    static PriorityQueue<string, int> CreatePriorityQueue()
    {
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("b", 2);
        queue.Enqueue("a", 1);
        return queue;
    }
}
