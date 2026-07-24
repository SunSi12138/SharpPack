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
