using SharpPack.Internal;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpPack.Formatters;

[Preserve]
public static class KeyValuePairFormatter
{
    // for Dictionary serialization

    [Preserve]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Serialize<TKey, TValue, TBufferWriter>(ISharpPackFormatter<TKey> keyFormatter, ISharpPackFormatter<TValue> valueFormatter, ref SharpPackWriter<TBufferWriter> writer, KeyValuePair<TKey?, TValue?> value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<TKey?, TValue?>>() &&
            !writer.HasFormatterOverride<TKey>() &&
            !writer.HasFormatterOverride<TValue>())
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        value.Deconstruct(out var k, out var v);
        keyFormatter.Serialize(ref writer, ref k);
        valueFormatter.Serialize(ref writer, ref v);
    }

    [Preserve]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Deserialize<TKey, TValue>(ISharpPackFormatter<TKey> keyFormatter, ISharpPackFormatter<TValue> valueFormatter, ref SharpPackReader reader, out TKey? key, out TValue? value)
    {
        using var entry = EnterReadScope<TKey, TValue>(ref reader, 1, out var unmanaged);
        DeserializeEntry(keyFormatter, valueFormatter, ref reader, unmanaged, out key, out value);
    }

    // Dictionary formatters choose the entry representation and enter its
    // depth scope once, before allocating or mutating the destination.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static SharpPackReader.CollectionDepthScope EnterReadScope<TKey, TValue>(ref SharpPackReader reader, int length, out bool unmanaged)
    {
        unmanaged = length != 0 &&
            !RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<TKey?, TValue?>>() &&
            !reader.HasFormatterOverride<TKey>() && !reader.HasFormatterOverride<TValue>();
        return reader.EnterCollectionDepth<KeyValuePair<TKey, TValue>>(length != 0 && !unmanaged);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void DeserializeEntry<TKey, TValue>(ISharpPackFormatter<TKey> keyFormatter, ISharpPackFormatter<TValue> valueFormatter,
        ref SharpPackReader reader, bool unmanaged, out TKey? key, out TValue? value)
    {
        if (unmanaged)
        {
            reader.DangerousReadUnmanaged(out KeyValuePair<TKey?, TValue?> kvp);
            key = kvp.Key;
            value = kvp.Value;
            return;
        }

        key = default;
        value = default;
        keyFormatter.Deserialize(ref reader, ref key);
        valueFormatter.Deserialize(ref reader, ref value);
    }
}
[Preserve]
public sealed class KeyValuePairFormatter<TKey, TValue> : SharpPackFormatter<KeyValuePair<TKey?, TValue?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<TKey>() ||
           graph.HasFormatterOverride<TValue>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref KeyValuePair<TKey?, TValue?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<TKey?, TValue?>>() &&
            !writer.HasFormatterOverride<TKey>() &&
            !writer.HasFormatterOverride<TValue>())
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Key);
        writer.WriteValue(value.Value);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref KeyValuePair<TKey?, TValue?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<KeyValuePair<TKey?, TValue?>>() &&
            !reader.HasFormatterOverride<TKey>() &&
            !reader.HasFormatterOverride<TValue>())
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new KeyValuePair<TKey?, TValue?>(
            reader.ReadValue<TKey>(),
            reader.ReadValue<TValue>()
        );
    }
}
