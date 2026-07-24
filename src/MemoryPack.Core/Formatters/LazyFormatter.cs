using MemoryPack.Internal;
using System.Diagnostics.CodeAnalysis;

namespace MemoryPack.Formatters;

[Preserve]
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2091",
    Justification = "Lazy<T> is constructed from an already deserialized value and does not require T's constructor.")]
public sealed class LazyFormatter<T> : MemoryPackFormatter<Lazy<T?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Lazy<T?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteValue(value.Value);
    }

    [Preserve]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref Lazy<T?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1) MemoryPackSerializationException.ThrowInvalidPropertyCount(1, count);

        var v = reader.ReadValue<T>();
        value = new Lazy<T?>(v);
    }
}
