using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack.Generator;

namespace SharpPack.Tests.SourceGeneratorTests;

public class DeserializeArrayRootsTest
{
    [Fact]
    public void RootsOnlyClosedManagedArraysPassedToTheRealDeserializer()
    {
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator("""
            using Serializer = SharpPack.SharpPackSerializer;
            [SharpPackable] public partial class Item { public int Value { get; set; } }
            [SharpPackable] public partial class Box<T> { public T? Value { get; set; } }
            [SharpPackable] public partial class Other { public int Value { get; set; } }

            public partial class Program
            {
                [SharpPackable] private partial class Hidden { public int Value { get; set; } }
                static Item[]? A(byte[] bytes) => Serializer.Deserialize<Item[]>(bytes);
                static Item?[]? B(byte[] bytes) => Serializer.Deserialize<Item?[]>(bytes);
                static Box<string>[]? C(byte[] bytes) => Serializer.Deserialize<Box<string>[]>(bytes);
                static Box<T>[]? Open<T>(byte[] bytes) => Serializer.Deserialize<Box<T>[]>(bytes);
                static int[]? Raw(byte[] bytes) => Serializer.Deserialize<int[]>(bytes);
                static Hidden[]? Private(byte[] bytes) => Serializer.Deserialize<Hidden[]>(bytes);
                static Other[]? Unrelated(byte[] bytes) => Deserialize<Other[]>(bytes);
                static T? Deserialize<T>(byte[] bytes) => default;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var root = Assert.Single(compilation.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackDeserializeArrayRoots.g.cs"));
        var source = root.GetText().ToString();
        Assert.Equal(2, source.Split("PreserveFormatterFactory<").Length - 1);
        Assert.Contains("PreserveFormatterFactory<global::Item>()", source);
        Assert.Contains("PreserveContextFormatterFactory<global::Box<string>>()", source);
        Assert.Contains("new global::SharpPack.Formatters.ArrayFormatter<global::Box<string>>()", source);
    }

    [Fact]
    public void KeepsExistingCallsThroughAnAliasOnlyMetadataReference()
    {
        var (vendor, vendorDiagnostics) = CSharpGeneratorRunner.RunGenerator("""
            namespace Vendor;
            [SharpPackable] public partial class Item { public int Value { get; set; } }
            """);
        Assert.Empty(vendorDiagnostics);
        using var bytes = new MemoryStream();
        var emitted = vendor.WithAssemblyName("ArrayRootVendor").Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var parse = new CSharpParseOptions(LanguageVersion.CSharp14);
        var consumer = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText("""
            extern alias External;
            public static class Reader
            {
                public static External::Vendor.Item[] Read(byte[] bytes) =>
                    SharpPackSerializer.Deserialize<External::Vendor.Item[]>(bytes);
            }
            """, parse)).AddReferences(MetadataReference.CreateFromImage(bytes.ToArray()).WithAliases(["External"]));
        CSharpGeneratorDriver.Create([new SharpPackGenerator().AsSourceGenerator()], parseOptions: parse)
            .RunGeneratorsAndUpdateCompilation(consumer, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(compilation.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackDeserializeArrayRoots.g.cs"));
    }

    [Fact]
    public void DoesNotIntroduceWarningsOrErrorsForObsoleteContracts()
    {
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator("""
            [Obsolete("Legacy data", true), SharpPackable]
            public partial class OldItem { public int Value { get; set; } }
            public static class Reader
            {
                [Obsolete("Legacy data")]
                public static OldItem[] Read(byte[] bytes) => SharpPackSerializer.Deserialize<OldItem[]>(bytes);
            }
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id == "CS0618");
        Assert.Contains(compilation.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackDeserializeArrayRoots.g.cs"));
    }

    [Fact]
    public void KeepsExistingUnionsButDoesNotAssumeCallerLocalExperimentalOptIn()
    {
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator("""
            #pragma warning disable SHARPPACKTEST
            [System.Diagnostics.CodeAnalysis.Experimental("SHARPPACKTEST"), SharpPackable]
            public partial class ExperimentalItem { public int Value { get; set; } }
            [SharpPackable] public partial class Box<T> { public T? Value { get; set; } }
            [SharpPackable(GenerateType.NoGenerate), SharpPackUnion(1, typeof(Case))]
            public partial interface Contract { }
            [SharpPackable] public partial class Case : Contract { public int Value { get; set; } }
            public static class Reader
            {
                public static ExperimentalItem[]? Preview(byte[] bytes) => SharpPackSerializer.Deserialize<ExperimentalItem[]>(bytes);
                public static Box<ExperimentalItem>[]? Nested(byte[] bytes) => SharpPackSerializer.Deserialize<Box<ExperimentalItem>[]>(bytes);
                public static Contract[]? Union(byte[] bytes) => SharpPackSerializer.Deserialize<Contract[]>(bytes);
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var root = Assert.Single(compilation.SyntaxTrees, static tree => tree.FilePath.EndsWith("SharpPackDeserializeArrayRoots.g.cs"));
        var source = root.GetText().ToString();
        Assert.Contains("PreserveFormatterFactory<global::Contract>()", source);
        Assert.DoesNotContain("PreserveContextFormatterFactory", source);
        Assert.DoesNotContain("ExperimentalItem", source);
    }
}
