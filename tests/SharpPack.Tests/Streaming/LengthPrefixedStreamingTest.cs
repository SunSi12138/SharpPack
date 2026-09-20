using FluentAssertions;
using SharpPack.Streaming;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPack.Tests.Streaming;

public class LengthPrefixedStreamingTest
{
    const int ChunkSize = 1024;
    const int LargeTextLength = 256 * 1024;

    [Fact]
    public async Task LengthPrefixed_LargeVariableItem_HandlesFragmentation()
    {
        var expected = new StreamingLargeVariableItem
        {
            Payload = new string('x', LargeTextLength),
        };
        var frame = CreateFrame(expected);
        var pipe = CreateFragmentedPipe();

        var producer = WriteInChunksAsync(pipe.Writer, frame);
        var actual = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingLargeVariableItem>(
                pipe.Reader);

        actual.Should().NotBeNull();
        actual!.Payload.Should().Be(expected.Payload);

        await pipe.Reader.CompleteAsync();
        await producer;
    }

    [Fact]
    public async Task LengthPrefixed_NestedVariableItem_HandlesFragmentation()
    {
        var expected = new StreamingNestedVariableItem
        {
            Items =
            [
                new StreamingLargeVariableItem
                {
                    Payload = new string('a', LargeTextLength),
                },
                new StreamingLargeVariableItem
                {
                    Payload = new string('b', LargeTextLength / 2),
                },
            ],
        };
        var frame = CreateFrame(expected);
        var pipe = CreateFragmentedPipe();

        var producer = WriteInChunksAsync(pipe.Writer, frame);
        var actual = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingNestedVariableItem>(
                pipe.Reader);

        actual!.Items.Should().HaveCount(2);
        actual.Items![0].Payload.Should().Be(expected.Items![0].Payload);
        actual.Items[1].Payload.Should().Be(expected.Items[1].Payload);

        await pipe.Reader.CompleteAsync();
        await producer;
    }

    [Fact]
    public async Task LengthPrefixed_CustomVariableFormatter_RoundTrips()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register<StreamingCustomVariableItem>(
                new StreamingCustomVariableItemFormatter())
            .Build();
        var expected = new StreamingCustomVariableItem
        {
            Payload = new string('c', LargeTextLength),
        };
        var pipe = new Pipe();

        var serialization = SharpPackStreamingSerializer
            .SerializeLengthPrefixedAsync(
                pipe.Writer,
                expected,
                context)
            .AsTask();
        var actual = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingCustomVariableItem>(
                pipe.Reader,
                context: context);
        await serialization;

        actual!.Payload.Should().Be(expected.Payload);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_ReferenceAwareItem_RoundTripsCircularReference()
    {
        var expected = new StreamingReferenceVariableItem
        {
            Payload = new string('d', LargeTextLength),
        };
        expected.Next = expected;
        var pipe = new Pipe();

        var serialization = SharpPackStreamingSerializer
            .SerializeLengthPrefixedAsync(
                pipe.Writer,
                expected)
            .AsTask();
        var actual = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingReferenceVariableItem>(
                pipe.Reader);
        await serialization;

        actual!.Payload.Should().Be(expected.Payload);
        actual.Next.Should().BeSameAs(actual);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_MultipleFrames_AndNullItem_AreIndependent()
    {
        var pipe = new Pipe();
        var first = new StreamingLargeVariableItem { Payload = "first" };
        var second = new StreamingLargeVariableItem { Payload = "second" };

        await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
            pipe.Writer,
            first);
        await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
            pipe.Writer,
            second);
        await SharpPackStreamingSerializer
            .SerializeLengthPrefixedAsync<StreamingLargeVariableItem>(
                pipe.Writer,
                null);
        await pipe.Writer.CompleteAsync();

        var actual = new List<StreamingLargeVariableItem?>();
        await foreach (var item in SharpPackStreamingSerializer
                           .DeserializeLengthPrefixedItemsAsync<
                               StreamingLargeVariableItem>(pipe.Reader))
        {
            actual.Add(item);
        }

