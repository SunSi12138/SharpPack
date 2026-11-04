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
        generated.Should().Contain("_ = __SharpPackPreserveSelfFactoryRootsValue;");
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
