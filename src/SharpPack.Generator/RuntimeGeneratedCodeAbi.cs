using Microsoft.CodeAnalysis;
using System.Globalization;

namespace SharpPack.Generator;

internal static class RuntimeGeneratedCodeAbi
{
    internal const int MinimumVersion = 1;
    internal const int MaximumVersion = 1;
    const string ContractMetadataName =
        "SharpPack.CompilerServices.SharpPackGeneratedCodeContract";

    internal static string? GetUnsupportedVersion(Compilation compilation)
    {
        // Emission binds to global::SharpPack. An alias-only reference or a
        // same-named consumer type cannot supply the marker for that runtime.
        var runtime = compilation.References
            .Where(static reference => reference.Properties.Aliases.IsDefaultOrEmpty ||
                reference.Properties.Aliases.Contains("global"))
            .Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .FirstOrDefault(static assembly => assembly.Identity.Name == "SharpPack.Core");
        var contract = runtime?.GetTypeByMetadataName(ContractMetadataName);
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
