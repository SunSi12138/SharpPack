namespace SharpPack;

/// <summary>
/// Lifecycle token for a context-owned, distributed generic formatter graph.
/// Formatter instances are stored in <see cref="ContextFormatterSlot{T}"/> and
/// weakly keyed by this graph, so the whole graph is collectible with its
/// owning context without a process-wide Type dictionary.
/// </summary>
internal sealed class FormatterGraph
{
    bool registrationsFrozen;
    bool hasRegistrations;
    readonly Lock formatterCreationLock = new();
    readonly HashSet<Type> creatingFormatterTypes = [];

    internal FormatterGraph(SharpPackSerializerContext owner)
    {
        Owner = owner;
    }

    internal SharpPackSerializerContext Owner { get; }
    internal bool HasRegistrations => hasRegistrations;

    internal void Register<T>(SharpPackFormatter<T> formatter)
    {
        if (registrationsFrozen)
        {
            throw new InvalidOperationException(
                "Formatter registrations are frozen after the context is built.");
        }

        ContextFormatterSlot<T>.Register(this, formatter);
        hasRegistrations = true;
    }

    internal SharpPackFormatter<T> GetFormatter<T>()
    {
        if (ContextFormatterSlot<T>.TryGet(this, out var formatter))
        {
            return formatter;
        }

        lock (formatterCreationLock)
        {
            return GetFormatterLocked<T>();
        }
    }

    SharpPackFormatter<T> GetFormatterLocked<T>()
    {
        if (ContextFormatterSlot<T>.TryGet(this, out var formatter))
        {
            return formatter;
        }

        var type = typeof(T);
        if (!creatingFormatterTypes.Add(type))
        {
            return FormatterSlot<T>.Formatter;
        }

        try
        {
            return ContextFormatterSlot<T>.Get(this);
        }
        finally
        {
            creatingFormatterTypes.Remove(type);
        }
    }

    internal bool HasExplicitRegistration<T>()
        => ContextFormatterSlot<T>.HasExplicitRegistration(this);

    internal bool HasFormatterOverride<T>()
    {
        if (HasExplicitRegistration<T>())
        {
            return true;
        }

        lock (formatterCreationLock)
        {
            // A recursive formatter dependency is selected conservatively.
            // The context-only formatter pays the override-aware path while
            // the default formatter remains branch-free.
            if (creatingFormatterTypes.Contains(typeof(T)))
            {
                return true;
            }

            return GetFormatterLocked<T>()
                .HasFormatterOverrideDependency(this);
        }
    }

    internal void FreezeRegistrations()
        => registrationsFrozen = true;
}
