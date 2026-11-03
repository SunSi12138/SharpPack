using SharpPack.Internal;

namespace SharpPack.Formatters;

[Preserve]
public sealed class TypeFormatter : SharpPackFormatter<Type>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Type? value)
    {
        var registry = RequireRegistry(writer.OptionalState.SerializerContext);
        // Resolve before writing so an unknown Type cannot emit a partial Type payload.
        writer.WriteString(value is null ? null : registry.GetId(value));
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Type? value)
    {
        var registry = RequireRegistry(reader.OptionalState.SerializerContext);
        var id = reader.ReadString();
        value = id is null ? null : registry.GetType(id);
    }

    static TypeRegistry RequireRegistry(SharpPackSerializerContext? context)
    {
        if (context is null)
        {
            throw new SharpPackSerializationException("System.Type serialization requires an explicitly enabled context.");
        }
        context.TypeRegistry.EnsureEnabled();
        return context.TypeRegistry;
    }
}
