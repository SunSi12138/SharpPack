using SharpPack.Internal;
using System.Runtime.CompilerServices;

namespace SharpPack.Formatters;

[Preserve]
public sealed class NullableFormatter<T> : SharpPackFormatter<T?>
    where T : struct
{
    // Nullable<T> is sometimes serialized on UnmanagedFormatter.
    // to keep same result, check if type is unmanaged.

    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !writer.HasFormatterOverride<T>())
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        if (!value.HasValue)
        {
            writer.WriteNullObjectHeader();
            return;
        }
        else
        {
            writer.WriteObjectHeader(1);
        }

        writer.WriteValue(value.Value);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !reader.HasFormatterOverride<T>())
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1) SharpPackSerializationException.ThrowInvalidPropertyCount(1, count);

        value = reader.ReadValue<T>();
    }
}
