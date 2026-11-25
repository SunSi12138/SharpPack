using SharpPack.Compression;
using SharpPack.Formatters;
using SharpPack.Tests.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class CompressionTest
{
    [Fact]
    public async Task CompressDecompress()
    {
        // pattern1, huge compression
        var pattern1 = Enumerable.Range(1, 1000).Select(_ => string.Concat(Enumerable.Repeat("http://", 1000)))
            .Prepend("hogehogehugahugahugahugahogehoge!")
            .ToArray();

        // pattern2, small compression
        var pattern2 = new string[] { "a", "b", "c" };

        var texts = new[] { pattern1, pattern2 };
        foreach (var text in texts)
        {
            var brotli = new BrotliCompressor();

            SharpPackSerializer.Serialize(ref brotli, text);

            var originalSerialized = SharpPackSerializer.Serialize(text);

            var array1 = brotli.ToArray();

            var arrayWriter = new ArrayBufferWriter<byte>();
            brotli.CopyTo(arrayWriter);

            var array2 = arrayWriter.WrittenMemory;

            // check BrotliCompressor ToArray()/CopyTo returns same result.
            array1.AsSpan().SequenceEqual(array2.Span).Should().BeTrue();

            var stream = new MemoryStream();
            await brotli.CopyToAsync(stream);
            stream.ToArray().AsSpan().SequenceEqual(array2.Span).Should().BeTrue();

            using var decompressor = new BrotliDecompressor();

            var decompressed = decompressor.Decompress(array1);

            var referenceDecompress = ReferenceDecompress(array1);
            var decompressedArray = decompressed.ToArray();

            // check decompress results correct
            referenceDecompress.SequenceEqual(decompressedArray).Should().BeTrue();

            originalSerialized.AsSpan().SequenceEqual(decompressed.ToArray()).Should().BeTrue();

            // deserialized check
            var more = SharpPackSerializer.Deserialize<string[]>(decompressed);

            text.Length.Should().Be(more!.Length);
            foreach (var (first, second) in text.Zip(more))
            {
                first.AsSpan().SequenceEqual(second).Should().BeTrue();
            }

            brotli.Dispose();
        }
    }

    [Fact]
    public void AttributeCompression()
    {

        // pattern1, huge compression
        var pattern1 = Enumerable.Range(1, 1000).Select(_ => string.Concat(Enumerable.Repeat("http://", 1000)))
            .Prepend("hogehogehugahugahugahugahogehoge!")
            .ToArray();

        // pattern2, small compression
        var pattern2 = new string[] { "a", "b", "c" };

        foreach (var pattern in new[] { pattern1, pattern2 })
        {
            var data = new CompressionAttrData()
            {
                Id1 = 14141,
                Data = Encoding.UTF8.GetBytes(string.Concat(pattern)),
                String = string.Concat(pattern),
                Id2 = 99999
            };

            var bin = SharpPackSerializer.Serialize(data);
            var v2 = SharpPackSerializer.Deserialize<CompressionAttrData>(bin)!;

            v2.Id1.Should().Be(data.Id1);
            v2.Id2.Should().Be(data.Id2);
            v2.Data.Should().Equal(data.Data);
            v2.String.Should().Be(data.String);
        }
    }


    [Fact]
    public void AttributeCompression2()
    {

        // pattern1, huge compression
        var pattern1 = Enumerable.Range(1, 1000).Select(_ => string.Concat(Enumerable.Repeat("http://", 1000)))
            .Prepend("hogehogehugahugahugahugahogehoge!")
            .ToArray();

        // pattern2, small compression
        var pattern2 = new string[] { "a", "b", "c" };

        foreach (var pattern in new[] { pattern1, pattern2 })
        {
            var data = new CompressionAttrData2()
            {
                Id1 = 14141,
                Data = Encoding.UTF8.GetBytes(string.Concat(pattern)),
                Two = new StandardTypeTwo { One = 9999, Two = 1111 },
                String = string.Concat(pattern),
                Id2 = 99999
            };

            var bin = SharpPackSerializer.Serialize(data);

            {
                var v2 = SharpPackSerializer.Deserialize<CompressionAttrData2>(bin)!;

                v2.Id1.Should().Be(data.Id1);
                v2.Id2.Should().Be(data.Id2);
                v2.Data.Should().Equal(data.Data);
                v2.String.Should().Be(data.String);

                v2.Two.One.Should().Be(data.Two.One);
                v2.Two.Two.Should().Be(data.Two.Two);
            }
            {
                var seq = ReadOnlySequenceBuilder.Create(bin.Chunk(bin.Length / 5).ToArray());

                var v2 = SharpPackSerializer.Deserialize<CompressionAttrData2>(seq)!;

                v2.Id1.Should().Be(data.Id1);
                v2.Id2.Should().Be(data.Id2);
                v2.Data.Should().Equal(data.Data);
                v2.String.Should().Be(data.String);

                v2.Two.One.Should().Be(data.Two.One);
                v2.Two.Two.Should().Be(data.Two.Two);
            }
        }
    }

    [Fact]
    public void BrotliByteArrayRoundTripsNullAndEmpty()
    {
        foreach (var expected in new byte[]?[] { null, Array.Empty<byte>() })
        {
            var value = new CompressionEdgeData { Data = expected };
            var defaultPayload = SharpPackSerializer.Serialize(value);
            var context = new SharpPackSerializerContext();
            var contextPayload = SharpPackSerializer.Serialize(value, context);

            SharpPackSerializer.Deserialize<CompressionEdgeData>(defaultPayload)!
                .Data.Should().Equal(expected);
            SharpPackSerializer.Deserialize<CompressionEdgeData>(
                    contextPayload,
                    context)!
                .Data.Should().Equal(expected);
        }
    }

    [Fact]
    public void BrotliDecompressorStopsAtFrameEndAndEnforcesLimit()
    {
        var source = new byte[16_384];
        var compressor = new BrotliCompressor();
        try
        {
            SharpPackSerializer.Serialize(ref compressor, source);
            var compressed = compressor.ToArray();
            var sequence = ReadOnlySequenceBuilder.Create(
                compressed,
                new byte[] { 0xCA, 0xFE });

            using var decompressor = new BrotliDecompressor();
            var decompressed = decompressor.Decompress(
                sequence,
                out var consumed);

            consumed.Should().Be(compressed.Length);
            SharpPackSerializer.Deserialize<byte[]>(decompressed)
                .Should().Equal(source);

            using var limited = new BrotliDecompressor(1024);
            Action exceedLimit = () => limited.Decompress(compressed);
            exceedLimit.Should().Throw<SharpPackSerializationException>();
        }
        finally
        {
            compressor.Dispose();
        }
    }

    [Fact]
    public void SegmentedBrotliStringRejectsDecodedLengthMismatch()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writerState = SharpPackWriterOptionalStatePool.Rent();
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(
            ref buffer,
            writerState);
        var formatter = new BrotliStringFormatter();
        string? text = "segment-boundary";
        formatter.Serialize(ref writer, ref text);
        writer.Flush();

        var payload = buffer.WrittenSpan.ToArray();
        BitConverter.GetBytes(text!.Length + 1).CopyTo(payload, 0);
        var sequence = ReadOnlySequenceBuilder.Create(
            payload.Select(static value => new[] { value }).ToArray());

        using var readerState = SharpPackReaderOptionalStatePool.Rent();
        var reader = new SharpPackReader(sequence, readerState);
        string? restored = null;
        SharpPackErrorCode? error = null;
        try
        {
            formatter.Deserialize(ref reader, ref restored);
        }
        catch (SharpPackSerializationException exception)
        {
            error = exception.ErrorCode;
        }

        error.Should().Be(SharpPackErrorCode.InvalidWireData);
    }

    [Fact]
    public void GenericBrotliRejectsTrailingDecompressedPayload()
    {
        var compressor = new BrotliCompressor();
        try
        {
            SharpPackSerializer.Serialize(ref compressor, 123);
            SharpPackSerializer.Serialize(ref compressor, 456);
            var payload = compressor.ToArray();
            Assert.Equal(SharpPackErrorCode.InvalidWireData, Assert.Throws<SharpPackSerializationException>(
                () => DeserializeCompressedInt(payload)).ErrorCode);
        }
        finally
        {
            compressor.Dispose();
        }

        static void DeserializeCompressedInt(byte[] payload)
        {
            using var state = SharpPackReaderOptionalStatePool.Rent();
            var reader = new SharpPackReader(payload, state);
            try
            {
                int value = default;
                new BrotliFormatter<int>().Deserialize(ref reader, ref value);
            }
            finally { reader.Dispose(); }
        }
    }


    [Fact]
    public void BrotliReportsTruncatedCompressedPayload()
    {
        var complete = SharpPackSerializer.Serialize(new BrotliIntEnvelope { Value = 123 });
        var truncated = complete[..^1];
        var segmented = ReadOnlySequenceBuilder.Create(truncated[..2], truncated[2..]);

        Assert.Equal(123, SharpPackSerializer.Deserialize<BrotliIntEnvelope>(complete)!.Value);
        Assert.Equal(SharpPackErrorCode.UnexpectedEndOfPayload, Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<BrotliIntEnvelope>(truncated)).ErrorCode);
        Assert.Equal(SharpPackErrorCode.UnexpectedEndOfPayload, Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<BrotliIntEnvelope>(segmented)).ErrorCode);
    }

    [Fact]
    public void RegisteredBrotliWrapsTheSpecifiedFormatterAndKeepsNestedOverrides()
    {
        var inner = new CountFormatter();
        var formatter = BrotliFormatter<CompressedCount>.Wrap(inner);
        var context = new SharpPackSerializerContextBuilder()
            .Register(formatter)
            .Register(new IntOffsetFormatter(10))
            .Build();
        var withoutOffset = new SharpPackSerializerContextBuilder().Register(formatter).Build();

        var payload = SharpPackSerializer.Serialize(new CompressedCount(42), context);

        Assert.Equal(42, SharpPackSerializer.Deserialize<CompressedCount>(payload, context)!.Count);
        Assert.Equal(52, SharpPackSerializer.Deserialize<CompressedCount>(payload, withoutOffset)!.Count);
        Assert.Equal(2, inner.WriteDepth);
        Assert.Equal(2, inner.ReadDepth);
    }

    [Fact]
    public void BrotliMemberUsesTheExistingObjectAndCompressedValueWireFormat()
    {
        var compressor = new BrotliCompressor();
        try
        {
            SharpPackSerializer.Serialize(ref compressor, 42);
            var payload = SharpPackSerializer.Serialize(new BrotliIntEnvelope { Value = 42 });
            Assert.Equal(1, payload[0]);
            Assert.Equal(compressor.ToArray(), payload[1..]);
        }
        finally { compressor.Dispose(); }
    }

    [Fact]
    public void RegisteredBrotliRequiresAnUncompressedFormatter()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(BrotliFormatter<int>.Default)
            .Build();
        var wrapped = new SharpPackSerializerContextBuilder()
            .Register(BrotliFormatter<int>.Wrap(new UnmanagedFormatter<int>()))
            .Build();
        var payload = SharpPackSerializer.Serialize(42, wrapped);
        var output = new ArrayBufferWriter<byte>();

        Assert.Equal(42, SharpPackSerializer.Deserialize<int>(payload, wrapped));
        Assert.Equal(SharpPackErrorCode.FormatterResolutionFailed, Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Serialize(ref output, 42, context)).ErrorCode);
        Assert.Equal(0, output.WrittenCount);
        Assert.Equal(SharpPackErrorCode.FormatterResolutionFailed, Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<int>(payload, context)).ErrorCode);
        Assert.Throws<ArgumentException>(() => BrotliFormatter<int>.Wrap(BrotliFormatter<int>.Default));
    }

    sealed record CompressedCount(int Count);

    sealed class CountFormatter : SharpPackFormatter<CompressedCount>
    {
        public int WriteDepth { get; private set; }
        public int ReadDepth { get; private set; }

        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref CompressedCount? value)
        {
            WriteDepth = WriterProbe<TBufferWriter>.GetDepth(ref writer);
            writer.WriteValue(value!.Count);
        }

        public override void Deserialize(ref SharpPackReader reader, scoped ref CompressedCount? value)
        {
            ReadDepth = GetReaderDepth(ref reader);
            value = new(reader.ReadValue<int>());
        }

        // Observe inheritance with one small value instead of building a deep
        // compressed graph to reach the runtime recursion cutoff.
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "depth")]
        static extern ref int GetReaderDepth(ref SharpPackReader reader);

        static class WriterProbe<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
        {
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "depth")]
            internal static extern ref int GetDepth(ref SharpPackWriter<TBufferWriter> writer);
        }
    }

    byte[] ReferenceDecompress(byte[] bytes)
    {
        using (var ms = new MemoryStream(bytes))
        using (var brotli = new BrotliStream(ms, CompressionMode.Decompress))
        {
            var dest = new MemoryStream();
            brotli.CopyTo(dest);
            return dest.ToArray();
        }
    }
}


[SharpPackable]
public partial class CompressionAttrData
{
    public int Id1 { get; set; }

    [BrotliFormatter]
    public byte[] Data { get; set; } = default!;

    [BrotliStringFormatter]
    public string String { get; set; } = default!;

    public int Id2 { get; set; }
}


[SharpPackable]
public partial class CompressionAttrData2
{
    public int Id1 { get; set; }

    [BrotliFormatter]
    public byte[] Data { get; set; } = default!;

    [BrotliStringFormatter]
    public string String { get; set; } = default!;

    [BrotliFormatter<StandardTypeTwo>]
    public StandardTypeTwo Two { get; set; } = default!;

    public int Id2 { get; set; }
}

[SharpPackable]
public partial class CompressionEdgeData
{
    [BrotliFormatter]
    public byte[]? Data { get; set; }
}

[SharpPackable]
public partial class BrotliIntEnvelope
{
    [BrotliFormatter<int>]
    public int Value { get; set; }
}
