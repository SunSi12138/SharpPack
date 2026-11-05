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
    sealed record Dependency(string Category, string ScopeContract);

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
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
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

        var comment = CSharpSyntaxTree.ParseText(
            "// SharpPack.Internal.ReusableLinkedArrayBufferWriterPool is only a comment.", parse, "Comment.g.cs");
        var commented = Collect(compilation.AddSyntaxTrees(comment), trees.Append(comment));
        if (!commented.SequenceEqual(dependencies))
            throw new InvalidOperationException("A comment changed the semantic dependency inventory.");

        AssertScopedContractMutations(parse, references);

        var output = new StringBuilder();
        output.AppendLine("# Generated-code runtime dependency inventory");
        output.AppendLine("# Exact Roslyn documentation IDs, normalized to original generic definitions.");
        output.AppendLine("# Semantic scope contracts include effective parameter ScopedKind and UnscopedRef/receiver semantics.");
        output.AppendLine("# Each member is owned by Core; source selection and coverage are owned by Generator.");
        output.AppendLine("# user = supported low-level API; legacy-generated = retained 1.x generated ABI;");
        output.AppendLine("# compiler-services = generated-only ABI. Internal implementation types are forbidden.");
        output.AppendLine($"# Coverage: {map.Count} formatter-map entries; unmanaged arities 1..15; representative and extended fixtures.");
        foreach (var pair in dependencies.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.AppendLine(pair.Value.Category + " | " + pair.Key + " | " + pair.Value.ScopeContract);
        return output.ToString();

        void Require(string id)
        {
            if (!dependencies.ContainsKey(id))
                throw new InvalidOperationException("ABI fixture misses dependency: " + id);
        }
    }

    static SortedDictionary<string, Dependency> Collect(Compilation compilation, IEnumerable<SyntaxTree> trees)
    {
        var result = new SortedDictionary<string, Dependency>(StringComparer.Ordinal);
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
            result.Add(id, new Dependency(Classify(symbol), ScopeContract(symbol)));
            if (symbol.ContainingType is { } container) Add(container);
        }
    }

    // Documentation IDs deliberately remain the member identity. They do not
    // distinguish scoped ref/in/value or UnscopedRef, so keep a readable semantic
    // fingerprint alongside each ID. Inspect original definitions, not a generic
    // substitution or the spelling of an individual call site.
    static string ScopeContract(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => MethodScopeContract(method),
        IPropertySymbol property => $"parameters=[{Parameters(property.Parameters)}];return={property.RefKind};" +
            $"unscoped-ref={HasUnscopedRef(property)};get=({Accessor(property.GetMethod)});set=({Accessor(property.SetMethod)})",
        IEventSymbol @event => $"add=({Accessor(@event.AddMethod)});remove=({Accessor(@event.RemoveMethod)})",
        INamedTypeSymbol type => $"type={type.TypeKind};ref-like={type.IsRefLikeType};readonly={type.IsReadOnly}",
        IFieldSymbol field => $"ref={field.RefKind};readonly={field.IsReadOnly}",
        _ => throw new InvalidOperationException("Unsupported scoped dependency: " + symbol)
    };

    static string MethodScopeContract(IMethodSymbol method)
    {
        // Roslyn exposes effective parameter scope, but not the implicit `this`
        // parameter. Record its semantic inputs without guessing a receiver scope:
        // static/reference/value receiver, readonly, ref-like, and UnscopedRef.
        // A property-level UnscopedRef may not appear on its accessor's attributes.
        var receiver = method.IsStatic ? "none" : method.ContainingType.IsValueType ? "value" : "reference";
        return $"parameters=[{Parameters(method.Parameters)}];return={method.RefKind};" +
            $"receiver={receiver};readonly={method.IsReadOnly};" +
            $"unscoped-ref={HasUnscopedRef(method) || (method.AssociatedSymbol is { } associated && HasUnscopedRef(associated))}";
    }

    static string Accessor(IMethodSymbol? method) => method is null ? "none" : MethodScopeContract(method);

    static string Parameters(IEnumerable<IParameterSymbol> parameters) => string.Join(",", parameters.Select(
        parameter => $"{parameter.Ordinal}:{parameter.RefKind}/{parameter.ScopedKind}/unscoped-ref={HasUnscopedRef(parameter)}"));

    static bool HasUnscopedRef(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
        attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute");

    static void AssertScopedContractMutations(CSharpParseOptions parse, IEnumerable<PortableExecutableReference> references)
    {
        // Use a tiny replacement Core, keeping the consumer fixed and valid. This
        // tests the actual collector without mutating the checkout or requiring
        // all generated models to survive a deliberately incompatible contract.
        // Round-trip through an emitted PE too: the real inventory reads Core as
        // metadata, and a source-only test would not guard attribute import.
        var platformReferences = references.Where(reference => !StringComparer.OrdinalIgnoreCase.Equals(
            reference.FilePath, typeof(SharpPackableAttribute).Assembly.Location)).ToArray();
        const string runtime = """
            namespace SharpPack
            {
                public ref struct SharpPackReader
                {
                    public void ReadValue(scoped ref int value) { }
                    public void ReadIn(scoped in int value) { }
                    public void ReadSpan(scoped System.Span<int> values) { }
                    public void ReadOut(out int value) { value = 0; }
                    public ref int ReadReceiver() => ref Holder.Value;
                    public readonly void ReadReadOnly() { }
                    public ref int Value => ref Holder.Value;
                    public int this[scoped System.Span<int> values] => 0;
                }
                internal static class Holder { internal static int Value; }
            }
            """;
        var consumer = CSharpSyntaxTree.ParseText("""
            internal static class ScopeConsumer
            {
                internal static void Read(ref int value, System.Span<int> values)
                {
                    global::SharpPack.SharpPackReader reader = default;
                    reader.ReadValue(ref value);
                    reader.ReadIn(in value);
                    reader.ReadSpan(values);
                    reader.ReadOut(out _);
                    reader.ReadReceiver();
                    reader.ReadReadOnly();
                    _ = reader.Value;
                    _ = reader[values];
                }
            }
            """, parse, "ScopeConsumer.g.cs");
        var baseline = CollectRuntime(runtime);
        const string unscopedRef = "[System.Diagnostics.CodeAnalysis.UnscopedRef] ";
        AssertChanged("scoped ref", "scoped ref int value", "ref int value",
            "M:SharpPack.SharpPackReader.ReadValue(System.Int32@)");
        AssertChanged("scoped in", "scoped in int value", "in int value",
            "M:SharpPack.SharpPackReader.ReadIn(System.Int32@)");
        AssertChanged("scoped value", "ReadSpan(scoped System.Span<int> values)", "ReadSpan(System.Span<int> values)",
            "M:SharpPack.SharpPackReader.ReadSpan(System.Span{System.Int32})");
        AssertChanged("UnscopedRef out", "ReadOut(out int value)", "ReadOut(" + unscopedRef + "out int value)",
            "M:SharpPack.SharpPackReader.ReadOut(System.Int32@)");
        AssertChanged("UnscopedRef receiver", "public ref int ReadReceiver()", unscopedRef + "public ref int ReadReceiver()",
            "M:SharpPack.SharpPackReader.ReadReceiver");
        AssertChanged("readonly receiver", "public readonly void ReadReadOnly()", "public void ReadReadOnly()",
            "M:SharpPack.SharpPackReader.ReadReadOnly");
        AssertChanged("UnscopedRef property receiver", "public ref int Value", unscopedRef + "public ref int Value",
            "P:SharpPack.SharpPackReader.Value");
        AssertChanged("scoped indexer value", "this[scoped System.Span<int> values]", "this[System.Span<int> values]",
            "P:SharpPack.SharpPackReader.Item(System.Span{System.Int32})");
        AssertUnchanged("implicit out scope", ReplaceOnce(runtime, "ReadOut(out int value)", "ReadOut(scoped out int value)"));
        AssertUnchanged("parameter spelling", ReplaceOnce(runtime, "scoped ref int value", "scoped ref int renamed"));
        AssertUnchanged("comment", runtime + "\n// scoped ref and UnscopedRef are not dependencies.\n");

        SortedDictionary<string, Dependency> CollectRuntime(string source)
        {
            var runtimeTree = CSharpSyntaxTree.ParseText(source, parse, "ScopeRuntime.cs");
            var core = CSharpCompilation.Create("SharpPack.Core", [runtimeTree], platformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var sourceCompilation = core.AddSyntaxTrees(consumer);
            AssertCompiles(sourceCompilation, []);
            var fromSource = Collect(sourceCompilation, [consumer]);
            using var image = new MemoryStream();
            var emitted = core.Emit(image);
            if (!emitted.Success)
                throw new InvalidOperationException("Scoped dependency fixture failed to emit:\n" +
                    string.Join("\n", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var metadataCompilation = CSharpCompilation.Create("ScopedDependencyConsumer", [consumer],
                platformReferences.Append(MetadataReference.CreateFromImage(image.ToArray())),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            AssertCompiles(metadataCompilation, []);
            var fromMetadata = Collect(metadataCompilation, [consumer]);
            if (!fromSource.SequenceEqual(fromMetadata))
            {
                var differences = fromSource.Keys.Union(fromMetadata.Keys, StringComparer.Ordinal)
                    .Where(id => fromSource.GetValueOrDefault(id) != fromMetadata.GetValueOrDefault(id))
                    .Select(id => $"{id}\n  source: {fromSource.GetValueOrDefault(id)}\n  metadata: {fromMetadata.GetValueOrDefault(id)}");
                throw new InvalidOperationException("Scoped dependency contracts differ between source and emitted metadata:\n" +
                    string.Join("\n", differences));
            }
            return fromMetadata;
        }

        void AssertChanged(string label, string oldText, string newText, string memberId)
        {
            var changed = CollectRuntime(ReplaceOnce(runtime, oldText, newText));
            if (!baseline.Keys.SequenceEqual(changed.Keys) ||
                !baseline.Select(pair => pair.Value.Category).SequenceEqual(changed.Select(pair => pair.Value.Category)))
                throw new InvalidOperationException("Scoped mutation changed dependency identities or categories: " + label);
            var changedMembers = changed.Where(pair => pair.Value != baseline[pair.Key]).Select(pair => pair.Key).ToArray();
            if (!changedMembers.SequenceEqual(new[] { memberId }))
                throw new InvalidOperationException("Dependency gate failed its same-member " + label + " mutation check: " +
                    string.Join(", ", changedMembers));
        }

        void AssertUnchanged(string label, string source)
        {
            if (!baseline.SequenceEqual(CollectRuntime(source)))
                throw new InvalidOperationException("Equivalent scope changed the dependency inventory: " + label);
        }

        static string ReplaceOnce(string source, string oldText, string newText)
        {
            var offset = source.IndexOf(oldText, StringComparison.Ordinal);
            if (offset < 0 || source.IndexOf(oldText, offset + oldText.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Scoped dependency mutation must match exactly once: " + oldText);
            return source[..offset] + newText + source[(offset + oldText.Length)..];
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
