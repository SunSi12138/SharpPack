using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

internal sealed record FormatterNode(
    ITypeSymbol Type,
    FormatterDependency[] Dependencies,
    SerializationPlan? Sidecar = null,
    string? BuiltInFormatter = null,
    UnsupportedFormatterShape? Error = null);

internal sealed record FormatterGraphFailure(string Path, string Reason);

// Nodes are analyzed by symbol identity once per compilation. Walking the
// cached edges per root provides useful paths even when a broken node is shared.
internal sealed class SidecarGraph
{
    // Prevent a recursively expanding generic shape from producing an unbounded
    // set of distinct closed types. Ordinary cycles revisit the same symbol.
    const int MaximumPathDepth = 128;

    readonly ReferenceSymbols references;
    readonly Compilation compilation;
    readonly INamedTypeSymbol? defaultFactory;
    readonly INamedTypeSymbol? contextFactory;
    readonly HashSet<ITypeSymbol> registeredTypes;
    readonly Dictionary<ITypeSymbol, FormatterNode> nodes = new(SymbolEqualityComparer.Default);

    internal SidecarGraph(Compilation compilation, IEnumerable<ITypeSymbol> registeredTypes)
    {
        this.compilation = compilation;
        references = new ReferenceSymbols(compilation);
        defaultFactory = compilation.GetTypeByMetadataName("SharpPack.ISharpPackFormatterFactory`1");
        contextFactory = compilation.GetTypeByMetadataName("SharpPack.ISharpPackContextFormatterFactory`1");
        this.registeredTypes = new HashSet<ITypeSymbol>(registeredTypes.Select(CanonicalType), SymbolEqualityComparer.Default);
    }

