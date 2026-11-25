using System.Linq;
using Microsoft.CodeAnalysis;

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
}
