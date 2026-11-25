using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

// A value snapshot of a validated declaration. Roslyn symbols and locations
// remain with the analysis/diagnostics, not with these model facts.
internal sealed record TypeShape(
    string DeclarationName,
    string TargetName,
    TypeKind DeclarationKind,
    TypeKind TargetKind,
    string Namespace,
    string ShortName,
    GenerateType GenerateType,
    SerializeLayout Layout,
    bool IsValueType,
    bool IsUnmanaged,
    bool IsRecord,
    bool IsUnion,
    EquatableArray<MemberShape> Members,
    EquatableArray<ParameterShape> ConstructorParameters,
    EquatableArray<CallbackShape> Callbacks,
    EquatableArray<UnionCaseShape> UnionCases)
{
    internal static TypeShape Create(INamedTypeSymbol declaration, TypeMeta model, ReferenceSymbols references)
    {
        var target = model.Symbol;
        var captureContextDependencies = !model.IsUnmanagedType && !model.IsUnion && model.GenerateType != GenerateType.Collection;
        return new TypeShape(
            declaration.FullyQualifiedToString(),
            target.FullyQualifiedToString(),
            declaration.TypeKind,
            target.TypeKind,
            target.ContainingNamespace.IsGlobalNamespace ? "" : target.ContainingNamespace.ToDisplayString(),
            model.TypeName,
            model.GenerateType,
            model.SerializeLayout,
            model.IsValueType,
            model.IsUnmanagedType,
            model.IsRecord,
            model.IsUnion,
            new EquatableArray<MemberShape>(model.Members.Select(member =>
                MemberShape.Create(member, references, captureContextDependencies))),
            model.Constructor is { } constructor
                ? new EquatableArray<ParameterShape>(constructor.Parameters.Select(static parameter =>
                    new ParameterShape(parameter.Name, parameter.Type.FullyQualifiedToString())))
                : default,
            new EquatableArray<CallbackShape>(
                CallbacksFor(CallbackPhase.Serializing, model.OnSerializing)
                    .Concat(CallbacksFor(CallbackPhase.Serialized, model.OnSerialized))
                    .Concat(CallbacksFor(CallbackPhase.Deserializing, model.OnDeserializing))
                    .Concat(CallbacksFor(CallbackPhase.Deserialized, model.OnDeserialized))),
            new EquatableArray<UnionCaseShape>(model.UnionTags.Select(static item =>
                new UnionCaseShape(item.Tag, item.Type.FullyQualifiedToString()))));

        static IEnumerable<CallbackShape> CallbacksFor(CallbackPhase phase, IEnumerable<MethodMeta> callbacks)
            => callbacks.Select(callback => new CallbackShape(phase, callback.Name, callback.IsStatic, callback.IsValueType,
                callback.UseReaderArgument || callback.UseWriterArgument));
    }
}

internal sealed record MemberShape(
    string Name,
    string TypeName,
    MemberKind Kind,
    int Order,
    bool HasExplicitOrder,
    bool IsAssignable,
    bool HasExactSizeSafeAccessor,
    bool SuppressDefaultInitialization,
    string? ConstructorParameter,
    CustomFormatterShape? CustomFormatter,
    EquatableArray<string> ContextDependencyTypes)
{
    internal static MemberShape Create(MemberMeta member, ReferenceSymbols references, bool captureContextDependencies)
        => new(member.Name, member.MemberType.FullyQualifiedToString(), member.Kind, member.Order,
            member.HasExplicitOrder, member.IsAssignable, member.HasExactSizeSafeAccessor,
            member.SuppressDefaultInitialization, member.ConstructorParameterName,
            member.CustomFormatterAttribute is { } attribute
                ? new CustomFormatterShape(
                    member.CustomFormatterName!,
                    member.CustomFormatter!.FullyQualifiedToString(),
                    new EquatableArray<AttributeValueShape>(attribute.ConstructorArguments.Select(AttributeValueShape.Create)),
                    new EquatableArray<NamedAttributeValueShape>(attribute.NamedArguments.Select(static pair =>
                        new NamedAttributeValueShape(pair.Key, AttributeValueShape.Create(pair.Value)))))
                : null,
            captureContextDependencies && member.Kind != MemberKind.CustomFormatter
                ? ContextTypes(member.MemberType, references)
                : default);

    static EquatableArray<string> ContextTypes(ITypeSymbol type, ReferenceSymbols references)
    {
        var names = new List<string>();
        Add(type);
        return new EquatableArray<string>(names);

        void Add(ITypeSymbol current)
        {
            names.Add(current.FullyQualifiedToString());
            if (current is IArrayTypeSymbol array)
            {
                Add(array.ElementType);
            }
            else if (current is INamedTypeSymbol { IsGenericType: true } named && references.KnownTypes.Contains(named))
            {
                foreach (var argument in named.TypeArguments)
                    Add(argument);
            }
        }
    }
}

internal sealed record ParameterShape(string Name, string TypeName);

internal enum CallbackPhase { Serializing, Serialized, Deserializing, Deserialized }

internal sealed record CallbackShape(CallbackPhase Phase, string Name, bool IsStatic, bool IsValueType, bool PassArguments);

internal sealed record UnionCaseShape(ushort Tag, string TypeName);

internal sealed record CustomFormatterShape(
    string FormatterType, string AttributeType,
    EquatableArray<AttributeValueShape> Arguments, EquatableArray<NamedAttributeValueShape> Initializers);

internal sealed record NamedAttributeValueShape(string Name, AttributeValueShape Value);

internal sealed record AttributeValueShape(
    TypedConstantKind Kind, bool IsNull, string? TypeName, object? Scalar, long FloatingBits,
    EquatableArray<AttributeValueShape> Elements)
{
    internal static AttributeValueShape Create(TypedConstant constant)
    {
        if (constant.IsNull)
            return new(constant.Kind, true, constant.Type?.FullyQualifiedToString(), null, 0, default);
        if (constant.Kind == TypedConstantKind.Array)
            return new(constant.Kind, false, ((IArrayTypeSymbol)constant.Type!).ElementType.FullyQualifiedToString(),
                null, 0, new EquatableArray<AttributeValueShape>(constant.Values.Select(Create)));
        if (constant.Kind == TypedConstantKind.Type)
            return new(constant.Kind, false, ((ITypeSymbol)constant.Value!).FullyQualifiedToString(), null, 0, default);

        var scalar = constant.Value;
        // Boxed floating point equality treats positive and negative zero as
        // equal, but attribute values can make that difference observable.
        var bits = scalar switch
        {
            float value => BitConverter.DoubleToInt64Bits(value),
            double value => BitConverter.DoubleToInt64Bits(value),
            _ => 0,
        };
        return new(constant.Kind, false, constant.Type?.FullyQualifiedToString(), scalar, bits, default);
    }
}
