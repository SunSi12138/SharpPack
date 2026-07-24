using MemoryPack.Tests.Models;
using MemoryPack.Internal;
using MemoryPack.Streaming;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

public class ZeroCopyApiTest
{
    [Fact]
    public void BufferWriter_ReturnsExactPayloadLength()
    {
        var value = new StandardTypeTwo { One = 10, Two = 20 };
        var expected = MemoryPackSerializer.Serialize(value);
        var defaultWriter = new ArrayBufferWriter<byte>();
        var contextWriter = new ArrayBufferWriter<byte>();
        var context = new MemoryPackSerializerContext();

        var defaultLength = MemoryPackSerializer.Serialize(ref defaultWriter, value);
        var contextLength = MemoryPackSerializer.Serialize(
            ref contextWriter,
            value,
            context);

        defaultLength.Should().Be(expected.Length);
        contextLength.Should().Be(expected.Length);
        defaultWriter.WrittenSpan.ToArray().Should().Equal(expected);
        contextWriter.WrittenSpan.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void SpanDestination_WritesWithoutIntermediatePayload()
    {
        var value = new StandardTypeTwo { One = 10, Two = 20 };
        var expected = MemoryPackSerializer.Serialize(value);
        Span<byte> defaultDestination = stackalloc byte[expected.Length];
        Span<byte> contextDestination = stackalloc byte[expected.Length];
        var context = new MemoryPackSerializerContext();

        MemoryPackSerializer.TrySerialize(
            defaultDestination,
            value,
            out var defaultWritten).Should().BeTrue();
        MemoryPackSerializer.TrySerialize(
            contextDestination,
            value,
            context,
            out var contextWritten).Should().BeTrue();

        defaultWritten.Should().Be(expected.Length);
        contextWritten.Should().Be(expected.Length);
        defaultDestination.ToArray().Should().Equal(expected);
        contextDestination.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void SpanDestination_ReportsInsufficientCapacity()
    {
        Span<byte> destination = stackalloc byte[1];

        MemoryPackSerializer.TrySerialize(
            destination,
            new StandardTypeTwo { One = 1, Two = 2 },
            out var written).Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void SequenceDeserializer_ReportsConsumedFrameWithoutReadingSuffix()
    {
        var value = new StandardTypeTwo { One = 10, Two = 20 };
        var payload = MemoryPackSerializer.Serialize(value);
        var framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 0);
        framed[^2] = 0xCA;
        framed[^1] = 0xFE;
        var sequence = new ReadOnlySequence<byte>(framed);
        var context = new MemoryPackSerializerContext();

        StandardTypeTwo? defaultValue = null;
        StandardTypeTwo? contextValue = null;
        var defaultConsumed = MemoryPackSerializer.Deserialize(
            sequence,
            ref defaultValue);
        var contextConsumed = MemoryPackSerializer.Deserialize(
            sequence,
            ref contextValue,
            context);

        defaultConsumed.Should().Be(payload.Length);
        contextConsumed.Should().Be(payload.Length);
        defaultValue.Should().BeEquivalentTo(value);
        contextValue.Should().BeEquivalentTo(value);
        sequence.Slice(defaultConsumed).ToArray().Should().Equal(0xCA, 0xFE);
    }

    [Fact]
    public void SequenceDeserializer_ReadsEveryByteFromADifferentSegment()
    {
        var value = new StandardTypeTwo { One = 10, Two = 20 };
        var payload = MemoryPackSerializer.Serialize(value);
        var sequence = ReadOnlySequenceBuilder.Create(
            payload.Select(static value => new[] { value }).ToArray());
        var context = new MemoryPackSerializerContext();

        MemoryPackSerializer.Deserialize<StandardTypeTwo>(sequence)
            .Should().BeEquivalentTo(value);
        MemoryPackSerializer.Deserialize<StandardTypeTwo>(sequence, context)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void SequenceDeserializer_ReadsRandomSegmentBoundaries()
    {
        var value = new StandardTypeTwo { One = 1234, Two = 5678 };
        var payload = MemoryPackSerializer.Serialize(value);
        var random = new Random(42);
        var segments = new List<byte[]>();
        for (var offset = 0; offset < payload.Length;)
        {
            var length = Math.Min(random.Next(1, 5), payload.Length - offset);
            segments.Add(payload.AsSpan(offset, length).ToArray());
            offset += length;
        }

        var sequence = ReadOnlySequenceBuilder.Create(segments.ToArray());
        MemoryPackSerializer.Deserialize<StandardTypeTwo>(sequence)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void SequenceDeserializer_RejectsTruncatedAndMaliciousLengths()
    {
        var payload = MemoryPackSerializer.Serialize("truncated");
        var truncated = ReadOnlySequenceBuilder.Create(
            payload[..^1].Select(static value => new[] { value }).ToArray());
        var malicious = ReadOnlySequenceBuilder.Create(
            new byte[] { 0xFF },
            new byte[] { 0xFF },
            new byte[] { 0xFF },
            new byte[] { 0x7F });

        Assert.Throws<MemoryPackSerializationException>(
            () => MemoryPackSerializer.Deserialize<string>(truncated));
        Assert.Throws<MemoryPackSerializationException>(
            () => MemoryPackSerializer.Deserialize<string>(malicious));
    }

    [Fact]
    public void LargeTemporarySegments_AreReleasedAfterReset()
    {
        var writer = new ReusableLinkedArrayBufferWriter(
            useFirstBuffer: true,
            pinned: false);
        var value = new string('x', 1_000_000);

        _ = MemoryPackSerializer.Serialize(ref writer, value);
        var payload = writer.ToArrayAndReset();

        payload.Should().NotBeEmpty();
        writer.TotalWritten.Should().Be(0);
        writer.DangerousGetFirstBuffer().Should().HaveCount(4096);
    }

    [Fact]
    public async Task PipeFrame_RoundTripsDefaultAndContextPaths()
    {
        var value = new StandardTypeTwo { One = 123, Two = 456 };
        var context = new MemoryPackSerializerContext();

        await RoundTrip(serializerContext: null);
        await RoundTrip(context);

        async Task RoundTrip(MemoryPackSerializerContext? serializerContext)
        {
            var pipe = new Pipe();
            var length = await MemoryPackStreamingSerializer.SerializeFrameAsync(
                pipe.Writer,
                value,
                serializerContext);
            var decoded = await MemoryPackStreamingSerializer.DeserializeFrameAsync<
                StandardTypeTwo>(
                pipe.Reader,
                length,
                serializerContext);

            decoded.Should().BeEquivalentTo(value);
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }
}
