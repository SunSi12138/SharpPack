using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace SharpPack.Generator;

// dotnet/runtime generators.

// https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.RegularExpressions/gen/
// https://github.com/dotnet/runtime/tree/main/src/libraries/System.Text.Json/gen
// https://github.com/dotnet/runtime/tree/main/src/libraries/System.Private.CoreLib/gen
// https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.Logging.Abstractions/gen
// https://github.com/dotnet/runtime/tree/main/src/libraries/System.Runtime.InteropServices.JavaScript/gen/JSImportGenerator
// https://github.com/dotnet/runtime/tree/main/src/libraries/System.Runtime.InteropServices/gen/LibraryImportGenerator
// https://github.com/dotnet/runtime/tree/main/src/tests/Common/XUnitWrapperGenerator

// documents, blogs.

// https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md
// https://andrewlock.net/creating-a-source-generator-part-1-creating-an-incremental-source-generator/
// https://qiita.com/WiZLite/items/48f37278cf13be899e40
// https://zenn.dev/pcysl5edgo/articles/6d9be0dd99c008
// https://neue.cc/2021/05/08_600.html
// https://www.thinktecture.com/en/net/roslyn-source-generators-introduction/

// for check generated file
// <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
// <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>

[Generator(LanguageNames.CSharp)]
public partial class SharpPackGenerator : IIncrementalGenerator
{
    public const string SharpPackableAttributeFullName = "SharpPack.SharpPackableAttribute";
    public const string SharpPackUnionFormatterAttributeFullName = "SharpPack.SharpPackUnionFormatterAttribute";
    public const string GenerateTypeScriptAttributeFullName = "SharpPack.GenerateTypeScriptAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var unsupportedAbi = context.CompilationProvider
            .Select(static (compilation, _) => RuntimeGeneratedCodeAbi.GetUnsupportedVersion(compilation))
            .WithTrackingName("SharpPack.RuntimeAbi");

        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                SharpPackableAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax,
                static (context, _) => (TypeDeclarationSyntax)context.TargetNode)
            .WithTrackingName("SharpPack.SharpPackable.1_ForAttributeSharpPackableAttribute");
        var unions = context.SyntaxProvider.ForAttributeWithMetadataName(
                SharpPackUnionFormatterAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (context, _) => (TypeDeclarationSyntax)context.TargetNode)
            .WithTrackingName("SharpPack.SharpPackable.1_ForAttributeSharpPackUnion");
        var typeScript = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateTypeScriptAttributeFullName,
            static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax,
            static (context, _) => (TypeDeclarationSyntax)context.TargetNode);

        ReportRuntimeAbiMismatch(context, models, unions, typeScript, unsupportedAbi);
        RegisterSharpPackable(context, CompatibleDeclarations(models, unsupportedAbi), CompatibleDeclarations(unions, unsupportedAbi));
        RegisterTypeScript(context, CompatibleDeclarations(typeScript, unsupportedAbi));
    }

    static IncrementalValuesProvider<TypeDeclarationSyntax> CompatibleDeclarations(
        IncrementalValuesProvider<TypeDeclarationSyntax> declarations, IncrementalValueProvider<string?> unsupportedAbi)
        => declarations.Combine(unsupportedAbi)
            .Where(static pair => pair.Right is null)
            .Select(static (pair, _) => pair.Left);

    static void ReportRuntimeAbiMismatch(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TypeDeclarationSyntax> models,
        IncrementalValuesProvider<TypeDeclarationSyntax> unions,
        IncrementalValuesProvider<TypeDeclarationSyntax> typeScript,
        IncrementalValueProvider<string?> unsupportedAbi)
    {
        IncrementalValuesProvider<(TypeDeclarationSyntax Left, string? Right)> Incompatible(
            IncrementalValuesProvider<TypeDeclarationSyntax> declarations)
            => declarations.Combine(unsupportedAbi).Where(static pair => pair.Right is not null);

        var uses = Incompatible(models).Collect().Combine(Incompatible(unions).Collect())
            .Combine(Incompatible(typeScript).Collect());
        context.RegisterSourceOutput(uses, static (output, declarations) =>
        {
            var first = declarations.Left.Left.Concat(declarations.Left.Right).Concat(declarations.Right)
                .OrderBy(static item => item.Left.SyntaxTree.FilePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Left.SpanStart)
                .FirstOrDefault();
            if (first.Left is not null)
            {
                output.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.IncompatibleRuntimeAbi,
                    first.Left.Identifier.GetLocation(),
                    first.Right,
                    RuntimeGeneratedCodeAbi.MinimumVersion,
                    RuntimeGeneratedCodeAbi.MaximumVersion));
            }
        });
    }

    void RegisterSharpPackable(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TypeDeclarationSyntax> models,
        IncrementalValuesProvider<TypeDeclarationSyntax> unions)
    {
        // return dir of info output or null .
        var logProvider = context.AnalyzerConfigOptionsProvider
            .Select((configOptions, token) =>
            {
                if (configOptions.GlobalOptions.TryGetValue("build_property.SharpPackGenerator_SerializationInfoOutputDirectory", out var path))
                {
                    return path;
                }

                return (string?)null;
            })
            .WithTrackingName("SharpPack.SharpPackable.0_AnalyzerConfigOptionsProvider"); // annotate for IncrementalGeneratorTest

        var parseOptions = context.ParseOptionsProvider
            .Select((parseOptions, token) =>
            {
                var csOptions = (CSharpParseOptions)parseOptions;
                var langVersion = csOptions.LanguageVersion;
                return langVersion;
            })
            .WithTrackingName("SharpPack.SharpPackable.0_ParseOptionsProvider");

        {
            var source = models
                .Combine(context.CompilationProvider)
                .Combine(logProvider)
                .Combine(parseOptions)
                .WithTrackingName("SharpPack.SharpPackable.2_SharpPackableCombined");

            context.RegisterSourceOutput(source, static (context, source) =>
            {
                var (typeDeclaration, compilation) = source.Left.Item1;
                var logPath = source.Left.Item2;
                var langVersion = source.Right;

                Generate(typeDeclaration, compilation, logPath, new GeneratorContext(context, langVersion));
            });
        }
        {
            var source = unions
                .Combine(context.CompilationProvider)
                .Combine(logProvider)
                .Combine(parseOptions)
                .WithTrackingName("SharpPack.SharpPackable.2_SharpPackUnionCombined");

            context.RegisterSourceOutput(source, static (context, source) =>
            {
                var (typeDeclaration, compilation) = source.Left.Item1;
                var logPath = source.Left.Item2;
                var langVersion = source.Right;

                Generate(typeDeclaration, compilation, logPath, new GeneratorContext(context, langVersion));
            });
        }
        {
            var source = unions
                .Collect()
                .Combine(context.CompilationProvider)
                .Combine(parseOptions)
                .WithTrackingName(
                    "SharpPack.SharpPackable.2_ExternalUnionFactoriesCombined");

            context.RegisterSourceOutput(source, static (context, source) =>
            {
                var declarations = source.Left.Left;
                var compilation = source.Left.Right;
                var langVersion = source.Right;

                GenerateExternalUnionTargetFactories(
                    declarations,
                    compilation,
                    new GeneratorContext(context, langVersion));
            });
        }
    }

    void RegisterTypeScript(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TypeDeclarationSyntax> typeScriptDeclarations)
    {
        var typeScriptEnabled = context.AnalyzerConfigOptionsProvider
            .Select((configOptions, token) =>
            {
                // https://github.com/dotnet/project-system/blob/main/docs/design-time-builds.md
                var isDesignTimeBuild = configOptions.GlobalOptions.TryGetValue("build_property.DesignTimeBuild", out var designTimeBuild) && designTimeBuild == "true";

                string? path;
                if (!configOptions.GlobalOptions.TryGetValue("build_property.SharpPackGenerator_TypeScriptOutputDirectory", out path))
                {
                    path = null;
                }
                string ext;
                if (!configOptions.GlobalOptions.TryGetValue("build_property.SharpPackGenerator_TypeScriptImportExtension", out ext!))
                {
                    ext = ".js";
                }

                string convertProp;
                if (!configOptions.GlobalOptions.TryGetValue("build_property.SharpPackGenerator_TypeScriptConvertPropertyName", out convertProp!))
                {
                    convertProp = "true";
                }

                if (!configOptions.GlobalOptions.TryGetValue("build_property.SharpPackGenerator_TypeScriptEnableNullableTypes", out var enableNullableTypes))
                {
                    enableNullableTypes = "false";
                }

                if (!bool.TryParse(convertProp, out var convert)) convert = true;

                if (path == null) return null;

                return new TypeScriptGenerateOptions
                {
                    OutputDirectory = path,
                    ImportExtension = ext,
                    ConvertPropertyName = convert,
                    EnableNullableTypes = bool.TryParse(enableNullableTypes, out var enabledNullableTypesParsed) && enabledNullableTypesParsed,
                    IsDesignTimeBuild = isDesignTimeBuild
                };
            });

        var typeScriptGenerateSource = typeScriptDeclarations
            .Combine(context.CompilationProvider)
            .Combine(typeScriptEnabled)
            .Where(x => x.Right != null) // filter
            .Collect();

        context.RegisterSourceOutput(typeScriptGenerateSource, static (context, source) =>
        {
            ReferenceSymbols? reference = null;
            string? generatePath = null;

            var unionMap = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default); // <impl, base>
            foreach (var item in source)
            {
                var tsOptions = item.Right;
                if (tsOptions == null) continue;
                if (tsOptions.IsDesignTimeBuild) continue; // designtime build(in IDE), do nothing.

                var syntax = item.Left.Item1;
                var compilation = item.Left.Item2;
                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var typeSymbol = semanticModel.GetDeclaredSymbol(syntax, context.CancellationToken) as ITypeSymbol;
                if (typeSymbol == null) continue;
                if (reference == null)
                {
                    reference = new ReferenceSymbols(compilation);
                }

                if (generatePath is null && item.Right is { } options)
                {
                    generatePath = options.OutputDirectory;
                }

                var isUnion = typeSymbol.ContainsAttribute(reference.SharpPackUnionAttribute);

                if (isUnion)
                {
                    var unionTags = typeSymbol.GetAttributes()
                        .Where(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, reference.SharpPackUnionAttribute))
                        .Where(x => x.ConstructorArguments.Length == 2)
                        .Select(x => (INamedTypeSymbol)x.ConstructorArguments[1].Value!);
                    foreach (var implType in unionTags)
                    {
                        unionMap[implType] = typeSymbol;
                    }
                }
            }

            if (generatePath != null)
            {
                var collector = new TypeCollector();
                foreach (var item in source)
                {
                    var typeDeclaration = item.Left.Item1;
                    var compilation = item.Left.Item2;

                    if (reference == null)
                    {
                        reference = new ReferenceSymbols(compilation);
                    }

                    var meta = GenerateTypeScript(typeDeclaration, compilation, item.Right!, context, reference, unionMap);
                    if (meta != null)
                    {
                        collector.Visit(meta, false);
                    }
                }

                GenerateEnums(collector.GetEnums(), generatePath);

                // generate runtime
                var runtime = new[]{
                    ("SharpPackWriter.ts", TypeScriptRuntime.SharpPackWriter),
                    ("SharpPackReader.ts", TypeScriptRuntime.SharpPackReader),
                };

                foreach (var item in runtime)
                {
                    var filePath = Path.Combine(generatePath, item.Item1);
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, item.Item2, new UTF8Encoding(false));
                    }
                }
            }
        });
    }

    class GeneratorContext : IGeneratorContext
    {
        SourceProductionContext context;

        public GeneratorContext(SourceProductionContext context, LanguageVersion languageVersion)
        {
            this.context = context;
            this.LanguageVersion = languageVersion;
        }

        public CancellationToken CancellationToken => context.CancellationToken;

        public Microsoft.CodeAnalysis.CSharp.LanguageVersion LanguageVersion { get; }

        public void AddSource(string hintName, string source)
        {
            context.AddSource(hintName, source);
        }

        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
