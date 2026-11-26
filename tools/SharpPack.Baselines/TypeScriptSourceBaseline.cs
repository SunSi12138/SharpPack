using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpPack;
using SharpPack.Generator;
using System.Diagnostics.CodeAnalysis;
using System.Text;

static class TypeScriptSourceBaseline
{
    const string Fixture = """
        #nullable enable
        using System.Collections.Generic;
        using SharpPack;

        namespace TypeScriptFixtures;

        [SharpPackable, GenerateTypeScript]
        [SharpPackUnion(4, typeof(Packet))]
        [SharpPackUnion(300, typeof(Fault))]
        public partial interface IWireMessage;

        [SharpPackable(SerializeLayout.Explicit), GenerateTypeScript]
        public partial class Packet : IWireMessage
        {
            [SharpPackOrder(4)] public List<List<int>>? Groups { get; set; }
            [SharpPackOrder(2)] public string? Details { get; set; }
            [SharpPackOrder(0)] public string Topic { get; set; } = "";
            [SharpPackOrder(1)] public Status State { get; set; }
            [SharpPackOrder(3)] public Dictionary<int, List<string?>>? Index { get; set; }
            [SharpPackOrder(5)] public byte[]? Body { get; set; }
            [SharpPackIgnore] public string? Scratch { get; set; }
        }

        [SharpPackable, GenerateTypeScript]
        public partial record Fault(int Code) : IWireMessage;

        public enum Status : byte { Pending = 0, Ready = 2 }
        """;

    internal static string Create()
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: ["NET10_0_OR_GREATER"]);
        var compilation = CSharpCompilation.Create("TypeScriptBaseline",
            [CSharpSyntaxTree.ParseText(Fixture, options, "TypeScriptModels.cs")],
            GeneratedSourceBaseline.TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(SharpPackableAttribute).Assembly.Location))
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var driver = CSharpGeneratorDriver.Create([new SharpPackGenerator().AsSourceGenerator()], parseOptions: options,
            optionsProvider: new BaselineOptions());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        var errors = diagnostics.Concat(generated.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("TypeScript fixture failed to compile:\n" + string.Join("\n", errors.Select(d => d.ToString())));

        var source = generated.SyntaxTrees.Single(tree => tree.FilePath.EndsWith(ArtifactSchema.FileName, StringComparison.Ordinal));
        using var reader = new StringReader(source.ToString());
        var plan = ArtifactSchema.ReadSource(reader).TypeScript ?? throw new InvalidOperationException("Missing TypeScript plan.");
        var files = TypeScriptEmitter.RenderFiles(plan).OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
        string[] expectedNames = ["Fault.ts", "IWireMessage.ts", "Packet.ts", "SharpPackReader.ts", "SharpPackWriter.ts", "Status.ts"];
        if (!files.Select(file => file.Name).SequenceEqual(expectedNames, StringComparer.Ordinal))
            throw new InvalidOperationException("Unexpected TypeScript files: " + string.Join(", ", files.Select(file => file.Name)));

        var snapshot = new StringBuilder("# Representative TypeScript output\n");
        foreach (var (name, contents, isRuntime) in files)
        {
            if (isRuntime) continue; // The packaged-consumer gate checks the files on disk.
            snapshot.AppendLine().Append("===== ").Append(name).AppendLine(" =====");
            snapshot.AppendLine(contents.TrimEnd());
        }
        return snapshot.ToString();
    }

    sealed class BaselineOptions : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Properties();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        sealed class Properties : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key switch
                {
                    "build_property.SharpPackGenerator_ArtifactMode" => "plan",
                    "build_property.SharpPackGenerator_TypeScriptOutputDirectory" => "typescript",
                    "build_property.SharpPackGenerator_TypeScriptImportExtension" => ".mjs",
                    "build_property.SharpPackGenerator_TypeScriptEnableNullableTypes" => "true",
                    _ => null,
                };
                return value is not null;
            }
        }
    }
}
