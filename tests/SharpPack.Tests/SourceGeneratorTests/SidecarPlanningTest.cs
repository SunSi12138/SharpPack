using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPack.Generator;
using SharpPack.Tests.Utils;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class SidecarPlanningTest
{
    static readonly CSharpParseOptions Parse = new(LanguageVersion.CSharp14, preprocessorSymbols: ["NET10_0_OR_GREATER"]);

    [Fact]
    public void OriginalCompilationApiReusesKnownKindsAndVisitsEachClosedNode()
    {
        var compilation = Create("""
            namespace Vendor;

            public enum State : byte { Active }
            public struct Raw { public int Value; }
            public sealed record Header(string? Name);
            public struct Payload { public string? Text { get; init; } }
            public class Outer<T>
            {
                public sealed class Box<TValue>
                {
                    public T? Key { get; init; }
                    public TValue? Value { get; init; }
                }
            }

            public class Base
            {
                public Header? Metadata { get; init; }
            }
            public sealed class Node : Base
            {
                public List<Node> Children { get; init; } = new();
                [SharpPackIgnore] public object? Ignored { get; init; }
            }
            public sealed class NotRooted { public object? Unsupported { get; set; } }
            """, """
            [SharpPackable]
            public partial class OwnModel
            {
                [SharpPackAllowSerialize]
                public Vendor.Payload Payload { get; set; }
            }

            public class Roots
            {
                public int Primitive { get; }
                public string BuiltIn { get; } = "";
                public Vendor.State Enum { get; }
                public Vendor.Raw Raw { get; }
                public List<Vendor.Node> List { get; } = new();
                public Dictionary<Vendor.State, Vendor.Node[]> Map { get; } = new();
                public (Vendor.Payload Record, int? Number) Pair { get; }
                public OwnModel? Own { get; }
                public Vendor.Outer<string>.Box<Vendor.Payload>? Nested { get; }
                public Vendor.Node? Node { get; }
            }

            internal static class RouteProviderConsumer
            {
                internal static SharpPackSerializerContext Compile() => SharpPack.Generated.RouteProvider
                    .Register(new SharpPackSerializerContextBuilder()).Build();
            }
            """);
        var roots = compilation.GetTypeByMetadataName("Roots")!.GetMembers().OfType<IPropertySymbol>()
            .Select(static member => new SharpPackSidecarRoot(member.Type)).ToImmutableArray();
        var output = SharpPackSidecarGenerator.Generate(compilation, roots, "RouteProvider");
        Assert.Empty(output.Diagnostics);
        Assert.All(output.Roots.Take(8), static result => Assert.Equal(SharpPackFormatterSupport.Existing, result.Support));
        Assert.All(output.Roots.Skip(8), static result => Assert.Equal(SharpPackFormatterSupport.GenerateExternal, result.Support));
        Assert.Equal(["Metadata", "Children"], output.Roots[^1].Sidecars
            .Single(static shape => shape.TypeName == "global::Vendor.Node").Members.Select(static member => member.Name));
        Assert.Empty(output.Roots.Single(static result => result.Type.Name == "Raw").Sidecars);
        Assert.Equal(output.Source, SharpPackSidecarGenerator.Generate(compilation, roots.Reverse().ToImmutableArray(), "RouteProvider").Source);

        var tree = CSharpSyntaxTree.ParseText(Assert.IsType<string>(output.Source), Parse, output.HintName);
        Assert.Equal(4, tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Count(static declaration => declaration.Identifier.ValueText.StartsWith("Formatter_", System.StringComparison.Ordinal)));
        var (_, generated) = Run(compilation.AddSyntaxTrees(tree));
        Assert.Empty(generated.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("public sealed class Payload { private Payload() { } }", "no accessible SharpPack constructor")]
    [InlineData("public sealed class Payload { public string Text { get; private set; } = string.Empty; }", ".Text")]
    [InlineData("public sealed class Payload { [SharpPackIgnore] public required string Text { get; init; } }", "required member")]
    [InlineData("public interface Payload { }", "concrete class or struct")]
    [InlineData("public sealed class Payload { public int Number; public string? Name { get; set; } }", "relative declaration order")]
    [InlineData("public sealed class Payload { public int Number { get; } public Payload(string number) { } }", "same type")]
    [InlineData("public sealed class Payload { public int Number { get; } public Payload(ref int number) { } }", "by-value or in")]
    [InlineData("public sealed class Payload { [SharpPackInclude] private int Hidden; }", "marked SharpPackInclude")]
    [InlineData("public sealed class Payload { [SharpPackOnDeserializing] private void Callback() { } }", "callbacks require an existing formatter")]
    public void UnsupportedMetadataReportsAnAssemblyRootDiagnostic(string declaration, string reason)
    {
        var compilation = Create("namespace Vendor; " + declaration, """
            [assembly: SharpPackExternalType(typeof(Vendor.Payload))]
            """);
        var (diagnostics, generated) = Run(compilation);
        var failure = Assert.Single(diagnostics);
        Assert.Equal("SHARPPACK045", failure.Id);
        Assert.True(failure.Location.IsInSource);
        Assert.Contains(reason, failure.GetMessage());
        Assert.DoesNotContain(generated.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackExternalFormatters.g.cs"));
    }

    [Fact]
    public void DiagnosticNamesTheWholeNestedPathUnlessTheIntegrationSuppliesAnOverride()
    {
        var compilation = Create("""
            namespace Vendor;
            public sealed class Envelope { public List<Node>? Nodes { get; set; } }
            public sealed class Node { public IPayload? Payload { get; set; } }
            public interface IPayload { }
            """, """
            [assembly: SharpPackExternalType(typeof(Vendor.Envelope))]
            """);
        var (diagnostics, _) = Run(compilation);
        var failure = Assert.Single(diagnostics);
        Assert.Equal("SHARPPACK045", failure.Id);
        Assert.Contains("global::Vendor.Envelope.Nodes -> type argument 1.Payload", failure.GetMessage());

        var root = compilation.GetTypeByMetadataName("Vendor.Envelope")!;
        var custom = compilation.GetTypeByMetadataName("Vendor.IPayload")!;
        var integration = SharpPackSidecarGenerator.Generate(compilation, [new(root)], "CustomProvider", [custom]);
        Assert.Empty(integration.Diagnostics);
        Assert.Equal(2, integration.Roots[0].Sidecars.Length);
        Assert.DoesNotContain("Register<global::Vendor.IPayload>", integration.Source);
    }

    static Compilation Create(string vendor, string consumer)
    {
        using var bytes = new MemoryStream();
        var vendorCompilation = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText(vendor, Parse, "Vendor.cs"))
            .WithAssemblyName("SidecarPlanningVendor");
        var emitted = vendorCompilation.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText(consumer, Parse, "Consumer.cs"))
            .AddReferences(MetadataReference.CreateFromImage(bytes.ToArray()));
    }

    static (ImmutableArray<Diagnostic> Diagnostics, Compilation Compilation) Run(Compilation compilation)
    {
        CSharpGeneratorDriver.Create([new SharpPackGenerator().AsSourceGenerator()], parseOptions: Parse)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        return (diagnostics, generated);
    }
}
