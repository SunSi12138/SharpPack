using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Collections;
using System.Linq;
using System.IO;

namespace SharpPack.Tests;

public class BitArrayWireTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(65)]
    public void PublicLayout_RoundTripsBits_AndWritesMinimalWordCount(int length)
    {
        var value = new BitArray(Enumerable.Range(0, length).Select(i => i % 3 == 0).ToArray());
        var payload = SharpPackSerializer.Serialize(value);
        Assert.Equal(length, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1)));
        Assert.Equal((length + 31) / 32, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(5)));
        foreach (var context in new[] { new SharpPackSerializerContext(), new SharpPackSerializerContext(
            SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits { MaxCollectionLength = Math.Max(1, length) },
            }) })
        {
            var decoded = SharpPackSerializer.Deserialize<BitArray>(payload, context)!;
            Assert.Equal(length, decoded.Length);
            Assert.Equal(value.Cast<bool>(), decoded.Cast<bool>());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(65)]
    [InlineData(4097)]
    public void DirectWriter_MatchesPublicCopyToWords_AndClearsPoisonedPadding(int length)
    {
        var value = new BitArray(Enumerable.Range(0, length).Select(i => i % 3 == 0).ToArray());
        if (length % 8 != 0)
        {
            // Public AsBytes allows irrelevant high bits. Never expose them.
            CollectionsMarshal.AsBytes(value)[^1] |= (byte)~((1 << (length % 8)) - 1);
        }
        var words = new int[(length + 31) / 32];
        value.CopyTo(words, 0);
        var expected = Payload(length, words.Length, words);
        var writer = new ArrayBufferWriter<byte>();
        writer.GetSpan(expected.Length + 32).Fill(0xCC);
        SharpPackSerializer.Serialize(ref writer, value);
        Assert.Equal(expected, writer.WrittenSpan.ToArray());
        Assert.Equal(expected, SharpPackSerializer.Serialize(value));
        Assert.Equal(value.Cast<bool>(),
            SharpPackSerializer.Deserialize<BitArray>(writer.WrittenSpan)!.Cast<bool>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void DirectWriter_RequestsNewSpanAcrossHeaderAndPayloadBoundaries(int minimumChunk)
    {
        var value = new BitArray(Enumerable.Range(0, 65).Select(i => i % 3 == 0).ToArray());
        var words = new int[3];
        value.CopyTo(words, 0);
        var expected = Payload(65, words.Length, words);
        var output = new ExactChunkWriter(minimumChunk);
        SharpPackSerializer.Serialize(ref output, value);
        Assert.True(output.Requests >= 3);
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(value.Cast<bool>(),
            SharpPackSerializer.Deserialize<BitArray>(output.ToArray())!.Cast<bool>());
    }

    sealed class ExactChunkWriter(int minimumChunk) : IBufferWriter<byte>
    {
        readonly MemoryStream written = new();
        byte[] current = Array.Empty<byte>();
        public int Requests { get; private set; }
        public void Advance(int count)
        {
            Assert.InRange(count, 0, current.Length);
            written.Write(current, 0, count);
            current = Array.Empty<byte>();
        }
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Requests++;
            current = new byte[Math.Max(Math.Max(1, minimumChunk), sizeHint)];
            current.AsSpan().Fill(0xCC);
            return current;
        }
        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
        public byte[] ToArray() => written.ToArray();
    }

    [Fact]
    public void OldExtraWords_AreAcceptedButDoNotBecomeVisibleBits()
    {
        // Original corpus shape: logical 4 bits, four backing words.
        var payload = Payload(4, 4, 13, -1, -1, -1);
        var decoded = SharpPackSerializer.Deserialize<BitArray>(payload)!;
        Assert.Equal(new[] { true, false, true, true }, decoded.Cast<bool>());
        Assert.Equal(Payload(4, 1, 13), SharpPackSerializer.Serialize(decoded));
        // Word-table limits still apply even when logical cardinality is small.
        var context = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Default with
        {
            ReadLimits = new SharpPackReadLimits { MaxCollectionLength = 4 },
        });
        Assert.Equal(4, SharpPackSerializer.Deserialize<BitArray>(payload, context)!.Length);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(33, 1)]
    [InlineData(1, -1)]
    [InlineData(1, -2)]
    [InlineData(int.MaxValue, 0)]
    public void InvalidLengthOrInsufficientWords_AreRejected(int bitLength, int wordCount)
    {
        var payload = Payload(bitLength, wordCount, wordCount > 0 ? new[] { 1 } : Array.Empty<int>());
        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<BitArray>(payload));
    }

    [Fact]
    public void TruncatedWord_IsRejected_AndUnusedHighBitsAreCleared()
    {
        var payload = Payload(1, 1, -1);
        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<BitArray>(payload.AsSpan(0, payload.Length - 1)));
        var decoded = SharpPackSerializer.Deserialize<BitArray>(payload)!;
        Assert.True(decoded[0]);
        Assert.Equal(Payload(1, 1, 1), SharpPackSerializer.Serialize(decoded));
    }

    static byte[] Payload(int length, int count, params int[] words)
    {
        var bytes = new byte[9 + words.Length * 4];
        bytes[0] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), count);
        for (var i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9 + i * 4), words[i]);
        return bytes;
    }
}
