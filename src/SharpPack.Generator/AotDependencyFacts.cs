using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

internal readonly record struct AotFactoryRoot(string TypeName, bool HasContextFactory);

internal readonly record struct AotDependencyFacts(
    EquatableArray<AotFactoryRoot> Factories,
    EquatableArray<string> ClosedFormatters)
{
    internal static AotDependencyFacts Capture(MemberMeta[] members, ReferenceSymbols references)
        => Capture(members.Select(static member => member.MemberType), references);

    internal static AotDependencyFacts Capture(IEnumerable<ITypeSymbol> types, ReferenceSymbols references)
    {
        var factories = new List<AotFactoryRoot>();
        var formatters = new List<string>();
        var contextFactory = references.Compilation.GetTypeByMetadataName("SharpPack.ISharpPackContextFormatterFactory`1");
        foreach (var type in types)
            Visit(type);
        return new(new EquatableArray<AotFactoryRoot>(factories), new EquatableArray<string>(formatters));

        void Visit(ITypeSymbol type)
        {
            if (references.KnownTypes.GetNonDefaultFormatterName(type) is { } formatter)
                formatters.Add(formatter);

            if (type is INamedTypeSymbol model &&
                type.TryGetSharpPackableType(references, out var mode, out _) &&
                mode != GenerateType.NoGenerate)
            {
                // Referenced assemblies can only use the factory they shipped.
                var willGenerateContextFactory =
                    SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, references.Compilation.Assembly) &&
                    mode is (GenerateType.Object or GenerateType.VersionTolerant or GenerateType.CircularReference) &&
                    !type.ContainsAttribute(references.SharpPackUnionAttribute);
                var hasContextFactory = model.AllInterfaces.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, contextFactory) &&
                    SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], type));
                factories.Add(new(type.FullyQualifiedToString(), willGenerateContextFactory || hasContextFactory));
            }

            if (type is IArrayTypeSymbol array)
                Visit(array.ElementType);
            else if (type is INamedTypeSymbol named)
                foreach (var argument in named.TypeArguments)
                    Visit(argument);
        }
    }
}
