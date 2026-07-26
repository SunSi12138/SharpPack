using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class FormatterGenerationTest
{
    [Fact]
    public void UnmanagedAnnotations_RemainOnRawCopyPath()
    {
        var source = """
namespace Generated;

public sealed class VarIntFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteVarInt(value);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
        => value = reader.ReadVarIntInt32();
}

public sealed class VarIntAttribute
    : SharpPackCustomFormatterAttribute<VarIntFormatter, int>
{
    public override VarIntFormatter GetFormatter() => new();
}

[SharpPackable]
public partial struct Formatted
{
    [VarInt]
    public int Value { get; set; }
    public long Tail { get; set; }
}

[SharpPackable]
public partial struct Nested
{
    public Formatted Value { get; set; }
}

[SharpPackable]
public partial struct Plain
{
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
        var generated = compilation.SyntaxTrees
            .Where(static tree => tree.FilePath.EndsWith(
                ".g.cs",
                StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .ToArray();
        var formatted = generated.Single(static text =>
            text.Contains("partial struct Formatted",
                StringComparison.Ordinal));
        var nested = generated.Single(static text =>
            text.Contains("partial struct Nested",
                StringComparison.Ordinal));
        var plain = generated.Single(static text =>
            text.Contains("partial struct Plain",
                StringComparison.Ordinal));

        formatted.Should().NotContain(
            "ISharpPackUnmanagedRawCopyDisabled");
        formatted.Should().NotContain("__ValueFormatter");
        formatted.Should().Contain("writer.WriteUnmanaged(value);");
        nested.Should().NotContain(
            "ISharpPackUnmanagedRawCopyDisabled");
        nested.Should().Contain("writer.WriteUnmanaged(value);");
        plain.Should().Contain("writer.WriteUnmanaged(value);");
    }

    [Fact]
    public void PackableLists_UseBulkHelperForUnmanagedElements()
    {
        var source = """
namespace Generated;

public sealed class VarIntFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteVarInt(value);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
        => value = reader.ReadVarIntInt32();
}

public sealed class VarIntAttribute
    : SharpPackCustomFormatterAttribute<VarIntFormatter, int>
{
    public override VarIntFormatter GetFormatter() => new();
}

[SharpPackable]
public partial struct Plain
{
    public int Value { get; set; }
}

[SharpPackable]
public partial struct Formatted
{
    [VarInt]
    public int Value { get; set; }
}

[SharpPackable]
public partial class PlainListHolder
{
    public List<Plain>? Values { get; set; }
}

[SharpPackable]
public partial class FormattedListHolder
{
    public List<Formatted>? Values { get; set; }
}
""";
        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var generated = compilation.SyntaxTrees
            .Where(static tree => tree.FilePath.EndsWith(
                ".g.cs",
                StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .ToArray();
        var plainHolder = generated.Single(static text =>
            text.Contains("partial class PlainListHolder",
                StringComparison.Ordinal));
        var formattedHolder = generated.Single(static text =>
            text.Contains("partial class FormattedListHolder",
                StringComparison.Ordinal));

        plainHolder.Should().Contain(
            "SerializePackableUnmanaged");
        plainHolder.Should().Contain(
            "DeserializePackableUnmanaged");
        formattedHolder.Should().Contain(
            "SerializePackableUnmanaged");
        formattedHolder.Should().Contain(
            "DeserializePackableUnmanaged");
    }

    [Fact]
    public void ExactSizeContract_IsEmittedOnlyForEligibleObjects()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Eligible
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public byte[]? Bytes { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Versioned
{
    [SharpPackOrder(0)]
    public int Id { get; set; }
}

[SharpPackable]
public partial class WithCallback
{
    public int Id { get; set; }

    [SharpPackOnSerializing]
    static void Before() { }
}

[SharpPackable]
public partial class WithCustomGetter
{
    int value;
    public int Value => value;
}

[SharpPackable]
public partial record PositionalRecord(int Id, string Text);

[SharpPackable]
public partial class WithVirtualProperty
{
    public virtual int Value { get; set; }
}

[SharpPackable]
public partial class WithPartialProperty
{
    public partial int Value { get; }
    public partial int Value => 42;
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
                .Where(static tree => tree.FilePath.EndsWith(
                    ".g.cs",
                    StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "ISharpPackExactSizeSerializable<Eligible>");
        generated.Should().Contain(
            "ISharpPackExactSizeSerializable<PositionalRecord>");
        generated.Should().Contain("SerializeExact()");
        generated.Should().Contain("var size = 1L +");
        generated.Should().Contain("(ulong)global::System.Array.MaxLength");
        generated.Should().Contain("ThrowSizeOverflow()");
        generated.Should().NotContain(
            "ISharpPackExactSizeSerializable<Versioned>");
        generated.Should().NotContain(
            "ISharpPackExactSizeSerializable<WithCallback>");
        generated.Should().NotContain(
            "ISharpPackExactSizeSerializable<WithCustomGetter>");
        generated.Should().NotContain(
            "ISharpPackExactSizeSerializable<WithVirtualProperty>");
        generated.Should().NotContain(
            "ISharpPackExactSizeSerializable<WithPartialProperty>");
    }

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
            "void __SharpPackDeserializeWithFormatterOverrides_(");
    }

    [Fact]
    public void FormatterOverrideHelpers_AvoidTypeParameterCollisions()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Model<
    __SharpPackSerializeWithFormatterOverrides,
    __SharpPackDeserializeWithFormatterOverrides>
{
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
                .Where(static tree => tree.FilePath.EndsWith(
                    ".g.cs",
                    StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "__SharpPackSerializeWithFormatterOverrides_<TBufferWriter>");
        generated.Should().Contain(
            "void __SharpPackDeserializeWithFormatterOverrides_(");
    }

    [Fact]
    public void ContextFormatterType_AvoidsUserNestedTypeCollisions()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Model
{
    public int Value { get; set; }
    public string? Name { get; set; }

    sealed class __SharpPackContextFormatter
    {
    }
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
                .Where(static tree => tree.FilePath.EndsWith(
                    ".g.cs",
                    StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "sealed class __SharpPackContextFormatter_");
        generated.Should().Contain(
            "return new __SharpPackContextFormatter_();");
    }

    [Fact]
    public void ContextFormatterType_AvoidsTypeParameterCollisions()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Model<__SharpPackContextFormatter>
{
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
                .Where(static tree => tree.FilePath.EndsWith(
                    ".g.cs",
                    StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "sealed class __SharpPackContextFormatter_");
        generated.Should().Contain(
            "return new __SharpPackContextFormatter_();");
    }

    [Fact]
    public void AotRootHelper_AvoidsTypeParameterCollisions()
    {
        var source = """
namespace Generated;

[SharpPackable]
public partial class Child
{
    public int Value { get; set; }
}

[SharpPackable]
public partial class Model<__SharpPackEnsureAotFormatterRoots>
{
    public Child? Value { get; set; }
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
                .Where(static tree => tree.FilePath.EndsWith(
                    ".g.cs",
                    StringComparison.Ordinal))
                .Select(static tree => tree.ToString()));

        generated.Should().Contain(
            "static void __SharpPackEnsureAotFormatterRoots_()");
        generated.Should().Contain(
            "__SharpPackEnsureAotFormatterRoots_();");
    }

    [Fact]
    public void GeneratedHelpers_AvoidContainingTypeParameterCollisions()
    {
        var source = """
namespace Generated;

public partial class Outer<
    __SharpPackContextFormatter,
    __SharpPackEnsureAotFormatterRoots,
    __SharpPackSerializeWithFormatterOverrides,
    __SharpPackDeserializeWithFormatterOverrides>
{
    [SharpPackable]
    public partial class Model
    {
        public int Value { get; set; }
        public string? Name { get; set; }
    }
}
""";

        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void NestedModels_PreserveRequiredContainingTypeModifiers()
    {
        var source = """
namespace Generated;

public static partial class StaticOuter
{
    [SharpPackable]
    public partial class Model
    {
        public int Value { get; set; }
    }
}

public readonly partial struct ReadOnlyOuter
{
    [SharpPackable]
    public partial class Model
    {
        public int Value { get; set; }
    }
}

public ref partial struct RefOuter
{
    [SharpPackable]
    public partial class Model
    {
        public int Value { get; set; }
    }
}

""";

        var (compilation, diagnostics) =
            CSharpGeneratorRunner.RunGenerator(source);

        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
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
            "ISharpPackContextFormatterFactory<Model>.CreateFormatter(");
        generated.Should().Contain(
            "context.HasFormatterOverrideDependency<int>()");
        generated.Should().Contain(
            "sealed class __SharpPackContextFormatter");
        generated.Should().NotContain(
            "writer.OptionalState.HasFormatterOverrides &&");
        generated.Should().NotContain(
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
