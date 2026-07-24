using SharpPack.Internal;
using System.Diagnostics.CodeAnalysis;

namespace SharpPack.Formatters;

[Preserve]
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2091",
    Justification = "Lazy<T> is constructed from an already deserialized value and does not require T's constructor.")]
public sealed class LazyFormatter<T> : SharpPackFormatter<Lazy<T?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Lazy<T?>? value)
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
    public override void Deserialize(ref SharpPackReader reader, scoped ref Lazy<T?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1) SharpPackSerializationException.ThrowInvalidPropertyCount(1, count);

        var v = reader.ReadValue<T>();
        value = new Lazy<T?>(v);
    }
}
