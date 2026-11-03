using System.Collections.Frozen;

namespace SharpPack;

/// <summary>A context-owned immutable capability, separate from formatter resolution.</summary>
internal sealed class TypeRegistry
{
    readonly TypeSerializationMode mode;
    readonly FrozenDictionary<string, Type> typesById;
    readonly FrozenDictionary<Type, string> idsByType;

    internal TypeRegistry(TypeSerializationMode mode, IReadOnlyDictionary<string, Type>? registrations)
    {
        this.mode = mode;
        typesById = registrations is null
            ? FrozenDictionary<string, Type>.Empty
            : registrations.ToFrozenDictionary(StringComparer.Ordinal);
        idsByType = typesById.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);
    }

    internal void EnsureEnabled()
    {
        if (mode != TypeSerializationMode.RegisteredOnly)
        {
            throw new SharpPackSerializationException("System.Type serialization is disabled for this context.");
        }
    }

    internal string GetId(Type type)
    {
        EnsureEnabled();
        return idsByType.TryGetValue(type, out var id)
            ? id
            : throw new SharpPackSerializationException("The Type is not explicitly registered in this context.");
    }

    internal Type GetType(string id)
    {
        EnsureEnabled();
        return typesById.TryGetValue(id, out var type)
            ? type
            : throw new SharpPackSerializationException("The Type ID is not explicitly registered in this context.");
    }
}
