using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack.Generator;
var actualVersion = typeof(CSharpCompilation).Assembly.GetName().Version!;
if (actualVersion.ToString() != args[0]) throw new Exception("Unexpected Roslyn host");
var language = args[0] == "5.6.0.0" ? Enum.Parse<LanguageVersion>("CSharp14") : LanguageVersion.Preview;
var parse = new CSharpParseOptions(language, preprocessorSymbols: new[] { "NET10_0_OR_GREATER" });
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(args[1]).Distinct().Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("PackageHostFixture", new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(args[2]), parse) },
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
GeneratorDriver driver = CSharpGeneratorDriver.Create(new SharpPackGenerator()).WithUpdatedParseOptions(parse);
driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
var result = driver.GetRunResult();
if (result.Results.Length != 1 || result.Results[0].Exception is not null || result.GeneratedTrees.Length == 0)
    throw new Exception("Generator did not execute");
Console.WriteLine("Packaged generator loaded and executed");
var output = result.Results.SelectMany(r => r.GeneratedSources).OrderBy(s => s.HintName)
    .Select(s => s.HintName + "\n" + s.SourceText.ToString().Replace("\r\n", "\n"));
File.WriteAllText(args[3], string.Join("\n", output));
var errors = diagnostics.Concat(updated.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(d => d.ToString())));
using var stream = new MemoryStream();
if (!updated.Emit(stream).Success) throw new Exception("Generated fixture emit failed");
Console.WriteLine("Isolated Roslyn host passed");
