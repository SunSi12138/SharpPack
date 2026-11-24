using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class FactoryRootInitializationTests
{
    [Fact]
    public void PureModel_UsesFieldInitializerWithoutStrictStaticConstructor()
    {
        var generated = Generate("public int Value { get; set; }");
        generated.Should().NotContain("static Model()");
        generated.Should().Contain("private static readonly bool __SharpPackPreserveSelfFactoryRootsValue = __SharpPackPreserveSelfFactoryRoots();");
        generated.Should().Contain("var __SharpPackPreserveSelfFactoryRoots = __SharpPackPreserveSelfFactoryRootsValue;");
        generated.Should().NotContain(".GetMethods(");
    }

    [Theory]
    [InlineData("static partial void StaticConstructor() { }")]
    [InlineData("public static int Marker = Initialize(); private static int Initialize() => 1;")]
    [InlineData("public static int Marker { get; } = Initialize(); private static int Initialize() => 1;")]
    public void UserInitialization_PreservesStrictStaticConstructor(string initialization)
    {
        var generated = Generate("public int Value { get; set; } " + initialization);
        generated.Should().Contain("static Model()");
        generated.Should().Contain("StaticConstructor();");
        generated.Should().NotContain("private static readonly bool __SharpPackPreserveSelfFactoryRootsValue");
    }

    [Theory]
    [InlineData("public static string? _;")]
    [InlineData("public static bool _;")]
    [InlineData("[SharpPackIgnore] public string? _;")]
    public void UnderscoreMember_DoesNotCaptureRootRead(string member)
    {
        var generated = Generate("public int Value { get; set; } " + member);
        generated.Should().Contain("var __SharpPackPreserveSelfFactoryRoots = __SharpPackPreserveSelfFactoryRootsValue;");
        generated.Should().NotContain("_ = __SharpPackPreserveSelfFactoryRootsValue");
    }

    [Fact]
    public void InheritedUnderscoreMember_DoesNotCaptureRootRead()
    {
        var generated = Run("""
namespace Generated;
public class ModelBase { protected static string? _; }
[SharpPackable]
public partial class Model : ModelBase { public int Value { get; set; } }
""");
        generated.Should().Contain("var __SharpPackPreserveSelfFactoryRoots = __SharpPackPreserveSelfFactoryRootsValue;");
    }

    [Fact]
    public void ExternalUnionUnderscoreMember_DoesNotCaptureRootRead()
    {
        var generated = Run("""
namespace Generated;
public abstract partial class Union { public static string? _; }
[SharpPackable]
public partial class Case : Union { public int Value { get; set; } }
[SharpPackUnionFormatter(typeof(Union))]
[SharpPackUnion(0, typeof(Case))]
public partial class UnionFormatter { }
""");
        generated.Should().Contain("var __SharpPackPreserveFactory = __SharpPackPreserveFactoryValue;");
        generated.Should().NotContain("_ = __SharpPackPreserveFactoryValue");
    }

    [Fact]
    public void Constants_DoNotIntroduceStrictInitialization()
    {
        var generated = Generate("public const int Marker = 1; public int Value { get; set; }");
        generated.Should().NotContain("static Model()");
    }

    [Fact]
    public void RootNames_AvoidMemberAndGenericParameterCollisions()
    {
        var source = """
namespace Generated;
[SharpPackable]
public partial class Model<__SharpPackPreserveSelfFactoryRootsValue>
{
    public int Value { get; set; }
    private static void __SharpPackPreserveSelfFactoryRoots() { }
}
""";
        var generated = Run(source);
        generated.Should().Contain("private static bool __SharpPackPreserveSelfFactoryRoots_()");
        generated.Should().Contain("private static readonly bool __SharpPackPreserveSelfFactoryRoots_Value");
    }

    [Fact]
    public void RootName_AvoidsContainingTypeName()
    {
        var generated = Run("""
namespace Generated;
[SharpPackable]
public partial class __SharpPackPreserveSelfFactoryRoots
{
    public int Value { get; set; }
}
""");
        generated.Should().Contain("private static bool __SharpPackPreserveSelfFactoryRoots_()");
    }

    [Fact]
    public void RootValue_DoesNotShadowCapturedConstructorParameter()
    {
        var generated = Run("""
namespace Generated;
[SharpPackable]
public partial class Model(int __SharpPackPreserveSelfFactoryRootsValue)
{
    public int __sharppackpreserveselffactoryrootsvalue { get; } = __SharpPackPreserveSelfFactoryRootsValue;
    public int ReadCapturedParameter() => __SharpPackPreserveSelfFactoryRootsValue;
}
""");
        generated.Should().Contain("private static readonly bool __SharpPackPreserveSelfFactoryRoots_Value");
    }

    [Fact]
    public void ExternalUnionRoot_DoesNotShadowImportedStaticMember()
    {
        var generated = Run("""
using static Generated.Imported;
namespace Generated;
public static class Imported { public static int __SharpPackPreserveFactoryValue => 42; }
public abstract partial class Union { public int ReadImportedValue() => __SharpPackPreserveFactoryValue; }
[SharpPackable]
public partial class Case : Union { public int Value { get; set; } }
[SharpPackUnionFormatter(typeof(Union))]
[SharpPackUnion(0, typeof(Case))]
public partial class UnionFormatter { }
""");
        generated.Should().Contain("private static readonly bool __SharpPackPreserveFactory_Value");
    }

    static string Generate(string members)
        => Run("namespace Generated; [SharpPackable] public partial class Model { " + members + " }");

    static string Run(string source)
    {
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator(source);
        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return string.Join(Environment.NewLine, compilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(tree => tree.ToString()));
    }
}
