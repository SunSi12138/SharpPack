using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpPack.Generator;

internal sealed record PartialTypeShape(
    string Name, TypeKind Kind, bool IsRecord, bool IsValueType, EquatableArray<TypeParameterShape> TypeParameters,
    bool IsNativeUnion)
{
    internal static PartialTypeShape Capture(INamedTypeSymbol type)
        => new(SyntaxFacts.GetKeywordKind(type.Name) == SyntaxKind.None ? type.Name : "@" + type.Name,
            type.TypeKind, type.IsRecord, type.IsValueType, TypeParameterShape.CaptureAll(type),
            NativeUnionAnalysis.IsSourceUnion(type));

    internal static EquatableArray<PartialTypeShape> CaptureContainers(INamedTypeSymbol type, bool useOriginalDefinitions = false)
    {
        if (type.ContainingType is null) return default;
        var containers = new Stack<PartialTypeShape>();
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
            containers.Push(Capture(useOriginalDefinitions ? containing.OriginalDefinition : containing));
        return new(containers);
    }
}

internal enum PrimaryTypeConstraint { None, Unmanaged, ValueType, ReferenceType, NullableReferenceType, NotNull }

internal sealed record TypeParameterShape(
    string Name, PrimaryTypeConstraint Primary, EquatableArray<string> ConstraintTypes, bool HasConstructorConstraint)
{
    internal static EquatableArray<TypeParameterShape> CaptureAll(INamedTypeSymbol type)
        => new(type.TypeParameters.Select(static parameter => new TypeParameterShape(
            parameter.FullyQualifiedToString(),
            parameter switch
            {
                { HasUnmanagedTypeConstraint: true } => PrimaryTypeConstraint.Unmanaged,
                { HasValueTypeConstraint: true } => PrimaryTypeConstraint.ValueType,
                { HasReferenceTypeConstraint: true, ReferenceTypeConstraintNullableAnnotation: NullableAnnotation.Annotated }
                    => PrimaryTypeConstraint.NullableReferenceType,
                { HasReferenceTypeConstraint: true } => PrimaryTypeConstraint.ReferenceType,
                { HasNotNullConstraint: true } => PrimaryTypeConstraint.NotNull,
                _ => PrimaryTypeConstraint.None,
            },
            new EquatableArray<string>(parameter.ConstraintTypes.Select(static constraint => constraint.FullyQualifiedToString())),
            parameter.HasConstructorConstraint)));
}

internal sealed record CollectionShape(CollectionKind Kind, EquatableArray<string> TypeArguments)
{
    internal static CollectionShape Capture(INamedTypeSymbol target, ReferenceSymbols references)
    {
        var (kind, collection) = TypeMeta.ParseCollectionKind(target, references);
        return new(kind, new EquatableArray<string>(collection!.TypeArguments.Select(static type => type.FullyQualifiedToString())));
    }
}

internal sealed record UnionCaseShape(
    ushort Tag, string TypeName, bool UsePackablePath, string SimpleName, string? RuntimeTypeName = null);

internal sealed record ExternalUnionFormatterShape(
    string Name, string Namespace, string QualifiedName, EquatableArray<TypeParameterShape> TypeParameters)
{
    internal static ExternalUnionFormatterShape Capture(INamedTypeSymbol formatter)
        => new(formatter.Name,
            formatter.ContainingNamespace.IsGlobalNamespace ? "" : formatter.ContainingNamespace.ToDisplayString(),
            QualifyName(formatter), TypeParameterShape.CaptureAll(formatter));

    internal static string QualifyName(INamedTypeSymbol formatter)
    {
        var ns = formatter.ContainingNamespace;
        var prefix = ns.IsGlobalNamespace ? "global::" : ns.FullyQualifiedToString() + ".";
        return prefix + formatter.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}

internal sealed record UnionShape(
    string TypeName, EquatableArray<UnionCaseShape> Cases, ExternalUnionFormatterShape? ExternalFormatter,
    bool IsNative = false)
{
    internal EquatableArray<TypeScriptUnionCase> ProjectTypeScriptCases()
        => new(Cases.Select(static item => new TypeScriptUnionCase(item.Tag, item.SimpleName)));

    internal static UnionShape Capture(TypeMeta model, ReferenceSymbols references, INamedTypeSymbol? externalFormatter)
    {
        var target = model.Symbol;
        var targetArguments = target.IsUnboundGenericType
            ? (externalFormatter is not null && target.Arity == externalFormatter.Arity
                ? externalFormatter.TypeParameters : target.TypeParameters).Cast<ITypeSymbol>().ToArray()
            : target.TypeArguments.ToArray();

        return new(externalFormatter is null ? target.FullyQualifiedToString() : Bind(target, targetArguments).FullyQualifiedToString(),
            new EquatableArray<UnionCaseShape>(model.UnionTags.Select(item => new UnionCaseShape(
                item.Tag, (externalFormatter is null ? BindSourceCase(item.Type, target) : Bind(item.Type, targetArguments)).FullyQualifiedToString(),
                item.Type.TryGetSharpPackableType(references, out var kind, out _) &&
                    kind is GenerateType.Object or GenerateType.VersionTolerant or GenerateType.CircularReference,
                item.Type.Name))),
            externalFormatter is null ? null : ExternalUnionFormatterShape.Capture(externalFormatter));
    }

    // Source unions can refer to open cases whose generic parameters belong
    // partly or entirely to containing types. Keep the positional convention,
    // outermost first, and validate the resulting closed-in-scope relation.
    internal static INamedTypeSymbol BindSourceCase(INamedTypeSymbol type, INamedTypeSymbol target)
    {
        if (!type.IsUnboundGenericType) return type;

        var arguments = TypeArgumentsInScope(target).ToArray();
        var definition = type.OriginalDefinition;
        if (arguments.Length != TypeArgumentsInScope(definition).Count()) return type;
        var position = 0;
        return Construct(definition);

        INamedTypeSymbol Construct(INamedTypeSymbol current)
        {
            if (current.ContainingType is { } containing)
            {
                var container = Construct(containing);
                current = container.GetTypeMembers(current.Name, current.Arity)[0];
            }
            if (current.Arity == 0) return current;

            var ownArguments = arguments.Skip(position).Take(current.Arity).ToArray();
            position += ownArguments.Length;
            return current.Construct(ownArguments);
        }

        static IEnumerable<ITypeSymbol> TypeArgumentsInScope(INamedTypeSymbol current)
            => current.ContainingType is { } containing
                ? TypeArgumentsInScope(containing).Concat(current.TypeArguments)
                : current.TypeArguments;
    }

    internal static INamedTypeSymbol Bind(INamedTypeSymbol type, ITypeSymbol[] targetArguments)
    {
        if (!type.IsUnboundGenericType) return type;

        var definition = type.ConstructedFrom;
        if (type.ContainingType is { IsUnboundGenericType: true } containing)
        {
            // Rebind the containing declaration first: a non-generic
            // nested case still needs its parent's type arguments.
            var boundContainer = Bind(containing, targetArguments);
            if (boundContainer.IsUnboundGenericType) return type;
            definition = boundContainer.GetTypeMembers(type.Name, type.Arity)[0];
        }

        return definition.Arity == 0 ? definition
            : definition.Arity == targetArguments.Length ? definition.Construct(targetArguments) : type;
    }
}
