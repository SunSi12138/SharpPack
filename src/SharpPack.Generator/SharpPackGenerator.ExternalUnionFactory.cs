using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace SharpPack.Generator;

partial class SharpPackGenerator
{
    internal static EquatableArray<ExternalUnionFactoryPlan> PlanExternalUnionTargetFactories(
        ImmutableArray<TypeDeclarationSyntax> declarations,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (declarations.IsDefaultOrEmpty)
        {
            return default;
        }

        var references = new ReferenceSymbols(compilation);
        var groups =
            new Dictionary<INamedTypeSymbol, List<ExternalUnionFactoryEntry>>(
                SymbolEqualityComparer.Default);

        foreach (var declaration in declarations)
        {
            var semanticModel =
                compilation.GetSemanticModel(declaration.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(
                    declaration,
                    cancellationToken) is not INamedTypeSymbol formatter)
            {
                continue;
            }

            var attribute = formatter.GetAttribute(
                references.SharpPackUnionFormatterAttribute);
            if (attribute?.ConstructorArguments[0].Value is not
                INamedTypeSymbol union)
            {
                continue;
            }

            var target = union.OriginalDefinition;
            if (!SymbolEqualityComparer.Default.Equals(
                    target.ContainingAssembly,
                    formatter.ContainingAssembly) ||
                target.DeclaringSyntaxReferences.Length == 0 ||
                !IsPartialTarget(target))
            {
                continue;
            }

            if (!groups.TryGetValue(target, out var entries))
            {
                entries = new List<ExternalUnionFactoryEntry>();
                groups.Add(target, entries);
            }

            entries.Add(new ExternalUnionFactoryEntry(formatter, union));
        }

        var plans = groups.OrderBy(static group =>
                     group.Key.FullyQualifiedToString(),
                     StringComparer.Ordinal)
            .Select(static group => PlanExternalUnionTargetFactory(group.Key, group.Value))
            .OfType<ExternalUnionFactoryPlan>();
        return new(plans);
    }

    static bool IsPartialTarget(INamedTypeSymbol target)
    {
        for (var current = target;
             current is not null;
             current = current.ContainingType)
        {
            if (current.DeclaringSyntaxReferences.Any(static reference =>
                    reference.GetSyntax() is not
                        TypeDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(static modifier =>
                        modifier.IsKind(SyntaxKind.PartialKeyword))))
            {
                return false;
            }
        }

        return true;
    }

    static ExternalUnionFactoryPlan? PlanExternalUnionTargetFactory(
        INamedTypeSymbol target,
        List<ExternalUnionFactoryEntry> entries)
    {
        var compatibleEntries = entries
            .Where(entry => CanConstructFormatter(target, entry))
            .OrderBy(static entry => IsOpenPattern(entry.Union) ? 1 : 0)
            .ThenBy(static entry =>
                entry.Union.FullyQualifiedToString(),
                StringComparer.Ordinal)
            .ThenBy(static entry =>
                entry.Formatter.FullyQualifiedToString(),
                StringComparer.Ordinal)
            .ToArray();
        if (compatibleEntries.Length == 0)
        {
            return null;
        }

        var matches = new List<ExternalUnionFactoryMatch>();
        var patterns = new HashSet<EquatableArray<ExternalUnionTypeMatch>>();
        string? fallback = null;
        foreach (var entry in compatibleEntries)
        {
            if (IsOpenPattern(entry.Union))
            {
                fallback ??= ConstructedFormatterName(target, entry);
                continue;
            }

            var arguments = new EquatableArray<ExternalUnionTypeMatch>(target.TypeParameters.Zip(entry.Union.TypeArguments,
                static (parameter, argument) => new ExternalUnionTypeMatch(parameter.Name, argument.FullyQualifiedToString())));
            if (patterns.Add(arguments))
                matches.Add(new(arguments, ConstructedFormatterName(target, entry)));
        }

        const string rootStem = "__SharpPackPreserveFactory";
        return new(
            target.FullyQualifiedToString(), ConstructedTargetName(target),
            target.ContainingNamespace.IsGlobalNamespace ? "" : target.ContainingNamespace.ToString(),
            PartialTypeShape.Capture(target), PartialTypeShape.CaptureContainers(target, useOriginalDefinitions: true),
            SerializationPlanner.AvailableFactoryName(rootStem, GeneratedNameFacts.CaptureFactoryIdentifiers(target, rootStem)),
            new(matches), fallback);
    }

    static bool CanConstructFormatter(
        INamedTypeSymbol target,
        ExternalUnionFactoryEntry entry)
    {
        if (!target.IsGenericType)
        {
            return entry.Formatter.TypeParameters.Length == 0;
        }

        if (IsOpenPattern(entry.Union))
        {
            return entry.Formatter.TypeParameters.Length ==
                target.TypeParameters.Length;
        }

        return entry.Formatter.TypeParameters.Length == 0 &&
            entry.Union.TypeArguments.All(static argument =>
                argument is not ITypeParameterSymbol);
    }

