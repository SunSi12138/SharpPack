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
    readonly TypeResolutionPolicy typeResolution;
    readonly SharpPackReadLimits readLimits;
    readonly int maxPayloadBytes;

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
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxPayloadBytes);

        Configuration = configuration;
        readLimits = configuration.ReadLimits.Normalize();
        maxPayloadBytes = configuration.MaxPayloadBytes == 0
            ? int.MaxValue
            : configuration.MaxPayloadBytes;
        typeResolution = new TypeResolutionPolicy(configuration.TypeResolutionMode);
        graph = new FormatterGraph(this);
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

    internal SharpPackReadLimits ReadLimits => readLimits;

    internal int MaxPayloadBytes => maxPayloadBytes;

    internal void EnsureRootType<T>()
        => ContextRootTypeRegistration<T>.Ensure(this);

    internal void Register<T>(SharpPackFormatter<T> formatter)
    {
        graph.Register(formatter);
        AddType(typeof(T));
    }

    internal void FreezeRegistrations()
        => graph.FreezeRegistrations();

    internal void AddType(Type type)
        => typeResolution.AddType(type);

    internal void ObserveSerializedType(Type type)
        => typeResolution.ObserveSerializedType(type);

    internal Type? ResolveType(string typeName)
        => typeResolution.ResolveType(typeName);

    internal void ThrowIfTypePayloadDisabled()
        => typeResolution.ThrowIfTypePayloadDisabled();
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

        try
        {
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
        finally
        {
            registrations.Clear();
        }
    }

    void ThrowIfBuilt()
    {
        if (built)
        {
            throw new InvalidOperationException("This serializer context builder has already been built.");
        }
    }
}
