using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

internal sealed record PartialTypeShape(
    string Name, TypeKind Kind, bool IsRecord, bool IsValueType, EquatableArray<TypeParameterShape> TypeParameters)
{
    internal static PartialTypeShape Capture(INamedTypeSymbol type)
        => new(type.Name, type.TypeKind, type.IsRecord, type.IsValueType, TypeParameterShape.CaptureAll(type));

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
            parameter.Name,
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

internal sealed record UnionCaseShape(ushort Tag, string TypeName, bool UsePackablePath);

internal sealed record ExternalUnionFormatterShape(string Name, EquatableArray<TypeParameterShape> TypeParameters);

internal sealed record UnionShape(
    string TypeName, EquatableArray<UnionCaseShape> Cases, ExternalUnionFormatterShape? ExternalFormatter)
{
    internal static UnionShape Capture(TypeMeta model, ReferenceSymbols references, INamedTypeSymbol? externalFormatter)
    {
        var target = model.Symbol;
        var targetArguments = target.IsGenericType
            ? string.Join(", ", target.TypeArguments.Select(static argument => argument.FullyQualifiedToString())) : null;

        return new(ConstructName(target),
            new EquatableArray<UnionCaseShape>(model.UnionTags.Select(item => new UnionCaseShape(
                item.Tag, ConstructName(item.Type),
                item.Type.TryGetSharpPackableType(references, out var kind, out _) &&
                    kind is GenerateType.Object or GenerateType.VersionTolerant or GenerateType.CircularReference))),
            externalFormatter is null ? null : new(externalFormatter.Name, TypeParameterShape.CaptureAll(externalFormatter)));

        string ConstructName(INamedTypeSymbol type)
        {
            var name = type.FullyQualifiedToString();
            return type.IsGenericType && targetArguments is not null
                ? name.Replace("<>", "<" + targetArguments + ">") : name;
        }
    }
}
