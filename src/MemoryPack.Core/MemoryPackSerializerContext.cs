using System.Reflection;
using System.Runtime.CompilerServices;
using MemoryPack.Formatters;

namespace MemoryPack;

/// <summary>
/// Immutable configuration and lifecycle boundary for a formatter graph.
/// </summary>
public sealed class MemoryPackSerializerContext
{
    readonly FormatterGraph graph;
    readonly Dictionary<string, List<Assembly>> assemblies =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Lock assemblyLock = new();

    public MemoryPackSerializerConfiguration Configuration { get; }

    public MemoryPackSerializerContext()
        : this(MemoryPackSerializerConfiguration.Default)
    {
    }

    public MemoryPackSerializerContext(MemoryPackSerializerConfiguration configuration)
        : this(configuration, freezeRegistrations: true)
    {
    }

    internal MemoryPackSerializerContext(
        MemoryPackSerializerConfiguration configuration,
        bool freezeRegistrations)
    {
        Configuration = configuration;
        graph = new FormatterGraph(this);

        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(MemoryPackSerializerContext).Assembly);
        if (freezeRegistrations)
        {
            graph.FreezeRegistrations();
        }
    }

    public MemoryPackFormatter<T> GetFormatter<T>()
        => !graph.HasRegistrations &&
            !FormatterTypeTraits<T>.ContainsCollectibleType
            ? FormatterSlot<T>.Formatter
            : graph.GetFormatter<T>();

    internal FormatterGraph? OverrideGraph
        => graph.HasRegistrations ? graph : null;

    internal FormatterGraph Graph => graph;

    internal void EnsureRootType<T>()
        => ContextRootTypeRegistration<T>.Ensure(this);

    internal void Register<T>(MemoryPackFormatter<T> formatter)
    {
        graph.Register(formatter);
        AddType(typeof(T));
    }

    internal void FreezeRegistrations()
        => graph.FreezeRegistrations();

    internal Assembly? ResolveAssembly(AssemblyName name)
    {
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
                    return null;
                }
                match = candidate;
            }
            return match;
        }
    }

    internal void AddType(Type type)
    {
        lock (assemblyLock)
        {
            AddTypeCore(type);
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
        if (name is not null)
        {
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
}

/// <summary>
/// Mutable startup-only builder for an immutable serializer context.
/// </summary>
public sealed class MemoryPackSerializerContextBuilder
{
    readonly List<Action<MemoryPackSerializerContext>> registrations = [];
    MemoryPackSerializerConfiguration configuration = MemoryPackSerializerConfiguration.Default;
    bool built;

    public MemoryPackSerializerContextBuilder Configure(
        MemoryPackSerializerConfiguration value)
    {
        ThrowIfBuilt();
        configuration = value;
        return this;
    }

    public MemoryPackSerializerContextBuilder Register<T>(MemoryPackFormatter<T> formatter)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(formatter);
        registrations.Add(context => context.Register(formatter));
        return this;
    }

    public MemoryPackSerializerContextBuilder RegisterFactory<T, TFactory>()
        where TFactory : IMemoryPackFormatterFactory<T>
        => Register<T>(TFactory.CreateFormatter());

    public MemoryPackSerializerContextBuilder RegisterCollection<TCollection, TElement>()
        where TCollection : ICollection<TElement?>, new()
        => Register(new GenericCollectionFormatter<TCollection, TElement>());

    public MemoryPackSerializerContextBuilder RegisterSet<TSet, TElement>()
        where TSet : ISet<TElement?>, new()
        => Register(new GenericSetFormatter<TSet, TElement>());

    public MemoryPackSerializerContextBuilder RegisterDictionary<TDictionary, TKey, TValue>()
        where TKey : notnull
        where TDictionary : IDictionary<TKey, TValue?>, new()
        => Register(new GenericDictionaryFormatter<TDictionary, TKey, TValue>());

    public MemoryPackSerializerContext Build()
    {
        ThrowIfBuilt();
        built = true;

        var context = new MemoryPackSerializerContext(
            configuration,
            freezeRegistrations: false);
        foreach (var registration in registrations)
        {
            registration(context);
        }
        context.FreezeRegistrations();
        return context;
    }

    void ThrowIfBuilt()
    {
        if (built)
        {
            throw new InvalidOperationException("This serializer context builder has already been built.");
        }
    }
}
