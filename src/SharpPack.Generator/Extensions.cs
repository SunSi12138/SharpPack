using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPack.Generator;

internal static class Extensions
{
    private const string UnderScorePrefix = "_";

    public static string NewLine(this IEnumerable<string> source)
    {
        return string.Join(Environment.NewLine, source);
    }

    public static bool EqualsNamespaceAndName(this ITypeSymbol? left, ITypeSymbol? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        var l = left.ContainingNamespace;
        var r = right.ContainingNamespace;
        while (l != null && r != null)
        {
            if (l.Name != r.Name) return false;

            l = l.ContainingNamespace;
            r = r.ContainingNamespace;
        }

        return (left.Name == right.Name);
    }

    public static bool ContainsAttribute(this ISymbol symbol, INamedTypeSymbol attribtue)
    {
        return symbol.GetAttributes().Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attribtue));
    }

    public static AttributeData? GetAttribute(this ISymbol symbol, INamedTypeSymbol attribtue)
    {
        return symbol.GetAttributes().FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attribtue));
    }

    public static AttributeData? GetImplAttribute(this ISymbol symbol, INamedTypeSymbol implAttribtue)
    {
        return symbol.GetAttributes().FirstOrDefault(x =>
        {
            if (x.AttributeClass == null) return false;
            if (x.AttributeClass.EqualsUnconstructedGenericType(implAttribtue)) return true;

            foreach (var item in x.AttributeClass.GetAllBaseTypes())
            {
                if (item.EqualsUnconstructedGenericType(implAttribtue))
                {
                    return true;
                }
            }
            return false;
        });
    }

    public static IEnumerable<ISymbol> GetAllMembers(this INamedTypeSymbol symbol, bool withoutOverride = true)
    {
        // Iterate Parent -> Derived
        if (symbol.BaseType != null)
        {
            foreach (var item in GetAllMembers(symbol.BaseType))
            {
                // override item already iterated in parent type
                if (!withoutOverride || !item.IsOverride)
                {
                    yield return item;
                }
            }
        }

        foreach (var item in symbol.GetMembers())
        {
            if (!withoutOverride || !item.IsOverride)
            {
                yield return item;
            }
        }
    }

    public static IEnumerable<ISymbol> GetParentMembers(this INamedTypeSymbol symbol)
    {
        // Iterate Parent -> Derived
        if (symbol.BaseType != null)
        {
            foreach (var item in GetAllMembers(symbol.BaseType))
            {
                // override item already iterated in parent type
                if (!item.IsOverride)
                {
                    yield return item;
                }
            }
        }
    }

    public static bool TryGetSharpPackableType(this ITypeSymbol symbol, ReferenceSymbols references, out GenerateType generateType, out SerializeLayout serializeLayout)
    {
        var memPackAttr = symbol.GetAttribute(references.SharpPackableAttribute);
        var packableCtorArgs = memPackAttr?.ConstructorArguments;
        generateType = GenerateType.Object;
        serializeLayout = SerializeLayout.Sequential;

        if (memPackAttr == null || packableCtorArgs == null)
        {
            var memPackUnionFormatterAttr = symbol.GetAttribute(references.SharpPackUnionFormatterAttribute);
            generateType = memPackUnionFormatterAttr != null ? GenerateType.Union : GenerateType.NoGenerate;
            serializeLayout = SerializeLayout.Sequential;
            return false;
        }

        if (packableCtorArgs.Value.Length != 0)
        {
            // SharpPackable has three attribtues
            // [GenerateType generateType]
            // [SerializeLayout serializeLayout]
            // [GenerateType generateType, SerializeLayout serializeLayout]

            if (packableCtorArgs.Value.Length == 1)
            {
                var ctorValue = packableCtorArgs.Value[0];

                // check which construcotr was used
                var attrConstructor = memPackAttr.AttributeConstructor;
                var isSerializeLayout = attrConstructor!.Parameters[0].Type.Name == nameof(SerializeLayout);
                if (isSerializeLayout)
                {
                    generateType = GenerateType.Object;
                    serializeLayout = (SerializeLayout)(ctorValue.Value!);
                }
                else
                {
                    generateType = (GenerateType)(ctorValue.Value!);
                    serializeLayout = SerializeLayout.Sequential;
                    if (generateType is GenerateType.VersionTolerant or GenerateType.CircularReference)
                    {
                        serializeLayout = SerializeLayout.Explicit;
                    }
                }
            }
            else
            {
                generateType = (GenerateType)(packableCtorArgs.Value[0].Value ?? GenerateType.Object);
                serializeLayout = (SerializeLayout)(packableCtorArgs.Value[1].Value ?? SerializeLayout.Sequential);
            }
        }

        if (generateType == GenerateType.Object && (symbol.IsStatic || symbol.IsAbstract))
        {
            // static or abstract class is Union, set as NoGenerate
            generateType = GenerateType.Union;
            serializeLayout = SerializeLayout.Sequential;
        }

        return true;
    }

    public static bool IsSharpPackableNoGenerate(this ITypeSymbol symbol, ReferenceSymbols references)
        => symbol.GetAttribute(references.SharpPackableAttribute) is { } attribute &&
            attribute.IsSharpPackableNoGenerate();

    public static bool IsSharpPackableNoGenerate(this AttributeData attribute)
        => attribute.AttributeConstructor?.Parameters is { Length: > 0 } parameters &&
            parameters[0].Type.Name == nameof(GenerateType) &&
            attribute.ConstructorArguments is { Length: > 0 } arguments &&
            arguments[0].Value is int mode && mode == (int)GenerateType.NoGenerate;

    public static bool IsWillImplementSharpPackUnion(this ITypeSymbol symbol, ReferenceSymbols references)
    {
        return symbol.IsAbstract && symbol.ContainsAttribute(references.SharpPackUnionAttribute);
    }

    public static bool HasDuplicate<T>(this IEnumerable<T> source)
    {
        var set = new HashSet<T>();
        foreach (var item in source)
        {
            if (!set.Add(item))
            {
                return true;
            }
        }
        return false;
    }

    public static IEnumerable<INamedTypeSymbol> GetAllBaseTypes(this INamedTypeSymbol symbol)
    {
        var t = symbol.BaseType;
        while (t != null)
        {
            yield return t;
            t = t.BaseType;
        }
    }

    internal static string ToFullyQualifiedFormatDisplayString(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static string FullyQualifiedToString(this ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static bool EqualsUnconstructedGenericType(this INamedTypeSymbol left, INamedTypeSymbol right)
    {
        var l = left.IsGenericType ? left.ConstructUnboundGenericType() : left;
        var r = right.IsGenericType ? right.ConstructUnboundGenericType() : right;
        return SymbolEqualityComparer.Default.Equals(l, r);
    }

    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) => DistinctBy(source, keySelector, null);

    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
    {
        return DistinctByIterator(source, keySelector, comparer);
    }

    private static IEnumerable<TSource> DistinctByIterator<TSource, TKey>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey>? comparer)
    {
        using IEnumerator<TSource> enumerator = source.GetEnumerator();

        if (enumerator.MoveNext())
        {
            var set = new HashSet<TKey>(comparer);
            do
            {
                TSource element = enumerator.Current;
                if (set.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
            while (enumerator.MoveNext());
        }
    }

    public static bool TryGetConstructorParameter(this IMethodSymbol constructor, ISymbol member, out IParameterSymbol? constructorParameter)
    {
        constructorParameter = GetConstructorParameter(constructor, member.Name);
        if (constructorParameter == null && member.Name.StartsWith(UnderScorePrefix))
        {
            constructorParameter = GetConstructorParameter(constructor, member.Name.Substring(UnderScorePrefix.Length));
        }

        return constructorParameter != null;

        static IParameterSymbol? GetConstructorParameter(IMethodSymbol constructor, string name)
            => constructor.Parameters.FirstOrDefault(parameter => parameter.Name == name)
                ?? constructor.Parameters.FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    internal static T[] FindConstructorBindings<T>(IEnumerable<T> candidates, Func<T, string?> getBinding, string parameter)
    {
        var exact = candidates.Where(candidate => getBinding(candidate) == parameter).ToArray();
        // A single legacy slot can supply parameters that differ only by case. If each
        // spelling has its own slot, the exact match takes precedence.
        return exact.Length != 0 ? exact : candidates.Where(candidate =>
            string.Equals(getBinding(candidate), parameter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    internal static bool HasGlobalTypeName(this ITypeSymbol type, Compilation compilation, ISymbol accessibleWithin,
        CancellationToken token, Func<INamedTypeSymbol, bool>? canUseType = null)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType.HasGlobalTypeName(compilation, accessibleWithin, token, canUseType);
        if (type is INamedTypeSymbol { TupleUnderlyingType: { } tuple })
            type = tuple;

        if (type is not INamedTypeSymbol named || !named.CanBeReferencedByName ||
            !compilation.IsSymbolAccessibleWithin(named, accessibleWithin))
            return false;

        for (var current = named; current is not null; current = current.ContainingType)
        {
            if (canUseType?.Invoke(current) == false ||
                current.DeclaringSyntaxReferences.Any(declaration => declaration.GetSyntax(token) is TypeDeclarationSyntax syntax &&
                    syntax.Modifiers.Any(static modifier => modifier.ValueText == "file")) ||
                !current.TypeArguments.All(argument => argument.HasGlobalTypeName(compilation, accessibleWithin, token, canUseType)))
                return false;
        }

        // Metadata lookup excludes alias-only references and reports ambiguity;
        // a global name must bind back to this exact definition.
        return SymbolEqualityComparer.Default.Equals(compilation.GetTypeByMetadataName(named.GetFullMetadataName()), named.OriginalDefinition);
    }

    internal static string GetFullMetadataName(this INamedTypeSymbol named)
    {
        var name = named.MetadataName;
        for (var enclosing = named.ContainingType; enclosing is not null; enclosing = enclosing.ContainingType)
            name = enclosing.MetadataName + "+" + name;
        for (var space = named.ContainingNamespace; !space.IsGlobalNamespace; space = space.ContainingNamespace)
            name = space.MetadataName + "." + name;
        return name;
    }
}
