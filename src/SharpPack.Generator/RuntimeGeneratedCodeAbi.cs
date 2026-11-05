using Microsoft.CodeAnalysis;
using System.Globalization;

namespace SharpPack.Generator;

internal static class RuntimeGeneratedCodeAbi
{
    internal const int MinimumVersion = 1;
    internal const int MaximumVersion = 1;
    const string ContractMetadataName =
        "SharpPack.CompilerServices.SharpPackGeneratedCodeContract";

    // Resolve through the attribute's assembly, rather than the consumer's
    // compilation: a same-named consumer type must not override the runtime ABI.
    internal static string? GetUnsupportedVersion(Compilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName(
            SharpPackGenerator.SharpPackableAttributeFullName);
        var contract = attribute?.ContainingAssembly.GetTypeByMetadataName(ContractMetadataName);
        if (contract is null) return "missing";

        var members = contract.GetMembers("Version");
        var version = members.Length == 1 ? members[0] as IFieldSymbol : null;
        if (contract.DeclaredAccessibility != Accessibility.Public ||
            contract.TypeKind != TypeKind.Class || !contract.IsStatic ||
            version is null || version.DeclaredAccessibility != Accessibility.Public ||
            !version.IsConst || version.Type.SpecialType != SpecialType.System_Int32 ||
            version.ConstantValue is not int value)
        {
            return "invalid";
        }

        if (value < MinimumVersion || value > MaximumVersion)
            return value.ToString(CultureInfo.InvariantCulture);

        // ABI 1 was introduced on an unpublished handshake-only candidate.
        // Additive compiler-services capabilities are checked explicitly so that
        // candidate (or an incomplete runtime) gets this diagnostic, not CS errors.
        var assembly = attribute!.ContainingAssembly;
        foreach (var name in new[] { "PreserveAttribute", "SharpPackExactBufferWriter", "SharpPackPooledBufferWriter" })
        {
            var helper = assembly.GetTypeByMetadataName("SharpPack.CompilerServices." + name);
            if (helper is null || helper.DeclaredAccessibility != Accessibility.Public)
                return "1 (missing compiler-services capability: " + name + ")";
            if (!HasRequiredShape(helper, name))
                return "1 (invalid compiler-services capability: " + name + ")";
        }
        return null;
    }

    static bool HasRequiredShape(INamedTypeSymbol type, string name)
    {
        bool IsByteArray(ITypeSymbol value) => value is IArrayTypeSymbol array &&
            array.IsSZArray && array.ElementType.SpecialType == SpecialType.System_Byte;
        bool IsSelf(ITypeSymbol value) => SymbolEqualityComparer.Default.Equals(value, type);
        bool IsWriter(ITypeSymbol value, ITypeSymbol argument) => value is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "SharpPack.SharpPackWriter<TBufferWriter>" &&
            named.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], argument);
        bool Method(string member, Func<IMethodSymbol, bool> predicate) => type.GetMembers(member)
            .OfType<IMethodSymbol>().Any(method => method.DeclaredAccessibility == Accessibility.Public && predicate(method));
        bool Constructor(Func<IMethodSymbol, bool> predicate) => type.InstanceConstructors
            .Any(method => method.DeclaredAccessibility == Accessibility.Public && predicate(method));

        if (name == "PreserveAttribute")
            return type.TypeKind == TypeKind.Class && !type.IsAbstract &&
                type.BaseType?.ToDisplayString() == "System.Attribute" && Constructor(ctor => ctor.Parameters.Length == 0) &&
                type.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute")
                    .All(attribute => attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is int targets && targets == (int)AttributeTargets.All);

        if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType || !type.AllInterfaces.Any(iface =>
            iface.OriginalDefinition.ToDisplayString() == "System.Buffers.IBufferWriter<T>" &&
            iface.TypeArguments[0].SpecialType == SpecialType.System_Byte)) return false;

        if (name == "SharpPackExactBufferWriter")
            return Constructor(ctor => ctor.Parameters.Length == 1 && IsByteArray(ctor.Parameters[0].Type) &&
                       ctor.Parameters[0].RefKind == RefKind.None) &&
                Method("GetFilledBuffer", method => !method.IsStatic && method.Arity == 0 &&
                    method.Parameters.Length == 0 && IsByteArray(method.ReturnType)) &&
                Method("CreateWriter", method => method.IsStatic && method.Arity == 0 &&
                    method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.Ref &&
                    IsSelf(method.Parameters[0].Type) && IsWriter(method.ReturnType, type));

        return Method("Rent", method => method.IsStatic && method.Arity == 0 && IsSelf(method.ReturnType) &&
                method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.Out &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Int64) &&
            Method("Return", method => method.IsStatic && method.Arity == 0 && method.ReturnsVoid &&
                method.Parameters.Length == 2 && method.Parameters.All(parameter => parameter.RefKind == RefKind.None) &&
                IsSelf(method.Parameters[0].Type) && method.Parameters[1].Type.SpecialType == SpecialType.System_Int64) &&
            Method("WriteToAndReset", method => !method.IsStatic && method.Arity == 1 && method.ReturnsVoid &&
                method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.Ref &&
                IsWriter(method.Parameters[0].Type, method.TypeParameters[0]) &&
                HasBufferWriterConstraint(method.TypeParameters[0]));
    }

    static bool HasBufferWriterConstraint(ITypeParameterSymbol parameter)
        => !parameter.HasConstructorConstraint && !parameter.HasReferenceTypeConstraint &&
            !parameter.HasValueTypeConstraint && !parameter.HasUnmanagedTypeConstraint &&
            !parameter.HasNotNullConstraint && parameter.ConstraintTypes.Length == 1 &&
            parameter.ConstraintTypes[0] is INamedTypeSymbol constraint &&
            constraint.OriginalDefinition.ToDisplayString() == "System.Buffers.IBufferWriter<T>" &&
            constraint.TypeArguments[0].SpecialType == SpecialType.System_Byte;
}