    static bool IsOpenPattern(INamedTypeSymbol union)
        => !union.IsGenericType ||
           union.IsUnboundGenericType ||
           union.TypeArguments.All(static argument => argument is ITypeParameterSymbol);

    static string ConstructedTargetName(INamedTypeSymbol target)
    {
        if (!target.IsGenericType)
        {
            return target.FullyQualifiedToString();
        }

        var typeArguments = string.Join(
            ", ",
            target.TypeParameters.Select(static parameter => parameter.Name));
        var definition = target.ConstructUnboundGenericType()
            .FullyQualifiedToString();
        return definition.Replace("<>", $"<{typeArguments}>");
    }

    static string ConstructedFormatterName(
        INamedTypeSymbol target,
        ExternalUnionFactoryEntry entry)
    {
        var formatter = entry.Formatter;
        if (formatter.Arity != 0)
            formatter = formatter.Construct(target.TypeParameters.Cast<ITypeSymbol>().ToArray());
        return ExternalUnionFormatterShape.QualifyName(formatter);
    }

    readonly record struct ExternalUnionFactoryEntry(INamedTypeSymbol Formatter, INamedTypeSymbol Union);
}

internal sealed record ExternalUnionFactoryPlan(
    string DeclarationName, string TargetType, string Namespace, PartialTypeShape Target,
    EquatableArray<PartialTypeShape> ContainingTypes, string RootName, EquatableArray<ExternalUnionFactoryMatch> Matches,
    string? FallbackFormatter);

internal sealed record ExternalUnionFactoryMatch(EquatableArray<ExternalUnionTypeMatch> Arguments, string Formatter);

internal sealed record ExternalUnionTypeMatch(string Parameter, string TypeName);

internal sealed partial class CSharpEmitter
{
    internal static GeneratedSource EmitExternalUnionTargetFactory(ExternalUnionFactoryPlan factory, bool fileScopedNamespace)
    {
        var targetType = factory.TargetType;
        var sb = new StringBuilder();
        sb.AppendLine("""
// <auto-generated/>
#nullable enable
#pragma warning disable CS0162
#pragma warning disable CS0618
""");

        var ns = factory.Namespace;
        if (ns.Length != 0)
        {
            if (fileScopedNamespace)
            {
                sb.AppendLine($"namespace {ns};");
            }
            else
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }
        }

        foreach (var containingType in factory.ContainingTypes)
        {
            sb.AppendLine(EmitPartialTypeDeclaration(containingType));
            sb.AppendLine(EmitTypeParameterConstraints(containingType.TypeParameters));
            sb.AppendLine("{");
        }

        sb.AppendLine("[global::SharpPack.CompilerServices.Preserve]");
        sb.AppendLine(EmitPartialTypeDeclaration(factory.Target) + " :");
        sb.AppendLine(
            $"    global::SharpPack.ISharpPackFormatterFactory<{targetType}>");
        sb.AppendLine(EmitTypeParameterConstraints(factory.Target.TypeParameters));
        sb.AppendLine("{");
        sb.AppendLine(EmitFactoryRootField(factory.RootName, targetType, includeContextFactory: false));
        sb.AppendLine(
            $"    static global::SharpPack.SharpPackFormatter<{targetType}>");
        sb.AppendLine(
            $"        global::SharpPack.ISharpPackFormatterFactory<{targetType}>.CreateFormatter()");
        sb.AppendLine("    {");
        sb.AppendLine(EmitFactoryRootRead(factory.RootName));

        foreach (var match in factory.Matches)
        {
            var condition = string.Join(" && ", match.Arguments.Select(static argument =>
                $"typeof({argument.Parameter}) == typeof({argument.TypeName})"));

            sb.AppendLine($"        if ({condition})");
            sb.AppendLine("        {");
            sb.AppendLine(
                $"            return (global::SharpPack.SharpPackFormatter<{targetType}>)(object)new {match.Formatter}();");
            sb.AppendLine("        }");
        }

        if (factory.FallbackFormatter is { } fallback)
        {
            sb.AppendLine(
                $"        return new {fallback}();");
        }
        else
        {
            sb.AppendLine(
                $"        throw new global::SharpPack.SharpPackSerializationException(\"No generated external union formatter is available for \" + typeof({targetType}).FullName + \".\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        for (var i = 0; i < factory.ContainingTypes.Length; i++)
        {
            sb.AppendLine("}");
        }

        if (ns.Length != 0 && !fileScopedNamespace)
        {
            sb.AppendLine("}");
        }

        var hintName = factory.DeclarationName
            .Replace("global::", "")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");
        return new($"{hintName}.SharpPackExternalUnionFactory.g.cs", sb.ToString());
    }
}
