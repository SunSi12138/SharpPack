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
    internal TypeRegistry TypeRegistry { get; }
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
        bool freezeRegistrations,
        IReadOnlyDictionary<string, Type>? typeRegistrations = null)
    {
        if (configuration.TypeSerializationMode is not TypeSerializationMode.Disabled and not TypeSerializationMode.RegisteredOnly)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), "Unknown Type serialization mode.");
        }
        Configuration = configuration;
        TypeRegistry = new TypeRegistry(configuration.TypeSerializationMode, typeRegistrations);
        ArgumentOutOfRangeException.ThrowIfNegative(configuration.MaxPayloadBytes);
        readLimits = configuration.ReadLimits.Normalize();
        maxPayloadBytes = configuration.MaxPayloadBytes == 0
            ? int.MaxValue
            : configuration.MaxPayloadBytes;
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

    internal void Register<T>(SharpPackFormatter<T> formatter)
        => graph.Register(formatter);

    internal void FreezeRegistrations()
        => graph.FreezeRegistrations();
}

/// <summary>
/// Mutable startup-only builder for an immutable serializer context.
/// </summary>
public sealed class SharpPackSerializerContextBuilder
{
    readonly List<Action<SharpPackSerializerContext>> registrations = [];
    SharpPackSerializerConfiguration configuration = SharpPackSerializerConfiguration.Default;
    readonly Dictionary<string, Type> typesById = new(StringComparer.Ordinal);
    readonly HashSet<Type> registeredTypes = [];
    bool built;

    public SharpPackSerializerContextBuilder Configure(
        SharpPackSerializerConfiguration value)
    {
        ThrowIfBuilt();
        configuration = value;
        return this;
    }

    /// <summary>Enables explicitly registered System.Type payloads for this context.</summary>
    public SharpPackSerializerContextBuilder EnableTypeSerialization()
    {
        ThrowIfBuilt();
        configuration = configuration with { TypeSerializationMode = TypeSerializationMode.RegisteredOnly };
        return this;
    }

    public SharpPackSerializerContextBuilder RegisterType<T>(string typeId)
        => RegisterType(typeof(T), typeId);

    /// <summary>Registers a stable, ordinal Type ID without enabling Type serialization.</summary>
    public SharpPackSerializerContextBuilder RegisterType(Type type, string typeId)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        if (typesById.ContainsKey(typeId))
        {
            throw new ArgumentException("The Type ID is already registered.", nameof(typeId));
        }
        if (registeredTypes.Contains(type))
        {
            throw new ArgumentException("The Type is already registered.", nameof(type));
        }
        typesById.Add(typeId, type);
        registeredTypes.Add(type);
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
                freezeRegistrations: false,
                typeRegistrations: typesById);
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
            typesById.Clear();
            registeredTypes.Clear();
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
