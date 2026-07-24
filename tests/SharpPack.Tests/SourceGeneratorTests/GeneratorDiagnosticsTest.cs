using SharpPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public partial class GeneratorDiagnosticsTest
{
    void Compile(int id, string code, bool allowMultipleError = false)
    {
        var (_, diagnostics) = CSharpGeneratorRunner.RunGenerator(code);
        if (!allowMultipleError)
        {
            diagnostics.Length.Should().Be(1);
            diagnostics[0].Id.Should().Be("SHARPPACK" + id.ToString("000"));
        }
        else
        {
            diagnostics.Select(x => x.Id).Should().Contain("SHARPPACK" + id.ToString("000"));
        }
    }

    [Fact]
    public void SHARPPACK001_MuestBePartial()
    {
        Compile(1, """
using SharpPack;

[SharpPackable]
public class Hoge
{
}
""");
    }

    [Fact]
    public void SHARPPACK003_AbstractMustUnion()
    {
        Compile(3, """
using SharpPack;

[SharpPackable]
public abstract partial class Hoge
{
}
""");

        Compile(3, """
using SharpPack;

[SharpPackable]
public partial interface IHoge
{
}
""");
    }

    [Fact]
    public void SHARPPACK004_MultipleCtorWithoutAttribute()
    {
        Compile(4, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public Hoge()
    {
    }

    public Hoge(int x)
    {
    }
}
""");
    }

    [Fact]
    public void SHARPPACK005_MultipleCtorAttribute()
    {
        Compile(5, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    [SharpPackConstructor]
    public Hoge()
    {
    }

    [SharpPackConstructor]
    public Hoge(int x)
    {
    }
}
""");
    }

    [Fact]
    public void SHARPPACK006_ConstructorHasNoMatchedParameter()
    {
        Compile(6, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public int Foo { get; set;}

    [SharpPackConstructor]
    public Hoge(int hhogee)
    {
        this.Foo = hhogee;
    }
}
""");
    }

    [Fact]
    public void SHARPPACK007_OnMethodHasParameter()
    {
        Compile(7, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    [SharpPackOnSerializing]
    void Foo(int x)
    {
    }
}
""");
    }

    [Fact]
    public void SHARPPACK008_OnMethodInUnamannagedType()
    {
        Compile(8, """
using SharpPack;

[SharpPackable]
public partial struct Hoge
{
    [SharpPackOnSerializing]
    void Foo()
    {
    }
}
""");
    }

    [Fact]
    public void SHARPPACK009_OverrideMemberCantAddAnnotation()
    {
        Compile(9, """
using SharpPack;

public abstract class MyClass
{
    public abstract int MyProperty { get; set; }
}

[SharpPackable]
public partial class MyClass2 : MyClass
{
    [SharpPackIgnore]
    public override int MyProperty { get; set; }
}
""");

        Compile(9, """
using SharpPack;

public abstract class MyClass
{
    public abstract int MyProperty { get; set; }
}

[SharpPackable]
public partial class MyClass3 : MyClass
{
    [SharpPackInclude]
    public override int MyProperty { get; set; }
}

""");
    }

    [Fact]
    public void SHARPPACK010_016_Union()
    {
        Compile(10, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(0, typeof(string))]
public sealed partial class MyClass
{
}
""", allowMultipleError: true);

        Compile(11, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(0, typeof(string))]
public partial class MyClass
{
}
""", allowMultipleError: true);

        Compile(12, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(1, typeof(MyClass1))]
[SharpPackUnion(1, typeof(MyClass2))]
public partial interface IMyClass
{
}

[SharpPackable]
public partial class MyClass1 : IMyClass
{
}

[SharpPackable]
public partial class MyClass2 : IMyClass
{
}
""", allowMultipleError: true);

        Compile(13, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(1, typeof(MyClass1))]
[SharpPackUnion(2, typeof(MyClass2))]
public partial interface IMyClass
{
}

[SharpPackable]
public partial class MyClass1 : IMyClass
{
}

[SharpPackable]
public partial class MyClass2
{
}
""", allowMultipleError: true);

        Compile(14, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(1, typeof(MyClass1))]
