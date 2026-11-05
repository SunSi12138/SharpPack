using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPack;
using SharpPack.Generator;
using System.Reflection;
using System.Text;

// Build-time semantic inventory only. No reflection or manifest is shipped in Core.
static class GeneratedCodeDependencyBaseline
{
    public static string Create(string root)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14,
            preprocessorSymbols: ["NET10_0_OR_GREATER"]);
        var map = (IReadOnlyDictionary<string, string>)(typeof(ReferenceSymbols.WellKnownTypes)
            .GetField("knownGenericTypes", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) ?? throw new InvalidOperationException("Cannot enumerate generator formatter map."));
        var fixture = new StringBuilder(File.ReadAllText(Path.Combine(root,
            "tools/SharpPack.Baselines/Fixtures/AbiModels.cs.txt")));
        fixture.AppendLine("[SharpPackable] public partial class FormatterMap {");
        var index = 0;
        foreach (var key in map.Keys.Order(StringComparer.Ordinal))
        {
            var start = key.IndexOf('<');
            var arity = key.Count(c => c == ',') + 1;
            var arguments = Enumerable.Repeat("BaselineFixtures.Simple", arity).ToArray();
            if (key.StartsWith("System.ValueTuple<", StringComparison.Ordinal) && arity == 8)
                arguments[7] = "System.ValueTuple<BaselineFixtures.Simple>";
            var type = key[..start] + "<" + string.Join(",", arguments) + ">";
            fixture.AppendLine($"public {type} Item{index++} {{ get; set; }} = default!;");
        }
        fixture.AppendLine("}");
        for (var arity = 1; arity <= 15; arity++)
        {
            foreach (var nullable in new[] { false, true })
            foreach (var prefix in new[] { false, true })
            {
                fixture.AppendLine($"[SharpPackable] public partial class Batch{arity}_{nullable}_{prefix} {{");
                if (prefix) fixture.AppendLine("public string? Prefix { get; set; }");
                for (var i = 0; i < arity; i++)
                    fixture.AppendLine($"public int{(nullable ? "?" : "")} Item{i} {{ get; set; }}");
                fixture.AppendLine("}");
            }
        }
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Append(typeof(SharpPackableAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var input = CSharpCompilation.Create("SharpPack.AbiInventory",
            [CSharpSyntaxTree.ParseText(GeneratedSourceBaseline.FixtureSource, parse, "RepresentativeModels.cs"),
             CSharpSyntaxTree.ParseText(fixture.ToString(), parse, "AbiModels.cs")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SharpPackGenerator()).WithUpdatedParseOptions(parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var compilation, out var diagnostics);
        AssertCompiles(compilation, diagnostics);
        var trees = driver.GetRunResult().GeneratedTrees;
        var dependencies = Collect(compilation, trees);

        // Prove the fixture reaches every formatter-map constructor, including all
        // tuple arities. A map extension cannot silently escape the inventory.
        foreach (var pair in map)
        {
            var arity = pair.Key.Count(c => c == ',') + 1;
            var type = pair.Value.Replace("global::", "", StringComparison.Ordinal)
                .Replace("<TREPLACE>", "`" + arity, StringComparison.Ordinal);
            Require("M:" + type + ".#ctor");
        }
        foreach (var formatter in new[] { "UnmanagedFormatter`1", "ArrayFormatter`1",
            "TwoDimensionalArrayFormatter`1", "ThreeDimensionalArrayFormatter`1", "FourDimensionalArrayFormatter`1",
            "NullableFormatter`1", "SharpPackableFormatter`1", "GenericCollectionFormatter`2",
            "GenericSetFormatter`2", "GenericDictionaryFormatter`3" })
            Require("M:SharpPack.Formatters." + formatter + ".#ctor");
        Require("M:SharpPack.SharpPackWriter`1.GetUnmanageArrayWriteLength``1(``0[])");
        foreach (var family in new[] { "ReadUnmanaged", "DangerousReadUnmanaged",
            "WriteUnmanaged", "DangerousWriteUnmanaged", "WriteUnmanagedWithObjectHeader", "DangerousWriteUnmanagedWithObjectHeader" })
        {
            for (var arity = 1; arity <= 15; arity++)
            {
                var prefix = "M:SharpPack.SharpPack" + (family.Contains("Read", StringComparison.Ordinal)
                    ? "Reader." : "Writer`1.") + family + "``" + arity + "(";
                if (!dependencies.Keys.Any(id => id.StartsWith(prefix, StringComparison.Ordinal)))
                    throw new InvalidOperationException("ABI fixture misses overload family: " + prefix);
            }
        }

        // Characterize the actual semantic collector: an injected, valid generated
        // Serialize call must add an unapproved member absent from the clean set.
        var mutation = CSharpSyntaxTree.ParseText("""
            internal static class UnexpectedGeneratedDependency
            {
                internal static byte[] Make() => global::SharpPack.SharpPackSerializer.Serialize<int>(42);
            }
            """, parse, "UnexpectedGeneratedDependency.g.cs");
        var mutated = compilation.AddSyntaxTrees(mutation);
        AssertCompiles(mutated, []);
        var changed = Collect(mutated, trees.Append(mutation));
        if (!changed.Keys.Except(dependencies.Keys, StringComparer.Ordinal)
            .Any(id => id.StartsWith("M:SharpPack.SharpPackSerializer.Serialize", StringComparison.Ordinal)))
            throw new InvalidOperationException("Dependency gate failed its added-call mutation check.");

        var output = new StringBuilder();
        output.AppendLine("# Generated-code runtime dependency inventory");
        output.AppendLine("# Exact Roslyn documentation IDs, normalized to original generic definitions.");
        output.AppendLine("# Each member is owned by Core; source selection and coverage are owned by Generator.");
        output.AppendLine("# user = supported low-level API; legacy-generated = retained 1.x generated ABI;");
        output.AppendLine("# compiler-services = generated-only ABI. Internal implementation types are forbidden.");
        output.AppendLine($"# Coverage: {map.Count} formatter-map entries; unmanaged arities 1..15; representative and extended fixtures.");
        foreach (var pair in dependencies.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.AppendLine(pair.Value + " | " + pair.Key);
        return output.ToString();

        void Require(string id)
        {
            if (!dependencies.ContainsKey(id))
                throw new InvalidOperationException("ABI fixture misses dependency: " + id);
        }
    }

    static SortedDictionary<string, string> Collect(Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
            {
                if (node is ExpressionSyntax or AttributeSyntax or ConstructorInitializerSyntax)
                {
                    Add(model.GetSymbolInfo(node).Symbol);
                    if (node is ExpressionSyntax expression)
                        Add(model.GetTypeInfo(expression).Type);
                }
                if (node is BaseMethodDeclarationSyntax method && model.GetDeclaredSymbol(method) is IMethodSymbol declared)
                {
                    Add(declared.OverriddenMethod);
                    foreach (var implemented in declared.ExplicitInterfaceImplementations) Add(implemented);
                }
                if (node is PropertyDeclarationSyntax property && model.GetDeclaredSymbol(property) is IPropertySymbol declaredProperty)
                {
                    Add(declaredProperty.OverriddenProperty);
                    foreach (var implemented in declaredProperty.ExplicitInterfaceImplementations) Add(implemented);
                }
                if (node is TypeDeclarationSyntax type && model.GetDeclaredSymbol(type) is INamedTypeSymbol declaredType)
                {
                    // Generated nested formatters implicitly call the protected
                    // parameterless runtime-base constructor.
                    if (declaredType.BaseType is { } parent && parent.ContainingAssembly?.Name == "SharpPack.Core")
                        foreach (var ctor in parent.InstanceConstructors.Where(x => x.Parameters.Length == 0)) Add(ctor);
                }
            }
        }
        return result;

        void Add(ISymbol? symbol)
        {
            if (symbol is IAliasSymbol alias) symbol = alias.Target;
            if (symbol is IArrayTypeSymbol array) { Add(array.ElementType); return; }
            if (symbol is INamedTypeSymbol named)
                foreach (var argument in named.TypeArguments.Where(x => x is not ITypeParameterSymbol)) Add(argument);
            if (symbol?.ContainingAssembly?.Name != "SharpPack.Core") return;
            symbol = symbol.OriginalDefinition;
            if (symbol is not (INamedTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)) return;
            var id = symbol.GetDocumentationCommentId()
                ?? throw new InvalidOperationException("Cannot identify dependency: " + symbol);
            if (result.ContainsKey(id)) return;
            result.Add(id, Classify(symbol));
            if (symbol.ContainingType is { } container) Add(container);
        }
    }

    static string Classify(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        var ns = type.ContainingNamespace.ToDisplayString();
        if (ns == "SharpPack.CompilerServices") return "compiler-services";
        if (ns == "SharpPack.Internal")
            throw new InvalidOperationException("Generated code leaks an Internal dependency: " + symbol);
        if (ns == "SharpPack.Formatters" && type.MetadataName == "ListFormatter") return "legacy-generated";
        if (ns is "SharpPack.Formatters" or "SharpPack.Compression") return "user";
        if (ns == "SharpPack")
        {
            if (type.Name is "ISharpPackContextFormatterFactory" or "ISharpPackContextOverrideFormatter" or
                "ISharpPackExactSizeSerializable" or "IFixedSizeSharpPackable")
                return "legacy-generated";
            if (symbol.Name is "WriteUtf8Exact" or "HasFormatterOverrideDependency" or "HasFormatterOverrides" ||
                (type.Name == "SharpPackSerializationException" && symbol.Name.StartsWith("Throw", StringComparison.Ordinal)))
                return "legacy-generated";
            if (type.Name is "ISharpPackable" or "ISharpPackFormatter" or "ISharpPackFormatterFactory" or "SharpPackFormatter" or
                "SharpPackReader" or "SharpPackWriter" or "SharpPackReaderOptionalState" or
                "SharpPackWriterOptionalState" or "SharpPackSerializerContext" or "SharpPackSerializerContextBuilder" or
                "SharpPackSerializationException" or "SharpPackSerializer" or "SharpPackCode" or
                "OrdinalIgnoreCaseStringDictionaryFormatter" ||
                type.Name.EndsWith("FormatterAttribute", StringComparison.Ordinal))
                return "user";
        }
        throw new InvalidOperationException("Unclassified generated runtime dependency: " + symbol);
    }

    static void AssertCompiles(Compilation compilation, IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Concat(compilation.GetDiagnostics())
            .Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("ABI fixtures failed to compile:\n" + string.Join("\n", errors.Select(x => x.ToString())));
    }
}
