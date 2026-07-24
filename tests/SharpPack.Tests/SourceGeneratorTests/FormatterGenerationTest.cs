using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class FormatterGenerationTest
{
    [Fact]
    public void FormatterOverrideHelpers_AvoidUserMemberNameCollisions()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Model
{
    public int Value { get; set; }

    static void __SharpPackSerializeWithFormatterOverrides()
    {
    }

    static int __SharpPackDeserializeWithFormatterOverrides => 0;
}
""";

        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            compilation.SyntaxTrees
                .Where(static tree =>
                    tree.FilePath.EndsWith(
                        ".g.cs",
                        StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "__SharpPackSerializeWithFormatterOverrides_<TBufferWriter>");
        generated.Should().Contain(
            "bool __SharpPackDeserializeWithFormatterOverrides_(");
    }

    [Fact]
    public void FormatterOverridePaths_AreEmittedAsColdHelpers()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Model
{
    public int Value { get; set; }
    public string? Name { get; set; }
    public int[]? Values { get; set; }
}
""";

        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            compilation.SyntaxTrees
                .Where(static tree =>
                    tree.FilePath.EndsWith(
                        ".g.cs",
                        StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "MethodImplOptions.NoInlining");
        generated.Should().Contain(
            "__SharpPackSerializeWithFormatterOverrides");
        generated.Should().Contain(
            "__SharpPackDeserializeWithFormatterOverrides");
        generated.Should().Contain(
            "writer.OptionalState.HasFormatterOverrides &&");
        generated.Should().Contain(
            "reader.OptionalState.HasFormatterOverrides &&");
        generated.Should().Contain(
            "writer.WriteUnmanagedArray(value.@Values);");
        generated.Should().Contain(
            "__Values = reader.ReadUnmanagedArray<int>();");
        generated.Should().Contain(
            "writer.WriteValue(value.@Values);");
    }

    [Fact]
    public void CustomFormatterAttributes_AreConstructedWithoutMemberReflection()
    {
        var source = """
namespace Generated;

public sealed class CustomIntFormatter(int offset) : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + offset);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - offset;
    }
}

public sealed class CustomIntFormatterAttribute(
    int offset,
    Type targetType,
    string marker,
    int[] increments)
    : SharpPackCustomFormatterAttribute<CustomIntFormatter, int>
{
    public bool Enabled { get; set; }

    public override CustomIntFormatter GetFormatter()
        => new(Enabled && targetType == typeof(Model) && marker == "marker"
            ? offset + increments.Sum()
            : 0);
}

[SharpPackable]
public partial class Model
{
    [CustomIntFormatter(
        7,
        typeof(Model),
        "marker",
        new[] { 2, 3 },
        Enabled = true)]
    public int Value { get; set; }
}
""";

        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            compilation.SyntaxTrees
                .Where(static tree =>
                    tree.FilePath.EndsWith(
                        ".g.cs",
                        StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));
        generated.Should().NotContain("System.Reflection");
        generated.Should().Contain(
            "new global::Generated.CustomIntFormatterAttribute(" +
            "7, typeof(global::Generated.Model), \"marker\", " +
            "new int[] { 2, 3 }) " +
            "{ @Enabled = true }.GetFormatter()");
    }
}
