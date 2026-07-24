using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace MemoryPack.Tests.SourceGeneratorTests;

public class FormatterGenerationTest
{
    [Fact]
    public void CustomFormatterAttributes_AreConstructedWithoutMemberReflection()
    {
        var source = """
namespace Generated;

public sealed class CustomIntFormatter(int offset) : MemoryPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + offset);

    public override void Deserialize(
        ref MemoryPackReader reader,
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
    : MemoryPackCustomFormatterAttribute<CustomIntFormatter, int>
{
    public bool Enabled { get; set; }

    public override CustomIntFormatter GetFormatter()
        => new(Enabled && targetType == typeof(Model) && marker == "marker"
            ? offset + increments.Sum()
            : 0);
}

[MemoryPackable]
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
