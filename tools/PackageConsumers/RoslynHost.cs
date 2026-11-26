using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack.Generator;

var actualVersion = typeof(CSharpCompilation).Assembly.GetName().Version?.ToString();
if (actualVersion != args[0])
    throw new Exception($"Expected Roslyn {args[0]}, loaded {actualVersion}");

var parse = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: ["NET10_0_OR_GREATER"]);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(args[1]).Distinct().Select(path => MetadataReference.CreateFromFile(path));
var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(args[2]), parse, path: "Model.cs");
var compilation = CSharpCompilation.Create("PackageHostFixture", [syntax], references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
var driver = CSharpGeneratorDriver.Create(new SharpPackGenerator()).WithUpdatedParseOptions(parse)
    .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

var output = driver.GetRunResult().Results.Single();
if (output.Exception is not null)
    throw output.Exception;

if (args[3] == "mismatch")
{
    var diagnostic = diagnostics.Single();
    if (diagnostic.Id != "SHARPPACK043" || diagnostic.Severity != DiagnosticSeverity.Error ||
        diagnostic.Location.SourceTree != syntax || output.GeneratedSources.Length != 0 ||
        updated.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
    {
        throw new Exception("Expected exactly one ABI error and no generated output");
    }
}
else
{
    using var stream = new MemoryStream();
    var emit = updated.Emit(stream);
    if (diagnostics.Length != 0 || !emit.Success || output.GeneratedSources.Length != 2)
        throw new Exception(string.Join("\n", diagnostics.Concat(emit.Diagnostics)));

    var sources = output.GeneratedSources.OrderBy(item => item.HintName, StringComparer.Ordinal)
        .Select(item => item.HintName + "\n" + item.SourceText.ToString().Replace("\r\n", "\n"));
    File.WriteAllText(args[3], string.Join("\n", sources));
}

Console.WriteLine($"Roslyn {actualVersion}: {(args[3] == "mismatch" ? "ABI mismatch" : "generation and emit")} passed");
