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
        var directory = Directory.CreateTempSubdirectory("SharpPack TS 样例 ");
        try
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
                optionsProvider: new BaselineOptions(directory.FullName));
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
            var errors = diagnostics.Concat(generated.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException("TypeScript fixture failed to compile:\n" + string.Join("\n", errors.Select(d => d.ToString())));

            string[] expectedNames = ["Fault.ts", "IWireMessage.ts", "Packet.ts", "SharpPackReader.ts", "SharpPackWriter.ts", "Status.ts"];
            var files = directory.EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
            if (!files.Select(file => file.Name).SequenceEqual(expectedNames, StringComparer.Ordinal))
                throw new InvalidOperationException("Unexpected TypeScript files: " + string.Join(", ", files.Select(file => file.Name)));

            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var snapshot = new StringBuilder("# Representative TypeScript output\n");
            foreach (var file in files)
            {
                var bytes = File.ReadAllBytes(file.FullName);
                var runtime = file.Name switch
                {
                    "SharpPackReader.ts" => TypeScriptRuntime.SharpPackReader,
                    "SharpPackWriter.ts" => TypeScriptRuntime.SharpPackWriter,
                    _ => null,
                };
                if (runtime is not null)
                {
                    if (!bytes.AsSpan().SequenceEqual(utf8.GetBytes(runtime)))
                        throw new InvalidOperationException($"{file.Name} does not match the shipped UTF-8 runtime.");
                    continue;
                }
                snapshot.AppendLine().Append("===== ").Append(file.Name).AppendLine(" =====");
                snapshot.AppendLine(utf8.GetString(bytes).TrimEnd());
            }
            return snapshot.ToString();
        }
        finally
        {
            foreach (var file in directory.EnumerateFiles()) file.Delete();
            directory.Delete();
        }
    }

    sealed class BaselineOptions(string directory) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Properties(directory);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        sealed class Properties(string directory) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                value = key switch
                {
                    "build_property.SharpPackGenerator_TypeScriptOutputDirectory" => directory,
                    "build_property.SharpPackGenerator_TypeScriptImportExtension" => ".mjs",
                    "build_property.SharpPackGenerator_TypeScriptEnableNullableTypes" => "true",
                    _ => null,
                };
                return value is not null;
            }
        }
    }
}
