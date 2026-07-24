using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Frozen;

namespace SharpPack.Tests;

public class FrozenCollectionFormatterTest
{
    [Fact]
    public void FrozenSet()
    {
        var set = new HashSet<int>();
        set.Add(1);
        set.Add(2);
        set.Add(3);
        set.Add(4);
        set.Add(5);

        var value = set.ToFrozenSet();
        var bin = SharpPackSerializer.Serialize(value);
        var deserializedValue = SharpPackSerializer.Deserialize<FrozenSet<int>>(bin);
        deserializedValue.Should().Equal(value);
    }

    [Fact]
    public void FrozenDictionary()
    {
        var dict = new Dictionary<int, int>()
        {
            { 1, 2 }, { 3, 4 }, { 4, 5 }, { 6, 7 }, { 8, 9 }
        };
        var value = dict.ToFrozenDictionary();
        var bin = SharpPackSerializer.Serialize(value);
        SharpPackSerializer.Deserialize<FrozenDictionary<int, int>>(bin).Should().BeEquivalentTo(value);
    }
}
