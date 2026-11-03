using SharpPack.Internal;
using System.Collections;
using System;
using System.Runtime.InteropServices;

namespace SharpPack.Formatters;

[Preserve]
public sealed class BitArrayFormatter : SharpPackFormatter<BitArray>
{
    // Wire: [logical bit length, int32 words]. Do not depend on BCL field layout.

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref BitArray? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        var length = value.Length;
        var wordCount = (int)(((long)length + 31) / 32);
        writer.WriteUnmanagedWithObjectHeader(2, length);
        writer.WriteCollectionHeader(wordCount);
        if (wordCount == 0) return;

        var byteCount = checked(wordCount * sizeof(int));
        var destination = MemoryMarshal.CreateSpan(
            ref writer.GetSpanReference(byteCount), byteCount);
        var source = CollectionsMarshal.AsBytes(value);
        source.CopyTo(destination);
        destination.Slice(source.Length).Clear();
        if (length % 8 != 0)
        {
            destination[source.Length - 1] &= (byte)((1 << (length % 8)) - 1);
        }
        // BitArray exposes low-order bytes first. Unmanaged int words retain
        // the existing native-endian wire convention on every architecture.
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < byteCount; i += sizeof(int))
            {
                destination.Slice(i, sizeof(int)).Reverse();
            }
        }
        writer.Advance(byteCount);
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

        reader.ThrowIfCollectionLimitExceeded(length);
        var words = reader.ReadUnmanagedArray<int>();
        var requiredWordCount = (int)(((long)length + 31) / 32);
        if (words is null || words.Length < requiredWordCount)
        {
            SharpPackSerializationException.ThrowMessage(
                $"BitArray length {length} requires at least {requiredWordCount} words.");
        }

        var bitArray = new BitArray(length, false);
        var bytes = CollectionsMarshal.AsBytes(bitArray);
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(words.AsSpan(0, requiredWordCount))
                .Slice(0, bytes.Length).CopyTo(bytes);
        }
        else
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((uint)words[i / 4] >> ((i % 4) * 8));
            }
        }
        if (length % 8 != 0)
        {
            bytes[^1] &= (byte)((1 << (length % 8)) - 1);
        }

        value = bitArray;
    }
}
