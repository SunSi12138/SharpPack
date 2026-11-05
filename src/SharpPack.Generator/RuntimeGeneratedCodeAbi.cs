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
        }
        return null;
    }
}
