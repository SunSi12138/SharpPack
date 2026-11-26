using SharpPack.Streaming;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPack.Tests.Streaming;

public class TransportContractTest
{
    const string PipeReaderCompletedMessage =
        "The pipe reader completed before serialization finished.";

    [Theory]
    [InlineData(PipeWritePath.Frame)]
    [InlineData(PipeWritePath.LengthPrefixed)]
    [InlineData(PipeWritePath.Collection)]
    public async Task PipeFlushCanceled_ThrowsOperationCanceledException(
        PipeWritePath path)
    {
        var writer = new ControlledPipeWriter(
            new FlushResult(isCanceled: true, isCompleted: false));

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await WritePipeAsync(path, writer));

        writer.CompleteCalled.Should().BeFalse();
        writer.Written.IsEmpty.Should().BeFalse(
            "flush-result cancellation can occur after output has already been written");

        writer.FlushResult = new FlushResult(
            isCanceled: false,
            isCompleted: false);
        writer.Write(new byte[] { 0xCA });
        var retry = await writer.FlushAsync();

        retry.IsCanceled.Should().BeFalse();
        retry.IsCompleted.Should().BeFalse();
        writer.Written.Span[^1].Should().Be(0xCA);
    }

    [Theory]
    [InlineData(PipeWritePath.Frame)]
    [InlineData(PipeWritePath.LengthPrefixed)]
    [InlineData(PipeWritePath.Collection)]
    public async Task PipeFlushCompleted_ThrowsSameTransportClosedException(
        PipeWritePath path)
    {
        var writer = new ControlledPipeWriter(
            new FlushResult(isCanceled: false, isCompleted: true));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await WritePipeAsync(path, writer));

        exception.Message.Should().Be(PipeReaderCompletedMessage);
        writer.CompleteCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData(PipeWritePath.Frame)]
    [InlineData(PipeWritePath.LengthPrefixed)]
    [InlineData(PipeWritePath.Collection)]
    public async Task PipeSerialization_PreCanceledToken_WritesNothingAndKeepsWriterOwnedByCaller(
        PipeWritePath path)
    {
        var writer = new ControlledPipeWriter(
            new FlushResult(isCanceled: false, isCompleted: false));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await WritePipeAsync(
                path,
                writer,
                cancellation.Token));

        writer.Written.IsEmpty.Should().BeTrue();
        writer.CompleteCalled.Should().BeFalse();

        await WritePipeAsync(path, writer);
        writer.Written.ToArray().Should().Equal(ExpectedPipeBytes(path));
    }

    [Fact]
    public async Task StreamNullValidation_HappensBeforeSerializationOrReadWork()
    {
        var formatter = new TrackingTransportValueFormatter();
        var context = new SharpPackSerializerContextBuilder()
            .Register(formatter)
            .Build();

        var serialize = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await SharpPackSerializer.SerializeAsync(
                (Stream)null!,
                new TrackingTransportValue(42),
                context));
        serialize.ParamName.Should().Be("stream");
        formatter.SerializeCalls.Should().Be(0);

        var serializeWithoutContext = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await SharpPackSerializer.SerializeAsync(
                (Stream)null!,
                42));
        serializeWithoutContext.ParamName.Should().Be("stream");

        var deserialize = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await SharpPackSerializer.DeserializeAsync<int>(
                (Stream)null!));
        deserialize.ParamName.Should().Be("stream");

        var deserializeWithContext = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await SharpPackSerializer.DeserializeAsync<int>(
                (Stream)null!,
                context));
        deserializeWithContext.ParamName.Should().Be("stream");
    }

    [Fact]
    public async Task StreamSerialization_PreCanceledToken_DoesNotSerializeOrWrite()
    {
        var formatter = new TrackingTransportValueFormatter();
        var context = new SharpPackSerializerContextBuilder()
            .Register(formatter)
            .Build();
        using var stream = new TrackingWriteStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await SharpPackSerializer.SerializeAsync(
                stream,
                new TrackingTransportValue(42),
                context,
                cancellation.Token));

        formatter.SerializeCalls.Should().Be(0);
        stream.Length.Should().Be(0);
        stream.FlushAsyncCalls.Should().Be(0);
    }

    [Fact]
    public async Task GeneralStreamDeserialize_ReadsToEndAndLeavesStreamOpen()
    {
        var first = SharpPackSerializer.Serialize(123);
        var second = SharpPackSerializer.Serialize(456);
        using var stream = new NonMemoryReadStream(first.Concat(second).ToArray());

        var value = await SharpPackSerializer.DeserializeAsync<int>(stream);

        value.Should().Be(123);
        stream.BytesRead.Should().Be(first.Length + second.Length);
        stream.EndOfStreamReads.Should().Be(1);
        stream.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task LengthDelimitedStream_ConsumesExactlyPayloadAndLeavesFollowingBytes()
    {
        var payload = SharpPackSerializer.Serialize(123);
        byte[] trailing = [0xCA, 0xFE];
        using var stream = new NonMemoryReadStream(payload.Concat(trailing).ToArray());

        var value = await SharpPackSerializer.DeserializeAsync<int>(
            stream,
            payload.Length);

        value.Should().Be(123);
        stream.BytesRead.Should().Be(payload.Length);
        stream.IsDisposed.Should().BeFalse();

        var remainder = new byte[trailing.Length];
        var read = await stream.ReadAsync(remainder);
        read.Should().Be(trailing.Length);
        remainder.Should().Equal(trailing);
    }

    [Fact]
    public async Task LengthDelimitedStream_ExactConsumptionMismatch_FailsDeterministically()
    {
        var payload = SharpPackSerializer.Serialize(123);
        byte[] suffix = [0xCA, 0xFE];
        using var stream = new NonMemoryReadStream(payload.Concat(suffix).ToArray());

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackSerializer.DeserializeAsync<int>(
                stream,
                payload.Length + 1));

        stream.BytesRead.Should().Be(payload.Length + 1);

        var remainder = new byte[1];
        var read = await stream.ReadAsync(remainder);
        read.Should().Be(1);
        remainder[0].Should().Be(0xFE);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoreStreamSerialize_PerformsFinalFlushAndLeavesStreamOpen(
        bool useContext)
    {
        using var stream = new TrackingWriteStream();

        if (useContext)
        {
            await SharpPackSerializer.SerializeAsync(
                stream,
                new[] { 1, 2, 3 },
                new SharpPackSerializerContext());
        }
        else
        {
            await SharpPackSerializer.SerializeAsync(
                stream,
                new[] { 1, 2, 3 });
        }

        stream.FlushAsyncCalls.Should().Be(1);
        stream.IsDisposed.Should().BeFalse();
        stream.ToArray().Should().Equal(
            SharpPackSerializer.Serialize(new[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task StreamingCollectionStream_DoesNotAddFinalFlushAndLeavesStreamOpen()
    {
        using var stream = new TrackingWriteStream();
        var values = new[] { 1, 2, 3 };

        await SharpPackStreamingSerializer.SerializeAsync(
            stream,
            values.Length,
            values,
            flushRate: 1);

        stream.FlushAsyncCalls.Should().Be(0);
        stream.IsDisposed.Should().BeFalse();
        stream.ToArray().Should().Equal(SharpPackSerializer.Serialize(values));
    }

    [Fact]
    public async Task StreamingCollectionDeserialize_LeavesCallerOwnedStreamOpen()
    {
        using var stream = new NonMemoryReadStream(
            SharpPackSerializer.Serialize(new[] { 1, 2, 3 }));
        var values = new List<int>();

        await foreach (var value in SharpPackStreamingSerializer.DeserializeAsync<int>(
            stream,
            bufferAtLeast: 4,
            readMinimumSize: 4))
        {
            values.Add(value);
        }

        values.Should().Equal(1, 2, 3);
        stream.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task StreamSerializationFailure_LeavesPartialBytesAndPooledStateReusable()
    {
        var value = new string('x', 200_000);
        var expected = SharpPackSerializer.Serialize(value);
        using var stream = new PartialWriteThenThrowStream();

        await Assert.ThrowsAsync<IOException>(
            async () => await SharpPackSerializer.SerializeAsync(stream, value));

        var partial = stream.ToArray();
        partial.Should().NotBeEmpty();
        partial.Length.Should().BeLessThan(expected.Length);
        expected.AsSpan(0, partial.Length).SequenceEqual(partial).Should().BeTrue();

        using var recovery = new MemoryStream();
        await SharpPackSerializer.SerializeAsync(recovery, "reused");
        SharpPackSerializer.Deserialize<string>(recovery.ToArray())
            .Should().Be("reused");
    }

    static async Task WritePipeAsync(
        PipeWritePath path,
        PipeWriter writer,
        CancellationToken cancellationToken = default)
    {
        switch (path)
        {
            case PipeWritePath.Frame:
                _ = await SharpPackStreamingSerializer.SerializeFrameAsync(
                    writer,
                    42,
                    cancellationToken: cancellationToken);
                break;
            case PipeWritePath.LengthPrefixed:
                await SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(
                    writer,
                    42,
                    cancellationToken: cancellationToken);
                break;
            case PipeWritePath.Collection:
                await SharpPackStreamingSerializer.SerializeAsync(
                    writer,
                    1,
                    new[] { 42 },
                    flushRate: 16,
                    cancellationToken: cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    static byte[] ExpectedPipeBytes(PipeWritePath path)
    {
        var payload = SharpPackSerializer.Serialize(42);
        if (path == PipeWritePath.Frame)
        {
            return payload;
        }
        if (path == PipeWritePath.Collection)
        {
            return SharpPackSerializer.Serialize(new[] { 42 });
        }

        var framed = new byte[sizeof(uint) + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            framed,
            (uint)payload.Length);
        payload.CopyTo(framed.AsSpan(sizeof(uint)));
        return framed;
    }

    public enum PipeWritePath
    {
        Frame,
        LengthPrefixed,
        Collection,
    }

    sealed record TrackingTransportValue(int Value);

    sealed class ControlledPipeWriter(FlushResult flushResult) : PipeWriter
    {
        readonly ArrayBufferWriter<byte> buffer = new();

        public FlushResult FlushResult { get; set; } = flushResult;

        public ReadOnlyMemory<byte> Written => buffer.WrittenMemory;

        public bool CompleteCalled { get; private set; }

        public override void Advance(int bytes)
            => buffer.Advance(bytes);

        public override void CancelPendingFlush()
        {
        }

        public override void Complete(Exception? exception = null)
            => CompleteCalled = true;

        public override ValueTask<FlushResult> FlushAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<FlushResult>(cancellationToken);
            }

            return ValueTask.FromResult(FlushResult);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
            => buffer.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0)
            => buffer.GetSpan(sizeHint);
    }

    sealed class TrackingTransportValueFormatter
        : SharpPackFormatter<TrackingTransportValue>
    {
        public int SerializeCalls { get; private set; }

        public override void Serialize<TBufferWriter>(
            ref SharpPackWriter<TBufferWriter> writer,
            scoped ref TrackingTransportValue? value)
        {
            SerializeCalls++;
            writer.WriteUnmanaged(value!.Value);
        }

        public override void Deserialize(
            ref SharpPackReader reader,
            scoped ref TrackingTransportValue? value)
        {
            value = new TrackingTransportValue(reader.ReadUnmanaged<int>());
        }
    }

    sealed class TrackingWriteStream : MemoryStream
    {
        public int FlushAsyncCalls { get; private set; }

        public bool IsDisposed { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushAsyncCalls++;
            return base.FlushAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    sealed class NonMemoryReadStream(byte[] bytes) : Stream
    {
        int position;

        public int BytesRead => position;

        public int EndOfStreamReads { get; private set; }

        public bool IsDisposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position == bytes.Length)
            {
                EndOfStreamReads++;
                return 0;
            }

            var toCopy = Math.Min(count, bytes.Length - position);
            bytes.AsSpan(position, toCopy)
                .CopyTo(buffer.AsSpan(offset, toCopy));
            position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == bytes.Length)
            {
                EndOfStreamReads++;
                return ValueTask.FromResult(0);
            }

            var toCopy = Math.Min(buffer.Length, bytes.Length - position);
            bytes.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    sealed class PartialWriteThenThrowStream : Stream
    {
        readonly MemoryStream captured = new();

        public byte[] ToArray() => captured.ToArray();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => captured.Length;

        public override long Position
        {
            get => captured.Position;
            set => throw new NotSupportedException();
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var partialLength = Math.Max(1, buffer.Length / 2);
            captured.Write(buffer.Span[..partialLength]);
            return ValueTask.FromException(
                new IOException("Injected partial write failure."));
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                captured.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