    internal (FormatterNode[] Nodes, FormatterGraphFailure? Error) Walk(ITypeSymbol root, CancellationToken cancellationToken)
    {
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var reached = new List<FormatterNode>();
        var pending = new Stack<(ITypeSymbol Type, DependencyPath Path)>();
        pending.Push((root, new(null, root.FullyQualifiedToString(), 0)));

        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (inputType, path) = pending.Pop();
            var type = CanonicalType(inputType);
            if (!visited.Add(type)) continue;
            if (path.Depth >= MaximumPathDepth)
                return ([], new(path.ToString(), $"the formatter dependency path exceeds {MaximumPathDepth} types"));
            if (!nodes.TryGetValue(type, out var model))
            {
                model = Analyze(type);
                nodes.Add(type, model);
            }
            if (model.Error is { } error)
                return ([], new(path + (error.Member is null ? "" : "." + error.Member), error.Reason));

            reached.Add(model);
            for (var index = model.Dependencies.Length - 1; index >= 0; index--)
            {
                var dependency = model.Dependencies[index];
                pending.Push((dependency.Type, new(path, dependency.Segment, path.Depth + 1)));
            }
        }
        return (reached.ToArray(), null);
    }

    static ITypeSymbol CanonicalType(ITypeSymbol type)
        => type is INamedTypeSymbol { TupleUnderlyingType: { } tuple } ? tuple : type;

    FormatterNode Analyze(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter ||
            type.IsRefLikeType || type.SpecialType == SpecialType.System_Void || !IsClosed(type))
        {
            return Unsupported("the type must be closed, non-ref-like, and usable as a generic argument");
        }

        if (!HasGlobalTypeName(type))
            return Unsupported("the type has no unambiguous, accessible global C# name");

        if (type is IArrayTypeSymbol array)
        {
            if (registeredTypes.Contains(type)) return Existing();
            var formatterName = references.KnownTypes.GetNonDefaultFormatterName(array);
            return formatterName is null || !array.IsSZArray && array.Rank == 1
                ? Unsupported("SharpPack supports only vectors and rectangular arrays of rank two through four")
                : Existing([new("[]", array.ElementType)], formatterName);
        }

        if (type is not INamedTypeSymbol named)
            return Unsupported("the type is not a named CLR type");
        if (registeredTypes.Contains(type)) return Existing();

        if (HasSelfInterface(named, contextFactory) || HasSelfInterface(named, defaultFactory))
            return Existing(); // A handwritten or previously compiled factory is authoritative.

        var declared = named.TryGetSharpPackableType(references, out var mode, out _);
        var isUnion = named.IsWillImplementSharpPackUnion(references);
        if (declared && (mode != GenerateType.NoGenerate || isUnion) &&
            SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly))
        {
            if (isUnion)
            {
                var cases = named.GetAttributes()
                    .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, references.SharpPackUnionAttribute))
                    .Where(static attribute => attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[1].Value is ITypeSymbol)
                    .Select(static attribute => (ITypeSymbol)attribute.ConstructorArguments[1].Value!)
                    .Select(static item => new FormatterDependency(" -> union case " + item.FullyQualifiedToString(), item));
                return Existing(cases.ToArray());
            }

            if (mode == GenerateType.Collection)
            {
                var (_, storage) = TypeMeta.ParseCollectionKind(named, references);
                return Existing(storage is null ? [] : TypeArguments(storage));
            }

            if (named.IsUnmanagedType) return Existing();
            var metadata = new TypeMeta(named, references);
            return Existing(metadata.Members.Where(static member => member.Kind != MemberKind.CustomFormatter)
                .Select(static member => new FormatterDependency("." + member.Name, member.MemberType)).ToArray());
        }

        if (HasSelfInterface(named, references.ISharpPackable.OriginalDefinition))
            return Existing(formatter: $"global::SharpPack.Formatters.SharpPackableFormatter<{named.FullyQualifiedToString()}>");
        if (references.KnownTypes.IsKnownLeaf(type))
            return Existing();

        if (named.IsGenericType && references.KnownTypes.GetExactGenericFormatter(named) is { } genericFormatter)
            return Existing(TypeArguments(named), genericFormatter);
        if (named.IsUnmanagedType || named.TypeKind == TypeKind.Enum)
            return Existing(formatter: $"global::SharpPack.Formatters.DangerousUnmanagedFormatter<{named.FullyQualifiedToString()}>");

        if (declared || isUnion)
            return Unsupported("SharpPack serialization metadata is present but the type has no verifiable formatter factory");
        if (type.SpecialType is SpecialType.System_Object or SpecialType.System_Array or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate)
            return Unsupported("the type requires a registered formatter for runtime polymorphism");

        var (plan, dependencies, error) = TypeMeta.PlanExternalObject(named, references);
        return new(type, dependencies, Sidecar: plan, Error: error);

        FormatterNode Existing(FormatterDependency[]? dependencies = null, string? formatter = null)
            => new(type, dependencies ?? [], BuiltInFormatter: formatter);
        FormatterNode Unsupported(string reason) => new(type, [], Error: new(null, reason));
    }

    static FormatterDependency[] TypeArguments(INamedTypeSymbol named)
        => named.TypeArguments.Select(static (type, index) => new FormatterDependency($" -> type argument {index + 1}", type)).ToArray();

    static bool HasSelfInterface(INamedTypeSymbol type, INamedTypeSymbol? definition)
        => definition is not null && type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition) &&
            SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));

    bool HasGlobalTypeName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return HasGlobalTypeName(array.ElementType);
        if (CanonicalType(type) is not INamedTypeSymbol named || !named.CanBeReferencedByName ||
            !compilation.IsSymbolAccessibleWithin(named, compilation.Assembly) ||
            !named.TypeArguments.All(HasGlobalTypeName))
            return false;

        var definition = named.OriginalDefinition;
        var name = definition.MetadataName;
        for (var enclosing = named.ContainingType; enclosing is not null; enclosing = enclosing.ContainingType)
        {
            if (!enclosing.TypeArguments.All(HasGlobalTypeName)) return false;
            name = enclosing.MetadataName + "+" + name;
        }
        if (!definition.ContainingNamespace.IsGlobalNamespace)
            name = definition.ContainingNamespace.ToDisplayString() + "." + name;
        return SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(name), definition);
    }

    static bool IsClosed(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => false,
        IArrayTypeSymbol array => IsClosed(array.ElementType),
        INamedTypeSymbol named => !named.IsUnboundGenericType &&
            (named.ContainingType is null || IsClosed(named.ContainingType)) && named.TypeArguments.All(IsClosed),
        _ => true,
    };

    sealed record DependencyPath(DependencyPath? Parent, string Segment, int Depth)
    {
        public override string ToString()
        {
            var segments = new Stack<string>();
            for (DependencyPath? path = this; path is not null; path = path.Parent)
                segments.Push(path.Segment);
            return string.Concat(segments);
        }
    }
}
