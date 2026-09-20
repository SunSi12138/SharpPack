using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpPack;

internal sealed class TypeResolutionPolicy
{
    readonly ContextTypeCatalog catalog = new();
    readonly TypeResolutionMode mode;

    internal TypeResolutionPolicy(TypeResolutionMode mode)
    {
        if (!Enum.IsDefined(typeof(TypeResolutionMode), mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        this.mode = mode;
        catalog.AddType(typeof(object));
        catalog.AddType(typeof(SharpPackSerializerContext));
    }

    internal void AddType(Type type)
        => catalog.AddType(type);

    internal void ObserveSerializedType(Type type)
    {
        ThrowIfTypePayloadDisabled();

        if (mode == TypeResolutionMode.GlobalCompatibility)
        {
            catalog.AddType(type);
        }
    }

    internal void ThrowIfTypePayloadDisabled()
    {
        if (mode == TypeResolutionMode.Disabled)
        {
            SharpPackSerializationException.ThrowMessage(
                "Serialized Type payloads are disabled for this serializer context.");
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    internal Type? ResolveType(string typeName)
    {
        ThrowIfTypePayloadDisabled();

        var ambiguousAssembly = false;
        var value = Type.GetType(
            typeName,
            name =>
            {
                var assembly = catalog.ResolveAssembly(name, out var ambiguous);
                ambiguousAssembly |= ambiguous;
                return assembly;
            },
            static (assembly, name, ignoreCase) =>
                assembly?.GetType(
                    name,
                    throwOnError: false,
                    ignoreCase),
            throwOnError: false);

        if (ambiguousAssembly)
        {
            return null;
        }

        if (value is not null)
        {
            return value;
        }

        if (mode == TypeResolutionMode.ContextCatalogOnly)
        {
            return null;
        }

        value = Type.GetType(typeName, throwOnError: false);
        if (value is not null)
        {
            catalog.AddType(value);
        }

        return value;
    }
}

internal sealed class ContextTypeCatalog
{
    readonly Dictionary<string, List<Assembly>> assemblies =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Lock assemblyLock = new();

    internal void AddType(Type type)
    {
        lock (assemblyLock)
        {
            AddTypeCore(type);
        }
    }

    internal Assembly? ResolveAssembly(
        AssemblyName name,
        out bool ambiguous)
    {
        ambiguous = false;
        if (name.Name is null)
        {
            return null;
        }

        lock (assemblyLock)
        {
            if (!assemblies.TryGetValue(name.Name, out var candidates))
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            Assembly? match = null;
            foreach (var candidate in candidates)
            {
                if (!AssemblyName.ReferenceMatchesDefinition(
                        candidate.GetName(),
                        name))
                {
                    continue;
                }

                if (match is not null)
                {
                    ambiguous = true;
                    return null;
                }

                match = candidate;
            }

            return match;
        }
    }

    void AddTypeCore(Type type)
    {
        AddAssembly(type.Assembly);
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            AddTypeCore(element);
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                AddTypeCore(argument);
            }
        }
    }

    void AddAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name is null)
        {
            return;
        }

        if (!assemblies.TryGetValue(name, out var candidates))
        {
            assemblies.Add(name, [assembly]);
        }
        else if (!candidates.Contains(assembly))
        {
            candidates.Add(assembly);
        }
    }
}
