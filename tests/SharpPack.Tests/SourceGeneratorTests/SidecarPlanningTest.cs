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
            .Single(static shape => shape.Type.Name == "Node").Members.Select(static member => member.Name));
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
    [InlineData("public sealed class Payload { public int Value { get; set; } public Payload(int value) => Value = value; [SharpPackConstructor] private Payload() { } }", "constructor marked SharpPackConstructor is not accessible")]
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
    public void ExternalConstructorCanReuseOneSlotWithDifferentCase()
    {
        var compilation = Create("""
            namespace Vendor;
            public sealed class Payload(int value, int Value)
            {
                public int Value { get; } = Value;
                public int GetLowercaseValue() => value;
            }
            """, """
            [assembly: SharpPackExternalType(typeof(Vendor.Payload))]
            """);
        var (diagnostics, generated) = Run(compilation);
        Assert.Empty(diagnostics);
        Assert.Contains(generated.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackExternalFormatters.g.cs"));
        Assert.Empty(generated.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
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

    [Fact]
    public void WireShapesExposeSymbolsAnIntegrationCanCanonicalize()
    {
        var compilation = Create("""
            namespace Vendor;
            public sealed class Box<T>
            {
                [SharpPackOrder(1)] public T Value { get; set; }
                [SharpPackOrder(0)] public int Count { get; set; }
            }
            """, """
            public class Roots { public Vendor.Box<(int Id, string Label)> Model { get; set; } }
            """);
        var root = Assert.IsAssignableFrom<IPropertySymbol>(compilation.GetTypeByMetadataName("Roots")!.GetMembers("Model")[0]);
        var result = SharpPackSidecarGenerator.Generate(compilation, [new(root.Type)], "TupleProvider");
        Assert.Empty(result.Diagnostics);
        var sidecar = Assert.Single(Assert.Single(result.Roots).Sidecars);
        Assert.Equal([(0, "Count"), (1, "Value")], sidecar.Members.Select(static member => (member.Position, member.Name)));
        Assert.Equal(SpecialType.System_Int32, sidecar.Members[0].Type.SpecialType);
        var member = sidecar.Members[1];
        Assert.True(SymbolEqualityComparer.Default.Equals(root.Type, sidecar.Type));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            ((IPropertySymbol)root.Type.GetMembers("Value")[0]).Type, member.Type));

        var identityFormat = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.ExpandValueTuple);
        Assert.Equal("global::Vendor.Box<global::System.ValueTuple<int, string>>", sidecar.Type.ToDisplayString(identityFormat));
        Assert.Equal("global::System.ValueTuple<int, string>", member.Type.ToDisplayString(identityFormat));
    }

    [Fact]
    public void SourceExternalUnionReusesExactThenOpenAndRejectsAnUnmatchedClosedRoot()
    {
        var compilation = Create("""
            namespace Vendor;
            public sealed class ExactPayload { public int Value { get; set; } }
            public sealed class OpenPayload { public int Value { get; set; } }
            """, """
            [SharpPackable(GenerateType.NoGenerate)]
            public partial interface IUnion<T>;
            [SharpPackable]
            public partial class ExactCase : IUnion<int>, IExactOnly<int>
            {
                [SharpPackAllowSerialize] public Vendor.ExactPayload? Payload { get; set; }
            }
            [SharpPackable]
            public partial class OpenCase<T> : IUnion<T>
            {
                [SharpPackAllowSerialize] public Vendor.OpenPayload? Payload { get; set; }
            }
            [SharpPackUnionFormatter(typeof(IUnion<int>))]
            [SharpPackUnion(1, typeof(ExactCase))]
            public partial class ExactFormatter;
            [SharpPackUnionFormatter(typeof(IUnion<>))]
            [SharpPackUnion(2, typeof(OpenCase<>))]
            public partial class OpenFormatter<T>;
            public partial interface IExactOnly<T>;
            [SharpPackUnionFormatter(typeof(IExactOnly<int>))]
            [SharpPackUnion(3, typeof(ExactCase))]
            public partial class OnlyFormatter;
            """);
        var number = compilation.GetSpecialType(SpecialType.System_Int32);
        var text = compilation.GetSpecialType(SpecialType.System_String);
        var union = compilation.GetTypeByMetadataName("IUnion`1")!;
        var exact = compilation.GetTypeByMetadataName("IExactOnly`1")!;
        var output = SharpPackSidecarGenerator.Generate(compilation,
            [new(union.Construct(number)), new(union.Construct(text)), new(exact.Construct(number))], "UnionProvider");
        Assert.Empty(output.Diagnostics);
        Assert.All(output.Roots, static root => Assert.Equal(SharpPackFormatterSupport.Existing, root.Support));
        Assert.Equal("global::Vendor.ExactPayload", Assert.Single(output.Roots[0].Sidecars).Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal("global::Vendor.OpenPayload", Assert.Single(output.Roots[1].Sidecars).Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var tree = CSharpSyntaxTree.ParseText(Assert.IsType<string>(output.Source), Parse, output.HintName);
        var (diagnostics, generated) = Run(compilation.AddSyntaxTrees(tree));
        Assert.Empty(diagnostics);
        Assert.Empty(generated.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var unsupported = SharpPackSidecarGenerator.Generate(compilation, [new(exact.Construct(text))], "UnmatchedProvider");
        Assert.Equal(SharpPackFormatterSupport.Unsupported, Assert.Single(unsupported.Roots).Support);
        Assert.Contains("no generated external union formatter matches", Assert.Single(unsupported.Diagnostics).GetMessage());
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
