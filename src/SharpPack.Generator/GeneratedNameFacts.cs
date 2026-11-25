using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPack.Generator;

internal readonly record struct GeneratedNameFacts(
    EquatableArray<string> MembersAndParameters,
    EquatableArray<string> SelfRootIdentifiers,
    EquatableArray<string> CircularReadIdentifiers,
    bool HasUserStaticInitialization)
{
    internal const string SelfRootStem = "__SharpPackPreserveSelfFactoryRoots";
    internal const string CircularReadTagStem = "__SharpPackCircularReadTag";

    internal static GeneratedNameFacts Capture(INamedTypeSymbol type, MemberMeta[] members, bool hasCircularRead)
    {
        var memberNames = type.GetAllMembers().Select(static member => member.Name)
            .Where(IsReservedHelperName).ToArray();
        var hasStaticInitialization = members.Any(static member => member.Kind == MemberKind.CustomFormatter) ||
            type.GetMembers().Any(UsesStaticInitialization);
        return new(
            OrderedNames(memberNames.Concat(type.TypeParameters.Select(static parameter => parameter.Name)
                .Where(IsReservedHelperName))),
            hasStaticInitialization ? default : CaptureFactoryIdentifiers(type, SelfRootStem, memberNames),
            hasCircularRead ? CaptureFactoryIdentifiers(type, CircularReadTagStem, memberNames) : default,
            hasStaticInitialization);
    }

    internal static EquatableArray<string> CaptureFactoryIdentifiers(INamedTypeSymbol type, string stem)
        => CaptureFactoryIdentifiers(type, stem, type.GetAllMembers().Select(static member => member.Name));

    static EquatableArray<string> CaptureFactoryIdentifiers(INamedTypeSymbol type, string stem, IEnumerable<string> members)
    {
        // Preserve binding of primary-constructor parameters and unqualified
        // names imported by using static, not just declared member names.
        var names = new HashSet<string>(members.Where(name => name.StartsWith(stem, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        if (type.Name.StartsWith(stem, StringComparison.Ordinal))
            names.Add(type.Name);
        for (INamedTypeSymbol? enclosing = type; enclosing is not null; enclosing = enclosing.ContainingType)
            foreach (var parameter in enclosing.TypeParameters)
                if (parameter.Name.StartsWith(stem, StringComparison.Ordinal))
                    names.Add(parameter.Name);
        foreach (var reference in type.DeclaringSyntaxReferences)
        foreach (var token in reference.GetSyntax().DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText.StartsWith(stem, StringComparison.Ordinal))
                names.Add(token.ValueText);
        }
        return OrderedNames(names);
    }

    static EquatableArray<string> OrderedNames(IEnumerable<string> names)
        => new(new SortedSet<string>(names, StringComparer.Ordinal));

    static bool IsReservedHelperName(string name)
        => name.StartsWith("__SharpPack", StringComparison.Ordinal);

    static bool UsesStaticInitialization(ISymbol member)
        => member is IMethodSymbol method && method.IsStatic &&
           (method.MethodKind == MethodKind.StaticConstructor ||
            method.Name == "StaticConstructor" && method.Parameters.Length == 0 &&
            method.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                (declaration.Body is not null || declaration.ExpressionBody is not null))) ||
           member is IFieldSymbol field && field.IsStatic && !field.IsConst &&
           field.DeclaringSyntaxReferences.Any(static reference =>
               reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }) ||
           member is IPropertySymbol property && property.IsStatic &&
           property.DeclaringSyntaxReferences.Any(static reference =>
               reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null });
}
