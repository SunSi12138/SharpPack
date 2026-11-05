using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack.Generator;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class RuntimeAbiDiagnosticsTest
{
    static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);
    static readonly MetadataReference[] FrameworkReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) ||
            Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll" or "Microsoft.CSharp.dll")
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    const string Models = """
        using SharpPack;
        [SharpPackable]
        public partial class First { public int Value { get; set; } }
        [SharpPackable]
        public partial class Second { public int Value { get; set; } }
        """;

    [Fact]
    public void MatchingRuntime_GeneratesAndCompiles()
    {
        var (output, diagnostics, result) = Run(CurrentRuntime(), Models);
        diagnostics.Should().BeEmpty();
        result.GeneratedTrees.Should().HaveCount(2);
        output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("public const int Version = 1;", "1 (missing compiler-services capability: PreserveAttribute)")]
    [InlineData("public const int Version = 0;", "0")]
    [InlineData("public const int Version = 2;", "2")]
    [InlineData("public const int Version = -1;", "-1")]
    [InlineData("public const int Version = int.MaxValue;", "2147483647")]
    [InlineData("public static readonly int Version = 1;", "invalid")]
    [InlineData("public const string Version = \"1\";", "invalid")]
    [InlineData("internal const int Version = 1;", "invalid")]
    [InlineData("public static int Version => 1;", "invalid")]
    [InlineData("", "invalid")]
    public void UnsupportedRuntime_ReportsOnceBeforeEmission(string? member, string detected)
    {
        var (output, diagnostics, result) = Run(CreateRuntime(member), Models);
        AssertMismatch(output, diagnostics, result, detected, "First");
    }

    [Fact]
    public void ExternalUnionOnly_ReportsOnceAndDoesNotGenerateFactories()
    {
        const string source = """
            using SharpPack;
            public partial interface IUnion { }
            [SharpPackUnionFormatter(typeof(IUnion))]
            public partial class UnionFormatter { }
            """;
        var (output, diagnostics, result) = Run(CreateRuntime(null), source);
        AssertMismatch(output, diagnostics, result, "missing", "UnionFormatter");
    }

    [Fact]
    public void ConsumerContract_CannotHideMissingRuntimeContract()
    {
        var (output, diagnostics, result) = Run(CreateRuntime(null), Models + """

            namespace SharpPack.CompilerServices
            {
                public static class SharpPackGeneratedCodeContract
                {
                    public const int Version = 1;
                }
            }
            """);
        AssertMismatch(output, diagnostics, result, "missing", "First");
    }

    [Fact]
    public void ConsumerContract_CannotOverrideMatchingRuntimeContract()
    {
        var (output, diagnostics, result) = Run(CurrentRuntime(), Models + """

            namespace SharpPack.CompilerServices
            {
                public static class SharpPackGeneratedCodeContract
                {
                    public const int Version = 999;
                }
            }
            """);
        diagnostics.Should().BeEmpty();
        result.GeneratedTrees.Should().HaveCount(2);
        output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void NoAnnotatedTypes_DoesNotRequireRuntime()
    {
        var compilation = CreateCompilation(null, "public class Ordinary { }");
        NewDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        diagnostics.Should().BeEmpty();
        output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void IncrementalRuntimeReplacement_RechecksAbiAndDropsStaleOutput()
    {
        GeneratorDriver driver = NewDriver();
        var input = CreateCompilation(CreateRuntime(null), Models);
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out _, out var oldDiagnostics);
        oldDiagnostics.Should().ContainSingle(x => x.Id == "SHARPPACK043");
        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();

        input = input.WithReferences(FrameworkReferences.Append(CurrentRuntime()));
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var currentDiagnostics);
        currentDiagnostics.Should().BeEmpty();
        driver.GetRunResult().GeneratedTrees.Should().HaveCount(2);
        output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        input = input.WithReferences(FrameworkReferences.Append(CreateRuntime("public const int Version = 0;")));
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out output, out var downgradeDiagnostics);
        AssertMismatch(output, downgradeDiagnostics, driver.GetRunResult(), "0", "First");
    }

    static void AssertMismatch(Compilation output, ImmutableArray<Diagnostic> diagnostics,
        GeneratorDriverRunResult result, string detected, string identifier)
    {
        diagnostics.Should().ContainSingle();
        var diagnostic = diagnostics.Single();
        diagnostic.Id.Should().Be("SHARPPACK043");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain($"ABI '{detected}'")
            .And.Contain("[1, 1]").And.Contain("SharpPack.Core")
            .And.Contain("SharpPack.Generator").And.Contain("Align");
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)
            .Should().Be(identifier);
        result.GeneratedTrees.Should().BeEmpty();
        result.Results.Should().OnlyContain(x => x.Exception == null);
        // The deliberately minimal old runtime has no reader/writer/formatter
        // symbols. No output means it cannot produce unrelated compiler cascades.
        output.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    static (Compilation, ImmutableArray<Diagnostic>, GeneratorDriverRunResult) Run(
        MetadataReference runtime, string source)
    {
        var driver = NewDriver().RunGeneratorsAndUpdateCompilation(
            CreateCompilation(runtime, source), out var output, out var diagnostics);
        return (output, diagnostics, driver.GetRunResult());
    }

    static GeneratorDriver NewDriver() => CSharpGeneratorDriver.Create(
        new[] { new SharpPackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);

    static CSharpCompilation CreateCompilation(MetadataReference? runtime, string source) =>
        CSharpCompilation.Create("AbiConsumer",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Models.cs") },
            runtime is null ? FrameworkReferences : FrameworkReferences.Append(runtime),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

    static MetadataReference CurrentRuntime() =>
        MetadataReference.CreateFromFile(typeof(SharpPackableAttribute).Assembly.Location);

    static MetadataReference CreateRuntime(string? member)
    {
        var source = """
            namespace SharpPack
            {
                public sealed class SharpPackableAttribute : System.Attribute { }
                public sealed class SharpPackUnionFormatterAttribute : System.Attribute
                {
                    public SharpPackUnionFormatterAttribute(System.Type type) { }
                }
            }
            """;
        if (member is not null)
        {
            source += "namespace SharpPack.CompilerServices { public static class SharpPackGeneratedCodeContract { "
                + member + " } }";
        }

        var compilation = CSharpCompilation.Create("SharpPack.Core",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) }, FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
