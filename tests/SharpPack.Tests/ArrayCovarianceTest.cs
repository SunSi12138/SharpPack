using System.Collections.Generic;
using SharpPack.Formatters;

namespace SharpPack.Tests;

public class ArrayCovarianceTest
{
    [Fact]
    public void InterfaceCollection_CovariantArrayUsesTheDeclaredFormatter()
    {
        var context = CreateUnionContext();
        var item = new Gen1 { MyProperty = 42 };
        var exact = new CovariantCollectionModel { Items = new IDynamicBase[] { item } };
        var covariant = new CovariantCollectionModel { Items = new Gen1[] { item } };

        var expected = SharpPackSerializer.Serialize(exact, context);
        var actual = SharpPackSerializer.Serialize(covariant, context);

        Assert.Equal(expected, actual);
        var decoded = SharpPackSerializer.Deserialize<CovariantCollectionModel>(actual, context);
        Assert.Equal(42, Assert.IsType<Gen1>(Assert.Single(decoded!.Items!)).MyProperty);
    }

    [Fact]
    public void ArrayRead_ReplacesCovariantDestinationButReusesExactDestination()
    {
        var context = CreateUnionContext();
        IDynamicBase?[] source = [new Gen2 { MyProperty = "replacement" }];
        var payload = SharpPackSerializer.Serialize(source, context);
        IDynamicBase?[]? covariant = new Gen1?[] { new() { MyProperty = 1 } };
        var oldCovariant = covariant;
        IDynamicBase?[]? exact = new IDynamicBase?[1];
        var oldExact = exact;

        SharpPackSerializer.Deserialize(payload, ref covariant, context);
        SharpPackSerializer.Deserialize(payload, ref exact, context);

        Assert.NotSame(oldCovariant, covariant);
        Assert.IsType<IDynamicBase[]>(covariant);
        Assert.Equal(1, Assert.IsType<Gen1>(oldCovariant[0]).MyProperty);
        Assert.Same(oldExact, exact);
        Assert.Equal("replacement", Assert.IsType<Gen2>(Assert.Single(covariant!)).MyProperty);
        Assert.Equal("replacement", Assert.IsType<Gen2>(Assert.Single(exact!)).MyProperty);
    }

    [Fact]
    public void GeneratedArrayMember_WritesCovariantInputUsingTheDeclaredModel()
    {
        var item = new CovariantDerivedItem { Text = "payload" };
        var exact = new CovariantArrayModel { Items = new CovariantBaseItem[] { item } };
        var covariant = new CovariantArrayModel { Items = new CovariantDerivedItem[] { item } };

        var expected = SharpPackSerializer.Serialize(exact);
        var actual = SharpPackSerializer.Serialize(covariant);

        Assert.Equal(expected, actual);
        var decoded = SharpPackSerializer.Deserialize<CovariantArrayModel>(actual);
        Assert.Equal("payload", Assert.IsType<CovariantBaseItem>(Assert.Single(decoded!.Items!)).Text);
    }

    [Fact]
    public void GeneratedArrayMember_ReplacesCovariantDestination()
    {
        var source = new CovariantArrayModel { Items = [new() { Text = "new" }] };
        CovariantArrayModel? destination = new() { Items = new CovariantDerivedItem[] { new() { Text = "old" } } };
        var previous = destination.Items;

        SharpPackSerializer.Deserialize(SharpPackSerializer.Serialize(source), ref destination);

        Assert.NotSame(previous, destination!.Items);
        Assert.Equal("old", previous![0]!.Text);
        Assert.Equal("new", Assert.IsType<CovariantBaseItem>(Assert.Single(destination.Items!)).Text);
    }

    static SharpPackSerializerContext CreateUnionContext()
        => new SharpPackSerializerContextBuilder()
            .Register(new DynamicUnionFormatterBuilder<IDynamicBase>().Add<Gen1>(7).Add<Gen2>(42).Build())
            .Build();
}

[SharpPackable]
internal partial class CovariantCollectionModel
{
    public IReadOnlyCollection<IDynamicBase>? Items { get; set; }
}

[SharpPackable]
internal partial class CovariantArrayModel
{
    public CovariantBaseItem?[]? Items { get; set; }
}

[SharpPackable]
internal partial class CovariantBaseItem
{
    public string? Text { get; set; }
}

internal sealed class CovariantDerivedItem : CovariantBaseItem;
