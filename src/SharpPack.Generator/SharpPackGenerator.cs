using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
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
                static (context, _) => (Declaration: (TypeDeclarationSyntax)context.TargetNode,
                    HasGenerationIntent: HasModelGenerationIntent(context)));
        var unions = context.SyntaxProvider.ForAttributeWithMetadataName(
                SharpPackUnionFormatterAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (context, _) => (TypeDeclarationSyntax)context.TargetNode);
        var typeScript = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateTypeScriptAttributeFullName,
            static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax,
            static (context, _) => (TypeDeclarationSyntax)context.TargetNode)
            .Combine(GetTypeScriptOptions(context))
            .Where(static pair => pair.Right is not null)
            .Select(static (pair, _) => (Declaration: pair.Left, Options: pair.Right!));

        ReportRuntimeAbiMismatch(
            context,
            models.Where(static model => model.HasGenerationIntent).Select(static (model, _) => model.Declaration),
            unions,
            typeScript.Select(static (model, _) => model.Declaration),
            unsupportedAbi);
        RegisterSharpPackable(context,
            CompatibleDeclarations(models.Select(static (model, _) => model.Declaration), unsupportedAbi),
            CompatibleDeclarations(unions, unsupportedAbi));
        RegisterTypeScript(context, CompatibleDeclarations(typeScript, unsupportedAbi));
    }

    static bool HasModelGenerationIntent(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes[0];
        if (!attribute.IsSharpPackableNoGenerate()) return true;

        // NoGenerate is often used for an external union contract. An actual
        // in-assembly union still emits even when its model opts out.
        var union = attribute.AttributeClass?.ContainingAssembly.GetTypeByMetadataName("SharpPack.SharpPackUnionAttribute");
        return union is not null && context.TargetSymbol.ContainsAttribute(union);
    }

    static IncrementalValuesProvider<T> CompatibleDeclarations<T>(
        IncrementalValuesProvider<T> declarations, IncrementalValueProvider<string?> unsupportedAbi)
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

    static void RegisterSharpPackable(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TypeDeclarationSyntax> models,
        IncrementalValuesProvider<TypeDeclarationSyntax> unions)
    {
        var logProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (config, _) => config.GlobalOptions.TryGetValue(
                "build_property.SharpPackGenerator_SerializationInfoOutputDirectory", out var path) ? path : null);
        var fileScopedNamespace = context.ParseOptionsProvider
            .Select(static (options, _) => ((CSharpParseOptions)options).LanguageVersion >= LanguageVersion.CSharp10);

        RegisterModelEmission(context, models, logProvider, fileScopedNamespace);
        RegisterModelEmission(context, unions, logProvider, fileScopedNamespace);

        var externalFactories = unions.Collect()
            .Combine(context.CompilationProvider)
            .Select(static (input, token) => PlanExternalUnionTargetFactories(input.Left, input.Right, token))
            .WithTrackingName("SharpPack.ExternalUnionFactoryPlans")
            .SelectMany(static (plans, _) => plans)
            .Combine(fileScopedNamespace)
            .Select(static (input, _) => CSharpEmitter.EmitExternalUnionTargetFactory(input.Left, input.Right))
            .WithTrackingName("SharpPack.ExternalUnionFactoryEmission");
        context.RegisterSourceOutput(externalFactories, AddGeneratedSource);
    }

    static void RegisterModelEmission(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TypeDeclarationSyntax> declarations,
        IncrementalValueProvider<string?> logProvider,
        IncrementalValueProvider<bool> fileScopedNamespace)
    {
        var analysis = declarations.Combine(context.CompilationProvider)
            .Select(static (input, token) => AnalyzeModel(input.Left, input.Right, token))
            .WithTrackingName("SharpPack.ModelAnalysis");
        context.RegisterSourceOutput(analysis.Where(static result => !result.Diagnostics.IsEmpty), static (output, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);
        });

        var shapes = analysis.Where(static result => result.Shape is not null)
            .Select(static (result, _) => result.Shape!)
            .WithTrackingName("SharpPack.TypeShape");
        var sources = shapes.Select(static (shape, _) => SerializationPlanner.Plan(shape))
            .WithTrackingName("SharpPack.SerializationPlan")
            .Combine(fileScopedNamespace)
            .Select(static (input, _) => new CSharpEmitter(input.Left).EmitModel(input.Right))
            .WithTrackingName("SharpPack.CSharpEmission");
        context.RegisterSourceOutput(sources, AddGeneratedSource);

        // Legacy files are side effects: allow a later compilation to retry
        // a failed write or recreate a missing file without rendering C#.
        var logFiles = shapes.Where(CSharpEmitter.HasSerializationInfo)
            .Combine(logProvider)
            .Where(static input => input.Right is not null)
            .Combine(context.CompilationProvider);
        context.RegisterSourceOutput(logFiles, static (_, input) => WriteSerializationInfo(input.Left.Left, input.Left.Right!));
    }

    static void AddGeneratedSource(SourceProductionContext output, GeneratedSource source)
        => output.AddSource(source.HintName, source.Text);

    static void WriteSerializationInfo(TypeShape shape, string outputDirectory)
    {
        try
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var file = Path.Combine(outputDirectory, $"{CSharpEmitter.ModelFileStem(shape)}.txt");
            File.WriteAllText(file, CSharpEmitter.EmitSerializationInfo(shape), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex.ToString());
        }
    }

    static IncrementalValueProvider<TypeScriptGenerateOptions?> GetTypeScriptOptions(
        IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider
            .Select(static (configOptions, _) =>
            {
                var options = configOptions.GlobalOptions;
                if (!options.TryGetValue("build_property.SharpPackGenerator_TypeScriptOutputDirectory", out var path) ||
                    (options.TryGetValue("build_property.DesignTimeBuild", out var designTimeBuild) && designTimeBuild == "true"))
                {
                    return null;
                }

                return new TypeScriptGenerateOptions
                {
                    OutputDirectory = path,
                    ImportExtension = options.TryGetValue("build_property.SharpPackGenerator_TypeScriptImportExtension", out var extension)
                        ? extension : ".js",
                    ConvertPropertyName = !options.TryGetValue("build_property.SharpPackGenerator_TypeScriptConvertPropertyName", out var convert)
                        || !bool.TryParse(convert, out var convertParsed) || convertParsed,
                    EnableNullableTypes = options.TryGetValue("build_property.SharpPackGenerator_TypeScriptEnableNullableTypes", out var nullable)
                        && bool.TryParse(nullable, out var nullableParsed) && nullableParsed,
                };
            });

    static void RegisterTypeScript(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<(TypeDeclarationSyntax Declaration, TypeScriptGenerateOptions Options)> typeScript)
    {
        var typeScriptGenerateSource = typeScript.Combine(context.CompilationProvider).Collect();

        context.RegisterSourceOutput(typeScriptGenerateSource, static (context, source) =>
        {
            ReferenceSymbols? reference = null;
            string? generatePath = null;

            var unionMap = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default); // <impl, base>
            foreach (var item in source)
            {
                var ((syntax, options), compilation) = item;
                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var typeSymbol = semanticModel.GetDeclaredSymbol(syntax, context.CancellationToken) as ITypeSymbol;
                if (typeSymbol == null) continue;
                if (reference == null)
                {
                    reference = new ReferenceSymbols(compilation);
                }

                generatePath ??= options.OutputDirectory;

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
                    var ((typeDeclaration, options), compilation) = item;

                    if (reference == null)
                    {
                        reference = new ReferenceSymbols(compilation);
                    }

                    var meta = GenerateTypeScript(typeDeclaration, compilation, options, context, reference, unionMap);
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
}
