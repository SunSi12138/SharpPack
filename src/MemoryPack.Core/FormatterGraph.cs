namespace MemoryPack;

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

    internal FormatterGraph(MemoryPackSerializerContext owner)
    {
        Owner = owner;
    }

    internal MemoryPackSerializerContext Owner { get; }
    internal bool HasRegistrations => hasRegistrations;

    internal void Register<T>(MemoryPackFormatter<T> formatter)
    {
        if (registrationsFrozen)
        {
            throw new InvalidOperationException(
                "Formatter registrations are frozen after the context is built.");
        }

        ContextFormatterSlot<T>.Register(this, formatter);
        hasRegistrations = true;
    }

    internal MemoryPackFormatter<T> GetFormatter<T>()
        => ContextFormatterSlot<T>.Get(this);

    internal void FreezeRegistrations()
        => registrationsFrozen = true;
}
