using System.Reflection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharpPack.Formatters;

namespace SharpPack;

/// <summary>
/// Immutable configuration and lifecycle boundary for a formatter graph.
/// </summary>
public sealed class SharpPackSerializerContext
{
    readonly FormatterGraph graph;
    readonly Dictionary<string, List<Assembly>> assemblies =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Lock assemblyLock = new();

    public SharpPackSerializerConfiguration Configuration { get; }

    public SharpPackSerializerContext()
        : this(SharpPackSerializerConfiguration.Default)
    {
    }

    public SharpPackSerializerContext(SharpPackSerializerConfiguration configuration)
        : this(configuration, freezeRegistrations: true)
    {
    }

    internal SharpPackSerializerContext(
        SharpPackSerializerConfiguration configuration,
        bool freezeRegistrations)
    {
        Configuration = configuration;
        graph = new FormatterGraph(this);

        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(SharpPackSerializerContext).Assembly);
        if (freezeRegistrations)
        {
            graph.FreezeRegistrations();
        }
    }

    public SharpPackFormatter<T> GetFormatter<T>()
        => !graph.HasRegistrations &&
            !FormatterTypeTraits<T>.ContainsCollectibleType
            ? FormatterSlot<T>.Formatter
            : graph.GetFormatter<T>();

    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFormatterOverrideDependency<T>()
        => graph.HasFormatterOverride<T>();

    internal FormatterGraph? OverrideGraph
        => graph.HasRegistrations ? graph : null;

    internal FormatterGraph Graph => graph;

    internal void EnsureRootType<T>()
        => ContextRootTypeRegistration<T>.Ensure(this);

    internal void Register<T>(SharpPackFormatter<T> formatter)
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
public sealed class SharpPackSerializerContextBuilder
{
    readonly List<Action<SharpPackSerializerContext>> registrations = [];
    SharpPackSerializerConfiguration configuration = SharpPackSerializerConfiguration.Default;
    bool built;

    public SharpPackSerializerContextBuilder Configure(
        SharpPackSerializerConfiguration value)
    {
        ThrowIfBuilt();
        configuration = value;
        return this;
    }

    public SharpPackSerializerContextBuilder Register<T>(SharpPackFormatter<T> formatter)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(formatter);
        registrations.Add(context => context.Register(formatter));
        return this;
    }

    public SharpPackSerializerContextBuilder RegisterFactory<T, TFactory>()
        where TFactory : ISharpPackFormatterFactory<T>
        => Register<T>(TFactory.CreateFormatter());

    public SharpPackSerializerContextBuilder RegisterCollection<TCollection, TElement>()
        where TCollection : ICollection<TElement?>, new()
        => Register(new GenericCollectionFormatter<TCollection, TElement>());

    public SharpPackSerializerContextBuilder RegisterSet<TSet, TElement>()
        where TSet : ISet<TElement?>, new()
        => Register(new GenericSetFormatter<TSet, TElement>());

    public SharpPackSerializerContextBuilder RegisterDictionary<TDictionary, TKey, TValue>()
        where TKey : notnull
        where TDictionary : IDictionary<TKey, TValue?>, new()
        => Register(new GenericDictionaryFormatter<TDictionary, TKey, TValue>());

    public SharpPackSerializerContext Build()
    {
        ThrowIfBuilt();
        built = true;

        var context = new SharpPackSerializerContext(
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
