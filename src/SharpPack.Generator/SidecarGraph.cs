using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

internal enum TypeOwnedFactory { None, Default, Context }

internal sealed record FormatterNode(
    ITypeSymbol Type,
    FormatterDependency[] Dependencies,
    SerializationPlan? Sidecar = null,
    string? BuiltInFormatter = null,
    TypeOwnedFactory Factory = TypeOwnedFactory.None,
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
    readonly ISymbol accessibleWithin;
    readonly INamedTypeSymbol? defaultFactory;
    readonly INamedTypeSymbol? contextFactory;
    readonly HashSet<ITypeSymbol> registeredTypes;
    readonly Dictionary<ITypeSymbol, FormatterNode> nodes = new(SymbolEqualityComparer.Default);
    Dictionary<INamedTypeSymbol, List<SharpPackGenerator.ExternalUnionFactoryEntry>>? externalUnions;
    ReferenceSymbols? completeMetadata;

    internal SidecarGraph(Compilation compilation, IEnumerable<ITypeSymbol> registeredTypes,
        INamedTypeSymbol? accessibleWithin = null)
    {
        this.compilation = compilation;
        this.accessibleWithin = (ISymbol?)accessibleWithin ?? compilation.Assembly;
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
                model = Analyze(type, cancellationToken);
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

    FormatterNode Analyze(ITypeSymbol type, CancellationToken cancellationToken)
    {
        if (type.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.TypeParameter ||
            type.IsRefLikeType || type.SpecialType == SpecialType.System_Void || !IsClosed(type))
        {
            return Unsupported("the type must be closed, non-ref-like, and usable as a generic argument");
        }

        if (!type.HasGlobalTypeName(compilation, accessibleWithin, cancellationToken))
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

        // A handwritten or previously compiled factory is authoritative.
        if (HasSelfInterface(named, contextFactory))
            return Existing(factory: TypeOwnedFactory.Context);
        if (HasSelfInterface(named, defaultFactory))
            return Existing(factory: TypeOwnedFactory.Default);

        var declared = named.TryGetSharpPackableType(references, out var mode, out _);
        var isUnion = named.IsWillImplementSharpPackUnion(references);
        var sourceOwned = SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly);
        if (sourceOwned && declared && (mode != GenerateType.NoGenerate || isUnion))
        {
            if (NativeUnionAnalysis.IsSourceUnion(named))
                return Existing(NativeUnionAnalysis.GetCases(named, references).Select(static entry =>
                    new FormatterDependency(" -> union case " + entry.Type.FullyQualifiedToString(), entry.Type)).ToArray(),
                    factory: TypeOwnedFactory.Default);

            if (isUnion)
                return Existing(UnionDependencies(named, named), factory: TypeOwnedFactory.Default);

            if (mode == GenerateType.Collection)
            {
                var (_, storage) = TypeMeta.ParseCollectionKind(named, references);
                return Existing(storage is null ? [] : TypeArguments(storage), factory: TypeOwnedFactory.Default);
            }

            if (named.IsUnmanagedType) return Existing(factory: TypeOwnedFactory.Context);
            var metadata = new TypeMeta(named, references);
            return Existing(metadata.Members.Where(static member => member.Kind != MemberKind.CustomFormatter)
                .Select(static member => new FormatterDependency("." + member.Name, member.MemberType)).ToArray(),
                factory: TypeOwnedFactory.Context);
        }

        // A same-compilation external union factory has not yet been added to
        // its partial target. Use the ordinary generator's matching rules.
        if (sourceOwned && named.IsAbstract && TryGetExternalUnionEntries(named, cancellationToken, out var entries))
        {
            var formatter = SharpPackGenerator.ResolveExternalUnionFormatter(named, entries);
            return formatter is null
                ? Unsupported("no generated external union formatter matches the closed target")
                : Existing(UnionDependencies(named, formatter), factory: TypeOwnedFactory.Default);
        }

        if (HasSelfInterface(named, references.ISharpPackable.OriginalDefinition))
            return Existing(formatter: $"global::SharpPack.Formatters.SharpPackableFormatter<{named.FullyQualifiedToString()}>");
        if (references.KnownTypes.IsKnownLeaf(type))
            return Existing(formatter: $"global::SharpPack.Formatters.{named.Name}Formatter");

        if (named.IsGenericType && references.KnownTypes.GetExactGenericFormatter(named) is { } genericFormatter)
            return Existing(TypeArguments(named), genericFormatter);
        if (named.IsUnmanagedType || named.TypeKind == TypeKind.Enum)
            return Existing(formatter: $"global::SharpPack.Formatters.DangerousUnmanagedFormatter<{named.FullyQualifiedToString()}>");

        if (declared || isUnion)
            return Unsupported("SharpPack serialization metadata is present but the type has no verifiable formatter factory");
        if (type.SpecialType is SpecialType.System_Object or SpecialType.System_Array or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate)
            return Unsupported("the type requires a registered formatter for runtime polymorphism");

        if (CheckHiddenMetadata(named) is { } hidden)
            return new(type, [], Error: hidden);

        var (plan, dependencies, error) = TypeMeta.PlanExternalObject(named, references);
        return new(type, dependencies, Sidecar: plan, Error: error);

        FormatterNode Existing(FormatterDependency[]? dependencies = null, string? formatter = null,
            TypeOwnedFactory factory = TypeOwnedFactory.None)
            => new(type, dependencies ?? [], BuiltInFormatter: formatter, Factory: factory);
        FormatterNode Unsupported(string reason) => new(type, [], Error: new(null, reason));
    }

    bool TryGetExternalUnionEntries(INamedTypeSymbol target, CancellationToken token,
        out List<SharpPackGenerator.ExternalUnionFactoryEntry> entries)
    {
        // GetSymbolsWithName is source-only. This index is needed only for
        // a reachable source-owned abstract type without an existing factory.
        externalUnions ??= SharpPackGenerator.GroupExternalUnionFactories(
            compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type, token).OfType<INamedTypeSymbol>()
                .Where(static type => type.TypeKind == TypeKind.Class && !type.IsRecord), references);
        return externalUnions.TryGetValue(target.OriginalDefinition, out entries!);
    }

    FormatterDependency[] UnionDependencies(INamedTypeSymbol target, INamedTypeSymbol declaration)
    {
        var typeArguments = target.TypeArguments.ToArray();
        var sourceUnion = SymbolEqualityComparer.Default.Equals(target, declaration);
        return TypeMeta.GetUnionTags(declaration, references).Select(item => sourceUnion
                ? UnionShape.BindSourceCase(item.Type, target)
                : UnionShape.Bind(item.Type, typeArguments))
            .Select(static type => new FormatterDependency(" -> union case " + type.FullyQualifiedToString(), type)).ToArray();
    }

    static FormatterDependency[] TypeArguments(INamedTypeSymbol named)
        => named.TypeArguments.Select(static (type, index) => new FormatterDependency($" -> type argument {index + 1}", type)).ToArray();

    internal static bool HasSelfInterface(INamedTypeSymbol type, INamedTypeSymbol? definition)
        => definition is not null && type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, definition) &&
            SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));

    UnsupportedFormatterShape? CheckHiddenMetadata(INamedTypeSymbol type)
    {
        if (type.Locations.Any(static location => location.IsInSource)) return null;

        // Public metadata import can hide private SharpPack annotations. Look
        // at the reachable type definition, not all types of its assembly.
        var all = completeMetadata ??= new ReferenceSymbols(
            compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All)));
        var definition = all.Compilation.GetTypeByMetadataName(type.GetFullMetadataName());
        if (definition is null) return null;

        if (definition.InstanceConstructors.Any(constructor =>
            constructor.ContainsAttribute(all.SharpPackConstructorAttribute) &&
            !all.Compilation.IsSymbolAccessibleWithin(constructor, all.Compilation.Assembly)))
        {
            return new(null, "the constructor marked SharpPackConstructor is not accessible from the consuming assembly");
        }

        foreach (var member in definition.GetAllMembers())
        {
            if (member is not (IFieldSymbol or IPropertySymbol) || member.IsStatic || !member.CanBeReferencedByName ||
                !member.ContainsAttribute(all.SharpPackIncludeAttribute) || member.ContainsAttribute(all.SharpPackIgnoreAttribute))
                continue;
            if (!all.Compilation.IsSymbolAccessibleWithin(member, all.Compilation.Assembly))
                return new(member.Name, "the member marked SharpPackInclude is not accessible from the consuming assembly");
        }

        if (definition.GetMembers().OfType<IMethodSymbol>().Any(method =>
            method.ContainsAttribute(all.SharpPackOnSerializingAttribute) ||
            method.ContainsAttribute(all.SharpPackOnSerializedAttribute) ||
            method.ContainsAttribute(all.SharpPackOnDeserializingAttribute) ||
            method.ContainsAttribute(all.SharpPackOnDeserializedAttribute)))
        {
            return new(null, "SharpPack callbacks require an existing formatter");
        }
        return null;
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
