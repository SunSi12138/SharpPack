using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPack.Generator;

internal static class NativeUnionAnalysis
{
    // Keep the generator loadable on Roslyn 5.0 without referencing the newer syntax kind.
    internal static bool IsDeclaration(TypeDeclarationSyntax syntax) => syntax.Keyword.ValueText == "union";

    internal static bool IsSourceUnion(INamedTypeSymbol symbol)
        => symbol.TypeKind == TypeKind.Struct && symbol.DeclaringSyntaxReferences.Any(static reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration && IsDeclaration(declaration));

    internal static IEnumerable<(ushort Tag, ITypeSymbol Type)> GetCases(INamedTypeSymbol symbol, ReferenceSymbols references)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, references.SharpPackUnionAttribute) &&
                attribute.ConstructorArguments is { Length: 2 } arguments &&
                arguments[0].Value is ushort tag && arguments[1].Value is ITypeSymbol type)
                yield return (tag, type);
        }
    }

    internal static TypeShape? Analyze(TypeDeclarationSyntax syntax, INamedTypeSymbol symbol, ReferenceSymbols references,
        CancellationToken cancellationToken, Action<Diagnostic> report)
    {
        symbol.TryGetSharpPackableType(references, out var mode, out var layout);
        if (mode == GenerateType.NoGenerate) return null;
        if (mode is not (GenerateType.Object or GenerateType.Union) || layout != SerializeLayout.Sequential)
            return Error("use [SharpPackable] with one [SharpPackUnion(tag, typeof(Case))] per case");
        var nativeTypes = new HashSet<ITypeSymbol>(symbol.InstanceConstructors
            .Where(static constructor => constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 1)
            .Select(static constructor => constructor.Parameters[0].Type), SymbolEqualityComparer.Default);
        var mappings = GetCases(symbol, references).OrderBy(static entry => entry.Tag).ToArray();
        var tags = new HashSet<ushort>();
        var runtimeTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var cases = new List<UnionCaseShape>(mappings.Length);
        var compilation = references.Compilation;
        var graph = new SidecarGraph(compilation, [], accessibleWithin: symbol);
        var defaultFactory = compilation.GetTypeByMetadataName("SharpPack.ISharpPackFormatterFactory`1");
        var contextFactory = compilation.GetTypeByMetadataName("SharpPack.ISharpPackContextFormatterFactory`1");
        var factories = new List<AotFactoryRoot>();
        var formatters = new List<string>();
        foreach (var (tag, caseType) in mappings)
        {
            if (!tags.Add(tag)) return Error($"wire tag {tag} is duplicated");
            if (!nativeTypes.Remove(caseType))
                return Error($"'{caseType}' is not an untagged declared case");

            var boxed = BoxedType(caseType);
            if (boxed.TypeKind == TypeKind.Interface || boxed.IsAbstract)
                return Error($"case '{caseType}' needs a supported concrete formatter type");
            if (!runtimeTypes.Add(boxed))
                return Error($"case '{caseType}' has the same boxed type as another case");

            // Use the same type graph for validation and NativeAOT roots. A
            // native union calls the regular resolver, so it cannot use
            // formatter code that only an explicit sidecar can generate.
            var (nodes, failure) = graph.Walk(caseType, cancellationToken);
            if (failure is not null)
                return Error($"case '{caseType}' at '{failure.Path}': {failure.Reason}");
            foreach (var node in nodes)
            {
                if (node.Sidecar is not null)
                    return Error($"case dependency '{node.Type}' needs a built-in or type-owned formatter");
                if (node.BuiltInFormatter is { } formatter)
                {
                    formatters.Add(formatter);
                    // The default resolver prefers raw copies for reference-free
                    // value types; a context can select the known generic formatter.
                    if (node.Type is INamedTypeSymbol { IsGenericType: true } && BoxedType(node.Type).IsUnmanagedType)
                        formatters.Add($"global::SharpPack.Formatters.DangerousUnmanagedFormatter<{node.Type.FullyQualifiedToString()}>");
                }
                if (node.Factory == TypeOwnedFactory.Context && node.Type is INamedTypeSymbol factoryType &&
                    SidecarGraph.HasSelfInterface(factoryType, contextFactory) &&
                    !SidecarGraph.HasSelfInterface(factoryType, defaultFactory))
                    return Error($"case dependency '{node.Type}' provides a context-only factory and needs a default formatter factory");
                if (node.Factory != TypeOwnedFactory.None)
                    factories.Add(new(node.Type.FullyQualifiedToString(), node.Factory == TypeOwnedFactory.Context));
            }

            cases.Add(new(tag, caseType.FullyQualifiedToString(), UsePackablePath: false, caseType.Name, boxed.FullyQualifiedToString()));
        }
        if (nativeTypes.Count != 0 || cases.Count == 0)
            return Error("each declared case needs an explicit [SharpPackUnion(tag, typeof(Case))] tag");

        var qualifiedName = symbol.FullyQualifiedToString();
        return new TypeShape(qualifiedName, qualifiedName, symbol.TypeKind, symbol.TypeKind,
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), symbol.Name, GenerateType.Union,
            layout, IsValueType: true, IsUnmanaged: false, IsRecord: false,
            Members: default, ConstructorParameters: default, Callbacks: default,
            ContainingTypes: PartialTypeShape.CaptureContainers(symbol),
            Union: new(qualifiedName, new EquatableArray<UnionCaseShape>(cases), ExternalFormatter: null, IsNative: true),
            Collection: null,
            AotDependencies: new(new EquatableArray<AotFactoryRoot>(factories), new EquatableArray<string>(formatters)),
            CircularRead: null,
            NameFacts: GeneratedNameFacts.CaptureForNativeUnion(symbol));

        TypeShape? Error(string reason)
        {
            report(Diagnostic.Create(DiagnosticDescriptors.UnsupportedNativeUnion, syntax.Identifier.GetLocation(),
                symbol.Name, reason));
            return null;
        }
    }

    static ITypeSymbol BoxedType(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0] : type;
}
