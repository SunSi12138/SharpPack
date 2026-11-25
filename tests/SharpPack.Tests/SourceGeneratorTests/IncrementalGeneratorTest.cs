using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpPack.Generator;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpPack.Tests.SourceGeneratorTests;

public class IncrementalGeneratorTest
{
    static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    [Fact]
    public void UnrelatedEditCachesPlansAndEmissionButDependencyEditInvalidatesItsModel()
    {
        var models = Parse("""
            [SharpPackable] public partial class Dependent : SharedFields { }
            [SharpPackable] public partial class Independent { public int Count { get; set; } }

            public partial interface IEvent { }
            [SharpPackable] public partial class Event : IEvent { }
            [SharpPackUnionFormatter(typeof(IEvent))]
            [SharpPackUnion(1, typeof(Event))]
            public partial class EventFormatter { }
            """, "Models.cs");
        var dependency = Parse("public class SharedFields { public int Size { get; set; } }", "Dependency.cs");
        var unrelated = Parse("internal class Unrelated { public int X; }", "Unrelated.cs");
        var compilation = CSharpGeneratorRunner.CreateCompilation(models, dependency, unrelated);
        var driver = CreateDriver();
        var first = Run(ref driver, compilation);

        Assert.Empty(first.Diagnostics);
        Assert.Equal(5, first.GeneratedSources.Length);
        AssertStage(first, "ModelAnalysis", 4, IncrementalStepRunReason.New);
        AssertStage(first, "TypeShape", 4, IncrementalStepRunReason.New);
        AssertStage(first, "SerializationPlan", 4, IncrementalStepRunReason.New);
        AssertStage(first, "CSharpEmission", 4, IncrementalStepRunReason.New);
        AssertStage(first, "ExternalUnionFactoryEmission", 1, IncrementalStepRunReason.New);

        compilation = compilation.ReplaceSyntaxTree(unrelated,
            Parse("internal class Unrelated { public string? Name; }", unrelated.FilePath));
        var afterUnrelatedEdit = Run(ref driver, compilation);
        Assert.Empty(afterUnrelatedEdit.Diagnostics);
        AssertStage(afterUnrelatedEdit, "ModelAnalysis", 4, IncrementalStepRunReason.Unchanged);
        AssertStage(afterUnrelatedEdit, "TypeShape", 4, IncrementalStepRunReason.Cached);
        AssertStage(afterUnrelatedEdit, "SerializationPlan", 4, IncrementalStepRunReason.Cached);
        AssertStage(afterUnrelatedEdit, "CSharpEmission", 4, IncrementalStepRunReason.Cached);
        AssertStage(afterUnrelatedEdit, "ExternalUnionFactoryPlans", 1, IncrementalStepRunReason.Unchanged);
        AssertStage(afterUnrelatedEdit, "ExternalUnionFactoryEmission", 1, IncrementalStepRunReason.Cached);
        Assert.Equal(Sources(first), Sources(afterUnrelatedEdit));

        var afterDependencyEdit = Run(ref driver, compilation.ReplaceSyntaxTree(dependency,
            Parse("public class SharedFields { public long Size { get; set; } }", dependency.FilePath)));
        Assert.Empty(afterDependencyEdit.Diagnostics);

        var planSteps = Outputs(afterDependencyEdit, "SerializationPlan");
        var changedPlan = Assert.Single(planSteps, step => step.Reason == IncrementalStepRunReason.Modified);
        Assert.Equal("Dependent", Assert.IsType<SerializationPlan>(changedPlan.Value).Shape.ShortName);
        Assert.Equal(3, planSteps.Count(step => step.Reason == IncrementalStepRunReason.Cached));

        var emissionSteps = Outputs(afterDependencyEdit, "CSharpEmission");
        var changedSource = Assert.Single(emissionSteps, step => step.Reason == IncrementalStepRunReason.Modified);
        var emitted = Assert.IsType<GeneratedSource>(changedSource.Value);
        Assert.Equal("Dependent.SharpPackFormatter.g.cs", emitted.HintName);
        Assert.Contains("Unsafe.SizeOf<long>()", emitted.Text);
        Assert.Equal(3, emissionSteps.Count(step => step.Reason == IncrementalStepRunReason.Cached));
        AssertStage(afterDependencyEdit, "ExternalUnionFactoryEmission", 1, IncrementalStepRunReason.Cached);
        Assert.Equal(Sources(first).Where(source => source.Hint != emitted.HintName).ToArray(),
            Sources(afterDependencyEdit).Where(source => source.Hint != emitted.HintName).ToArray());
    }

