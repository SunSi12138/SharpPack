using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace SharpPack.Generator;

public enum SharpPackFormatterSupport
{
    Existing,
    GenerateExternal,
    Unsupported,
}

public readonly struct SharpPackSidecarRoot(ITypeSymbol type, Location? location = null)
{
    public ITypeSymbol Type { get; } = type;
    public Location? Location { get; } = location;
}

public readonly struct SharpPackSidecarWireMember
{
    internal SharpPackSidecarWireMember(int position, string name, string typeName)
        => (Position, Name, TypeName) = (position, name, typeName);

    public int Position { get; }
    public string Name { get; }
    public string TypeName { get; }
}

/// <summary>The ordinary object wire shape of a generated sidecar.</summary>
public sealed class SharpPackSidecarWireShape
{
    internal SharpPackSidecarWireShape(string typeName, bool isValueType, ImmutableArray<SharpPackSidecarWireMember> members)
        => (TypeName, IsValueType, Members) = (typeName, isValueType, members);

    public string TypeName { get; }
    public bool IsValueType { get; }
    public ImmutableArray<SharpPackSidecarWireMember> Members { get; }
}

/// <summary>A root decision and the sidecar wire shapes reachable from it.</summary>
public sealed class SharpPackSidecarRootResult
{
    internal SharpPackSidecarRootResult(
        ITypeSymbol type, SharpPackFormatterSupport support, ImmutableArray<SharpPackSidecarWireShape> sidecars)
        => (Type, Support, Sidecars) = (type, support, sidecars);

    public ITypeSymbol Type { get; }
    public SharpPackFormatterSupport Support { get; }
    public ImmutableArray<SharpPackSidecarWireShape> Sidecars { get; }
}

public sealed class SharpPackSidecarResult
{
    internal SharpPackSidecarResult(
        string hintName, string? source, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<SharpPackSidecarRootResult> roots)
        => (HintName, Source, Diagnostics, Roots) = (hintName, source, diagnostics, roots);

    public string HintName { get; }
    public string? Source { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public ImmutableArray<SharpPackSidecarRootResult> Roots { get; }
}

/// <summary>Compiles explicit formatter roots in the calling generator's original C# compilation.</summary>
public static class SharpPackSidecarGenerator
{
    /// <summary>
    /// Produces one source file defining <c>SharpPack.Generated.{providerName}.Register(builder)</c>.
    /// Register that provider while building the serializer context.
    /// </summary>
    /// <param name="compilation">The original compilation, with SharpPack.Core referenced.</param>
    /// <param name="roots">The closed payload types owned by this provider.</param>
    /// <param name="providerName">A C# identifier unique within SharpPack.Generated for this integration.</param>
    /// <param name="registeredFormatterTypes">Opaque nodes provided explicitly by the integration at context construction.</param>
    /// <param name="cancellationToken">Cancels compile-time graph traversal.</param>
    public static SharpPackSidecarResult Generate(
        Compilation compilation, ImmutableArray<SharpPackSidecarRoot> roots, string providerName,
        ImmutableArray<ITypeSymbol> registeredFormatterTypes = default, CancellationToken cancellationToken = default)
    {
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));
        if (providerName is null || !SyntaxFacts.IsValidIdentifier(providerName))
            throw new ArgumentException("The provider name must be a C# identifier.", nameof(providerName));
        var hintName = $"SharpPack.Sidecars.{providerName}.g.cs";
        if (roots.IsDefaultOrEmpty)
            return new(hintName, null, ImmutableArray<Diagnostic>.Empty, ImmutableArray<SharpPackSidecarRootResult>.Empty);

        if (RuntimeGeneratedCodeAbi.GetUnsupportedVersion(compilation) is { } abi)
        {
            return new(hintName, null,
                ImmutableArray.Create(Diagnostic.Create(DiagnosticDescriptors.IncompatibleRuntimeAbi, roots[0].Location, abi,
                    RuntimeGeneratedCodeAbi.MinimumVersion, RuntimeGeneratedCodeAbi.MaximumVersion)),
                roots.Select(static root => new SharpPackSidecarRootResult(root.Type, SharpPackFormatterSupport.Unsupported,
                        ImmutableArray<SharpPackSidecarWireShape>.Empty))
                    .ToImmutableArray());
        }

        var analyzer = new SidecarGraph(compilation, registeredFormatterTypes.IsDefault ? Array.Empty<ITypeSymbol>() : registeredFormatterTypes);
        var decisions = ImmutableArray.CreateBuilder<SharpPackSidecarRootResult>(roots.Length);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var allNodes = new Dictionary<ITypeSymbol, FormatterNode>(SymbolEqualityComparer.Default);
        var wireShapes = new Dictionary<ITypeSymbol, SharpPackSidecarWireShape>(SymbolEqualityComparer.Default);
        foreach (var root in roots)
        {
            var (nodes, failure) = analyzer.Walk(root.Type, cancellationToken);
            if (failure is not null)
            {
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedSidecarRoot, root.Location,
                    root.Type.FullyQualifiedToString(), failure.Path, failure.Reason));
                decisions.Add(new(root.Type, SharpPackFormatterSupport.Unsupported, ImmutableArray<SharpPackSidecarWireShape>.Empty));
                continue;
            }

            var shapes = ImmutableArray.CreateBuilder<SharpPackSidecarWireShape>();
            foreach (var node in nodes.OrderBy(static node => node.Type.FullyQualifiedToString(), StringComparer.Ordinal))
            {
                allNodes[node.Type] = node;
                if (node.Sidecar is not { } plan) continue;
                if (!wireShapes.TryGetValue(node.Type, out var shape))
                {
                    shape = new(plan.Shape.TargetName, plan.Shape.IsValueType,
                        plan.WireSlots.Select(slot =>
                        {
                            var member = plan.Shape.Members[slot.DeclaredMemberIndex!.Value];
                            return new SharpPackSidecarWireMember(slot.Position, member.Name, member.TypeName);
                        }).ToImmutableArray());
                    wireShapes.Add(node.Type, shape);
                }
                shapes.Add(shape);
            }
            decisions.Add(new(root.Type,
                nodes[0].Sidecar is null ? SharpPackFormatterSupport.Existing : SharpPackFormatterSupport.GenerateExternal,
                shapes.ToImmutable()));
        }

        var source = diagnostics.Count == 0 ? CSharpEmitter.EmitSidecarProvider(providerName,
            allNodes.Values.OrderBy(static node => node.Type.FullyQualifiedToString(), StringComparer.Ordinal).ToArray()) : null;
        return new(hintName, source, diagnostics.ToImmutable(), decisions.MoveToImmutable());
    }
}
