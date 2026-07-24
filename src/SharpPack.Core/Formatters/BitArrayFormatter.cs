using SharpPack.Internal;
using System.Collections;
using System.Runtime.CompilerServices;

namespace SharpPack.Formatters;

[Preserve]
public sealed class BitArrayFormatter : SharpPackFormatter<BitArray>
{
    // serialize [m_length, m_array]

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref BitArray? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        ref var view = ref Unsafe.As<BitArray, BitArrayView>(ref value);

        writer.WriteUnmanagedWithObjectHeader(2, view.m_length);
        writer.WriteUnmanagedArray(view.m_array);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref BitArray? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 2) SharpPackSerializationException.ThrowInvalidPropertyCount(2, count);

        reader.ReadUnmanaged(out int length);

        if (length < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(length);
        }

        var words = reader.ReadUnmanagedArray<int>();
        var requiredWordCount = (int)(((long)length + 31) / 32);
        if (words is null || words.Length < requiredWordCount)
        {
            SharpPackSerializationException.ThrowMessage(
                $"BitArray length {length} requires at least {requiredWordCount} words.");
        }

        var bitArray = new BitArray(length, false);
        ref var view = ref Unsafe.As<BitArray, BitArrayView>(ref bitArray);
        words.AsSpan(0, requiredWordCount).CopyTo(view.m_array);

        value = bitArray;
    }
}

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
[Preserve]
internal class BitArrayView
{
    public int[] m_array;
    public int m_length;
    public int _version;
}
