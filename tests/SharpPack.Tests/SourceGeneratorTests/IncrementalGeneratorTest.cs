using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.SourceGeneratorTests;

public class IncrementalGeneratorTest
{
    [Fact]
    public void Run()
    {
        // lang=C#-test
        var step1 = """
[SharpPackable]
public partial class MyClass
{
    public int MyProperty { get; set; }
}
""";

        // lang=C#-test
        var step2 = """
[SharpPackable]
public partial class MyClass
{
    public int MyProperty { get; set; }
    // unrelated line
}
""";

        var hoge = CSharpGeneratorRunner.GetIncrementalGeneratorTrackedStepsReasons("SharpPack.SharpPackable.", step1, step2);

    }
}