        actual.Should().HaveCount(3);
        actual[0]!.Payload.Should().Be("first");
        actual[1]!.Payload.Should().Be("second");
        actual[2].Should().BeNull();

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_EarlyConsumerBreak_LeavesNextFrameAvailable()
    {
        var pipe = new Pipe();

        await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
            pipe.Writer,
            new StreamingLargeVariableItem { Payload = "first" });
        await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
            pipe.Writer,
            new StreamingLargeVariableItem { Payload = "second" });
        await pipe.Writer.CompleteAsync();

        await foreach (var item in SharpPackStreamingSerializer
                           .DeserializeLengthPrefixedItemsAsync<
                               StreamingLargeVariableItem>(pipe.Reader))
        {
            item!.Payload.Should().Be("first");
            break;
        }

        var second = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingLargeVariableItem>(
                pipe.Reader);
        second!.Payload.Should().Be("second");

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task SerializeLengthPrefixed_PreservesCorePayloadBytes()
    {
        var expected = new StreamingLargeVariableItem { Payload = "wire" };
        var corePayload = SharpPackSerializer.Serialize(expected);
        var pipe = new Pipe();

        await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
            pipe.Writer,
            expected);

        var read = await pipe.Reader.ReadAsync();
        var frame = read.Buffer.ToArray();

        BinaryPrimitives.ReadUInt32LittleEndian(frame)
            .Should().Be((uint)corePayload.Length);
        frame.AsSpan(sizeof(uint)).ToArray().Should().Equal(corePayload);

        pipe.Reader.AdvanceTo(read.Buffer.End);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_TruncatedHeader_Throws()
    {
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 0x04, 0x00 });
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(pipe.Reader));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_TruncatedPayload_Throws()
    {
        var payload = SharpPackSerializer.Serialize(123);
        var frame = new byte[sizeof(uint) + payload.Length - 1];
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame,
            (uint)payload.Length);
        payload.AsSpan(0, payload.Length - 1)
            .CopyTo(frame.AsSpan(sizeof(uint)));

        var pipe = new Pipe();
        pipe.Writer.Write(frame);
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<EndOfStreamException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(pipe.Reader));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_OversizedFrame_IsRejectedBeforePayloadWait()
    {
        var pipe = new Pipe();
        var header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(header, uint.MaxValue);
        pipe.Writer.Write(header);
        await pipe.Writer.FlushAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(pipe.Reader));

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_ConfiguredMaximum_IsCheckedBeforePayloadWait()
    {
        var pipe = new Pipe();
        var header = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 1024);
        pipe.Writer.Write(header);
        await pipe.Writer.FlushAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(
                    pipe.Reader,
                    maxFrameLength: 16));

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_CancellationWhileWaitingForHeader_Throws()
    {
        var pipe = new Pipe();
        using var cancellation = new CancellationTokenSource();

        var read = SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<int>(
                pipe.Reader,
                cancellationToken: cancellation.Token)
            .AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await read);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_CancellationWhileWaitingForPayload_BalancesPipeRead()
    {
        var expected = new StreamingLargeVariableItem
        {
            Payload = new string('z', 32 * 1024),
        };
        var frame = CreateFrame(expected);
        var initialLength = sizeof(uint) + 16;
        var pipe = new Pipe();

        pipe.Writer.Write(frame.AsSpan(0, initialLength));
        await pipe.Writer.FlushAsync();

        using var cancellation = new CancellationTokenSource();
        var read = SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<StreamingLargeVariableItem>(
                pipe.Reader,
                cancellationToken: cancellation.Token)
            .AsTask();

        await Task.Yield();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await read);

        pipe.Writer.Write(new byte[] { 0x7A });
        await pipe.Writer.FlushAsync();

        var postCancellation = await pipe.Reader.ReadAsync();
        postCancellation.Buffer.ToArray().Should().Equal(0x7A);
        pipe.Reader.AdvanceTo(postCancellation.Buffer.End);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_ExactConsumptionMismatch_FailsDeterministically()
    {
        var payload = SharpPackSerializer.Serialize(123);
        var frame = new byte[sizeof(uint) + payload.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame,
            (uint)(payload.Length + 1));
        payload.CopyTo(frame.AsSpan(sizeof(uint)));
        frame[^1] = 0xCA;
        var nextFrame = CreateFrame(456);

        var pipe = new Pipe();
        pipe.Writer.Write(frame);
        pipe.Writer.Write(nextFrame);
        await pipe.Writer.FlushAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(pipe.Reader));

        var next = await SharpPackStreamingSerializer
            .DeserializeLengthPrefixedAsync<int>(pipe.Reader);
        next.Should().Be(456);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task LengthPrefixed_ZeroLengthPayload_IsRejectedByCore()
    {
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[sizeof(uint)]);
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackStreamingSerializer
                .DeserializeLengthPrefixedAsync<int>(pipe.Reader));

        await pipe.Reader.CompleteAsync();
    }

    static Pipe CreateFragmentedPipe()
        => new(new PipeOptions(
            pauseWriterThreshold: 16 * 1024,
            resumeWriterThreshold: 8 * 1024,
            minimumSegmentSize: ChunkSize,
            useSynchronizationContext: false));

    static byte[] CreateFrame<T>(
        T value,
        SharpPackSerializerContext? context = null)
    {
        var payload = context is null
            ? SharpPackSerializer.Serialize(value)
            : SharpPackSerializer.Serialize(value, context);
        var frame = new byte[sizeof(uint) + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame,
            (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(uint)));
        return frame;
    }

    static async Task WriteInChunksAsync(
        PipeWriter writer,
        byte[] bytes)
    {
        try
        {
            for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
            {
                var length = Math.Min(ChunkSize, bytes.Length - offset);
                writer.Write(bytes.AsSpan(offset, length));

                var flush = await writer.FlushAsync();
                if (flush.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            await writer.CompleteAsync();
        }
    }
}
