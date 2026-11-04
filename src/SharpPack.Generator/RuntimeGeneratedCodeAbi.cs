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

        return value >= MinimumVersion && value <= MaximumVersion
            ? null
            : value.ToString(CultureInfo.InvariantCulture);
    }
}