[SharpPackUnion(2, typeof(MyClass2))]
public abstract partial class MyClassBase
{
}

[SharpPackable]
public partial class MyClass1 : MyClassBase
{
}

[SharpPackable]
public partial class MyClass2
{
}
""", allowMultipleError: true);

        Compile(15, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(1, typeof(MyClass1))]
[SharpPackUnion(2, typeof(MyClass2))]
public partial interface IMyClass
{
}

[SharpPackable]
public partial class MyClass1 : IMyClass
{
}

[SharpPackable]
public partial struct MyClass2 : IMyClass
{
}
""", allowMultipleError: true);

        Compile(16, """
using SharpPack;

[SharpPackable]
[SharpPackUnion(1, typeof(MyClass1))]
[SharpPackUnion(2, typeof(MyClass2))]
public partial interface IMyClass
{
}

[SharpPackable]
public partial class MyClass1 : IMyClass
{
}

// [SharpPackable]
public partial class MyClass2 : IMyClass
{
}
""", allowMultipleError: true);

    }



    [Fact]
    public void SHARPPACK018_MemberCantSerializeType()
    {
        Compile(18, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public object Foo { get; set;}
}
""");

        Compile(18, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public System.Array Foo { get; set;}
}
""");

        Compile(18, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public System.Action Foo { get; set;}
}
""");
    }

    [Fact]
    public void SHARPPACK019_MemberIsNotSharpPackable()
    {
        Compile(19, """
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    public Foo Bar { get; set;}
}

public class Foo { }
""");
    }

    [Fact]
    public void SHARPPACK020_TypeIsRefStruct()
    {
        Compile(20, """
using SharpPack;

[SharpPackable]
public ref partial struct Hoge
{
    public int Bar { get; set;}
}
""");
    }

    [Fact]
    public void SHARPPACK021_MemberIsRefStruct()
    {
        Compile(21, """
using System;
using SharpPack;

[SharpPackable]
public partial class Hoge
{
    byte[] b = default!;
    public ReadOnlySpan<byte> SpanProp => b;
}
""");
    }

    [Fact]
    public void SHARPPACK022_CollectionGenerateIsAbstract()
    {
        Compile(22, """
using System.Collections.Generic;
using SharpPack;

[SharpPackable(GenerateType.Collection)]
public abstract partial class MyList : List<int>
{
}
""");
    }

    [Fact]
    public void SHARPPACK023_CollectionGenerateNotImplementedInterface()
    {
        Compile(23, """
using SharpPack;

[SharpPackable(GenerateType.Collection)]
public partial class Hoge
{
}
""");
    }

    [Fact]
    public void SHARPPACK024_CollectionGenerateNoParameterlessConstructor()
    {
        Compile(24, """
using System.Collections.Generic;
using SharpPack;

[SharpPackable(GenerateType.Collection)]
public partial class Hoge : List<int>
{
    public Hoge(int x)
    {
        Add(x);
    }
}
""");
    }

    [Fact]
    public void SHARPPACK025_AllMembersMustAnnotateOrder()
    {
        Compile(25, """
using SharpPack;

[SharpPackable(SerializeLayout.Explicit)]
public partial class Hoge
{
    [SharpPackOrder(0)]
    public int Prop1 { get; set; }
    public int Prop2 { get; set; }
}
""");
    }

    [Fact]
    public void SHARPPACK026_AllMembersMustBeContinuousNumber()
    {
        Compile(26, """
using SharpPack;

[SharpPackable(SerializeLayout.Explicit)]
public partial class Hoge
{
    [SharpPackOrder(0)]
    public int Prop1 { get; set; }
    [SharpPackOrder(2)]
    public int Prop2 { get; set; }
}
""");
    }

    [Fact]
    public void SHARPPACK033_CircularReferenceOnlyAllowsParameterlessConstructor()
    {
        Compile(33, """