    [Fact]
    public void DiagnosticLocationFollowsTheNewSyntaxTreeAndClearsWhenFixed()
    {
        const string declaration = "[SharpPackable] public class Model { }";
        var tree = Parse(declaration, "Model.cs");
        var compilation = CSharpGeneratorRunner.CreateCompilation(tree);
        var driver = CreateDriver();

        var first = Run(ref driver, compilation);
        Assert.Equal("SHARPPACK001", Assert.Single(first.Diagnostics).Id);
        Assert.Empty(first.GeneratedSources);

        var moved = Parse("// inserted\n" + declaration, tree.FilePath);
        compilation = compilation.ReplaceSyntaxTree(tree, moved);
        var afterMove = Run(ref driver, compilation);
        var diagnostic = Assert.Single(afterMove.Diagnostics);
        Assert.Equal("SHARPPACK001", diagnostic.Id);
        Assert.Same(moved, diagnostic.Location.SourceTree);
        Assert.Equal(1, diagnostic.Location.GetLineSpan().StartLinePosition.Line);

        var fixedTree = Parse(declaration.Replace("public class", "public partial class"), tree.FilePath);
        var afterFix = Run(ref driver, compilation.ReplaceSyntaxTree(moved, fixedTree));
        Assert.Empty(afterFix.Diagnostics);
        Assert.Equal("Model.SharpPackFormatter.g.cs", Assert.Single(afterFix.GeneratedSources).HintName);
    }

    [Fact]
    public void SerializationInfoWritesAndRefreshesTheLegacyFileWithoutRenderingCSharpAgain()
    {
        var model = Parse("[SharpPackable] public partial class Model { public int Size; }", "Model.cs");
        var compilation = CSharpGeneratorRunner.CreateCompilation(model);
        var driver = CreateDriver();
        var first = Run(ref driver, compilation);
        var directory = Directory.CreateTempSubdirectory("sharppack-info-");
        var file = Path.Combine(directory.FullName, "Model.txt");
        try
        {
            driver = driver.WithUpdatedAnalyzerConfigOptions(new SerializationInfoOptions(directory.FullName));
            var after = Run(ref driver, compilation);

            AssertStage(after, "CSharpEmission", 1, IncrementalStepRunReason.Cached);
            Assert.Equal(Sources(first), Sources(after));
            var expected = string.Join(Environment.NewLine, "GenerateType Object Model", "---", "int Size", "");
            Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(file));

            File.Delete(file);
            var afterEdit = Run(ref driver, compilation.AddSyntaxTrees(Parse("internal class Unrelated { }", "Unrelated.cs")));
            AssertStage(afterEdit, "CSharpEmission", 1, IncrementalStepRunReason.Cached);
            Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(file));
        }
        finally
        {
            File.Delete(file);
            directory.Delete();
        }
    }

    static SyntaxTree Parse(string source, string path) => CSharpSyntaxTree.ParseText(source, ParseOptions, path);

    static GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        [new SharpPackGenerator().AsSourceGenerator()], parseOptions: ParseOptions,
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    static GeneratorRunResult Run(ref GeneratorDriver driver, Compilation compilation)
    {
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Results.Single();
    }

    static (string Hint, string Text)[] Sources(GeneratorRunResult result)
        => result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
            .Select(source => (source.HintName, source.SourceText.ToString())).ToArray();

    static (object Value, IncrementalStepRunReason Reason)[] Outputs(GeneratorRunResult result, string stage)
        => result.TrackedSteps["SharpPack." + stage].SelectMany(step => step.Outputs).ToArray();

    static void AssertStage(GeneratorRunResult result, string stage, int count, IncrementalStepRunReason reason)
    {
        var outputs = Outputs(result, stage);
        Assert.Equal(count, outputs.Length);
        Assert.All(outputs, output => Assert.Equal(reason, output.Reason));
    }

    sealed class SerializationInfoOptions(string directory) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Properties(directory);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        sealed class Properties(string directory) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key == "build_property.SharpPackGenerator_SerializationInfoOutputDirectory" ? directory : null;
                return value is not null;
            }
        }
    }
}


