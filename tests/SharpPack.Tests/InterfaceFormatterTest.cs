using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class InterfaceFormatterTest
{
    void CollectionEqual<T, TSerializeAs>(T value, TSerializeAs? dummy)
        where T : TSerializeAs
        where TSerializeAs : IEnumerable<int>
    {
        var bin = SharpPackSerializer.Serialize<TSerializeAs>(value);
        var value2 = SharpPackSerializer.Deserialize<TSerializeAs>(bin);
        value2.Should().Equal(value);
    }

    [Fact]
    public void EnumerableTest()
    {
        // Array
        {
            var collection = new[] { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(IEnumerable<int>));
        }

        // List
        {
            var collection = new List<int> { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(IEnumerable<int>));
        }

        // Has Count
        {
            var collection = new ObservableCollection<int> { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(IEnumerable<int>));
        }

        // No Count
        {
            var collection = Iterate(1, 5);
            CollectionEqual(collection, default(IEnumerable<int>));
        }
    }

    [Fact]
    public void CollectionTest()
    {
        // Array
        {
            var collection = new[] { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(ICollection<int>));
        }

        // List
        {
            var collection = new List<int> { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(ICollection<int>));
        }

        // Has Count
        {
            var collection = new ObservableCollection<int> { 1, 2, 3, 4, 5 };
            CollectionEqual(collection, default(ICollection<int>));
        }
    }

    [Fact]
    public void Collections()
    {
        var collection = new ObservableCollection<int> { 1, 2, 3, 4, 5 };
        CollectionEqual(collection, default(IReadOnlyCollection<int>));
        CollectionEqual(collection, default(IList<int>));
        CollectionEqual(collection, default(IReadOnlyList<int>));
    }

    [Fact]
    public void Dictionaries()
    {
        var collection = new Dictionary<int, int> { { 1, 2 }, { 3, 4 }, { 5, 6 } };

        {
            var bin = SharpPackSerializer.Serialize<IDictionary<int, int>>(collection);
            SharpPackSerializer.Deserialize<IDictionary<int, int>>(bin)
                .Should().BeEquivalentTo(collection);
        }
        {
            var bin = SharpPackSerializer.Serialize<IReadOnlyDictionary<int, int>>(collection);
            SharpPackSerializer.Deserialize<IReadOnlyDictionary<int, int>>(bin)
                .Should().BeEquivalentTo(collection);
        }
    }

    [Fact]
    public void Lookup()
    {
        var seq = new[]
        {
            (1, 2), (1, 100), (3, 42), (45, 30), (3, 10)
        };

        var lookup = seq.ToLookup(x => x.Item1, x => x.Item2);
        {
            var bin = SharpPackSerializer.Serialize(lookup);
            SharpPackSerializer.Deserialize<ILookup<int, int>>(bin)
                .Should().BeEquivalentTo(lookup);
        }

        var grouping = lookup.First(x => x.Key == 3);
        {
            var bin = SharpPackSerializer.Serialize(grouping);
            var g2 = SharpPackSerializer.Deserialize<IGrouping<int, int>>(bin);
            g2!.Key.Should().Be(grouping.Key);
            g2.AsEnumerable().Should().BeEquivalentTo(grouping.AsEnumerable());
        }

        var emptyLookup = Array.Empty<int>().ToLookup(x => x, x => x);
        {
            var bin = SharpPackSerializer.Serialize(emptyLookup);
            var deserialized = SharpPackSerializer.Deserialize<ILookup<int, int>>(bin);
            deserialized![0].Should().BeEmpty();
        }
    }

    [Fact]
    public void Sets()
    {
        var collection = new HashSet<int> { 1, 10, 100, 1000, 10000, 20, 200 };

        {
            var bin = SharpPackSerializer.Serialize<ISet<int>>(collection);
            SharpPackSerializer.Deserialize<ISet<int>>(bin)
                .Should().BeEquivalentTo(collection);
        }
        {
            var bin = SharpPackSerializer.Serialize<IReadOnlySet<int>>(collection);
            SharpPackSerializer.Deserialize<IReadOnlySet<int>>(bin)
                .Should().BeEquivalentTo(collection);
        }

    }

    IEnumerable<int> Iterate(int from, int to)
    {
        for (int i = from; i <= to; i++)
        {
            yield return i;
        }
    }
}
