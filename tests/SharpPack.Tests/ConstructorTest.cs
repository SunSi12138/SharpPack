using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;
public class ConstructorTest
{
    [Fact]
    public void SkipOrder()
    {
        var a = new Alpha { B1 = new Beta(10) };
        var bin = SharpPackSerializer.Serialize(a);
        var v2 = SharpPackSerializer.Deserialize<Alpha>(bin);
        v2!.B1!.Value1.Should().Be(10);
    }

    [Fact]
    public void ExactConstructorNamesCanDifferByCase()
    {
        var original = new CaseSensitiveConstructor(17, 42);
        var result = SharpPackSerializer.Deserialize<CaseSensitiveConstructor>(SharpPackSerializer.Serialize(original))!;

        result.GetLowercaseValue().Should().Be(17);
        result.Value.Should().Be(42);
    }

    [Fact]
    public void OneSlotSuppliesBothCaseVariantsWhenThereIsNoExactMatch()
    {
        var original = new SharedConstructorSlot(17, 42);
        var result = SharpPackSerializer.Deserialize<SharedConstructorSlot>(SharpPackSerializer.Serialize(original))!;

        result.GetLowercaseValue().Should().Be(42);
        result.Value.Should().Be(42);
    }
}

[SharpPackable]
public partial class CaseSensitiveConstructor
{
    [SharpPackInclude] private readonly int value;
    public int Value { get; }

    public CaseSensitiveConstructor(int value, int Value)
    {
        this.value = value;
        this.Value = Value;
    }

    public int GetLowercaseValue() => value;
}

[SharpPackable]
public partial class SharedConstructorSlot(int value, int Value)
{
    readonly int lower = value;
    public int Value { get; } = Value;

    public int GetLowercaseValue() => lower;
}

[SharpPackable(GenerateType.CircularReference)]
public partial class Alpha
{
    [SharpPackOrder(1)]
    public Beta? B1 { get; set; }

    public Alpha()
    {

    }

}

// ctor for VersionTolerant, Skipped order

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Beta
{
    [SharpPackOrder(1)]
    public int Value1 { get; set; }

    public Beta(int value1)
    {
        this.Value1 = value1;
    }
}

// support underscore private/internal convention

[SharpPackable]
public partial class Gamma
{
    [SharpPackInclude]
    private readonly string _test;

    public Gamma(string test)
    {
        _test = test;
    }
}