using SharpPack;

[SharpPackable(GenerateType.CircularReference)]
public partial class Hoge
{
    [SharpPackOrder(0)]
    public int Prop1 { get; set; }
    [SharpPackOrder(2)]
    public int Prop2 { get; set; }

    public Hoge(int prop1, int prop2)
    {
        this.Prop1 = prop1;
        this.Prop2 = prop2;
    }
}
""");
    }

    [Fact]
    public void SHARPPACK034_UnamangedStructWithLayoutAutoField()
    {
        var code = """
using System;
using SharpPack;

[SharpPackable]
public partial struct Hoge
{
    public int X;
    public int Y;
    public DateTime DT;
}
""";

        var (_, diagnostics) = CSharpGeneratorRunner.RunGenerator(code);
        diagnostics.Length.Should().Be(0);
    }

    [Fact]
    public void SHARPPACK035_UnamangedStructSharpPackCtor()
    {
        Compile(35, """
using SharpPack;

[SharpPackable]
public partial struct Hoge
{
    public int X;
    public int Y;

    [SharpPackConstructor]
    public Hoge(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}
""");
    }


    [Fact]
    public void SHARPPACK036_InheritTypeCanNotIncludeParentPrivateMember()
    {
        Compile(36, """
using SharpPack;

[SharpPackable(SerializeLayout.Explicit)]
public  partial class TestParent2
{
    [SharpPackOrder(0)]
    public int A;

    [SharpPackOrder(1), SharpPackInclude]
    private int B;

    [SharpPackOrder(2)]
    public int C;
}

[SharpPackable(SerializeLayout.Explicit)]
public sealed partial class TestChild2 : TestParent2
{
    [SharpPackOrder(3)]
    public int D;
}
""");
    }


    [Fact]
    public void SHARPPACK037_ReadOnlyFieldMustBeConstructorMember()
    {
        Compile(37, """
using SharpPack;

[SharpPackable]
public partial class ReadOnlyTest
{
    public readonly int A;
}

""");
    }

    [Fact]
    public void SHARPPACK038_()
    {
        Compile(38, """
using SharpPack;

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Tester
{
    [SharpPackOrder(0)]
    public int I1 { get; set; }

    [SharpPackOrder(0)]
    public string S1 { get; set; }

    [SharpPackOrder(1)]
    public bool B1 { get; set; }
}

""");
    }

    [Fact]
    public void SHARPPACK040_SuppressDefaultInitializationMustBeSettable()
    {
        Compile(40, """
using SharpPack;

[SharpPackable]
public partial class Tester
{
    [SuppressDefaultInitialization]
    public required int I1 { get; set; }
}

""");

        Compile(40, """
using SharpPack;

[SharpPackable]
public partial class Tester
{
    [SuppressDefaultInitialization]
    public int I1 { get; init; }
}

""");

        Compile(40, """
using SharpPack;

[SharpPackable]
public partial class Tester
{
    [SuppressDefaultInitialization]
    public readonly int I1;

    [SharpPackConstructor]
    public Tester(int i1)
    {
        I1 = i1;
    }
}

""");
    }

    [Fact]
    public void SHARPPACK041_UnmanagedStructCannotBeVersionTolerant()
    {
        Compile(41, """
using SharpPack;

[SharpPackable(GenerateType.VersionTolerant)]
public partial struct Tester
{
    [SharpPackOrder(0)]
    public int I1 { get; init; }
}
""");
    }

    [Fact]
    public void SHARPPACK042_NestedContainingTypesMustBePartial()
    {
        Compile(42, """
                    using SharpPack;

                    public struct NestedContainer
                    {
                        [SharpPackable]
                        public partial struct NestedStruct
                        {
                            public int I1 { get; init; }
                        }
                    }
                    """);
    }
}
