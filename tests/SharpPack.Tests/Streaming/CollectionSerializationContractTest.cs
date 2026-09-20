using FluentAssertions;
using SharpPack.Streaming;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPack.Tests.Streaming;

public class CollectionSerializationContractTest
{
    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_CountLessThanSource_Throws(
        CollectionDestination destination)
    {
        var formatter = new CountingCollectionItemFormatter();
        var context = new SharpPackSerializerContextBuilder()
            .Register<CountingCollectionItem>(formatter)
            .Build();
        var source = new TrackingEnumerable<CountingCollectionItem>(
        [
            new(10),
            new(20),
            new(30),
        ]);

        var attempt = await CaptureAsync(
            destination,
            count: 1,
            source,
            flushRate: 1,
            context);

        attempt.Exception.Should().BeOfType<SharpPackSerializationException>();
        source.MoveNextCalls.Should().Be(2,
            "a lazy source should be probed exactly once past the declared count");
        source.CurrentReads.Should().Be(1);
        formatter.SerializeCalls.Should().Be(1,
            "the (count + 1)th item must be detected but never serialized");
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_CountGreaterThanSource_Throws(
        CollectionDestination destination)
    {
        var source = new TrackingEnumerable<int>([10]);

        var attempt = await CaptureAsync(
            destination,
            count: 2,
            source,
            flushRate: 1);

        attempt.Exception.Should().BeOfType<SharpPackSerializationException>();
        source.MoveNextCalls.Should().Be(2);
        source.CurrentReads.Should().Be(1);
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_KnownCountMismatch_DoesNotWriteHeader(
        CollectionDestination destination)
    {
        var source = new KnownCountCollection<int>(actualCount: 2);

        var attempt = await CaptureAsync(
            destination,
            count: 1,
            source,
            flushRate: 16);

        attempt.Exception.Should().BeOfType<SharpPackSerializationException>();
        source.GetEnumeratorCalls.Should().Be(0,
            "known count detection must not enumerate the source");
        attempt.Bytes.Should().BeEmpty(
            "known mismatches are knowable before the first output byte");
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_NullSource_FailsBeforeOutput(
        CollectionDestination destination)
    {
        var attempt = await CaptureAsync<int>(
            destination,
            count: 0,
            source: null!,
            flushRate: 16);

        attempt.Exception.Should().BeOfType<ArgumentNullException>();
        attempt.Bytes.Should().BeEmpty();
    }

    [Fact]
    public async Task Serialize_NullPipeWriter_FailsBeforeEnumeratingSource()
    {
        var source = new TrackingEnumerable<int>([1]);

        var exception = await Record.ExceptionAsync(
            async () => await SharpPackStreamingSerializer.SerializeAsync(
                (PipeWriter)null!,
                count: 1,
                source));

        exception.Should().BeOfType<ArgumentNullException>();
        source.GetEnumeratorCalls.Should().Be(0);
    }

    [Fact]
    public async Task Serialize_NullStream_FailsBeforeEnumeratingSource()
    {
        var source = new TrackingEnumerable<int>([1]);

        var exception = await Record.ExceptionAsync(
            async () => await SharpPackStreamingSerializer.SerializeAsync(
                (Stream)null!,
                count: 1,
                source));

        exception.Should().BeOfType<ArgumentNullException>();
        source.GetEnumeratorCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_NegativeCount_FailsBeforeOutput(
        CollectionDestination destination)
    {
        var source = new TrackingEnumerable<int>([1]);

        var attempt = await CaptureAsync(
            destination,
            count: -1,
            source,
            flushRate: 16);

        attempt.Exception.Should().BeOfType<ArgumentOutOfRangeException>();
        attempt.Bytes.Should().BeEmpty();
        source.GetEnumeratorCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe, 0)]
    [InlineData(CollectionDestination.Stream, 0)]
    [InlineData(CollectionDestination.Pipe, -1)]
    [InlineData(CollectionDestination.Stream, -1)]
    public async Task Serialize_NonPositiveFlushRate_FailsBeforeOutput(
        CollectionDestination destination,
        int flushRate)
    {
        var source = new TrackingEnumerable<int>([1]);

        var attempt = await CaptureAsync(
            destination,
            count: 1,
            source,
            flushRate);

        attempt.Exception.Should().BeOfType<ArgumentOutOfRangeException>();
        attempt.Bytes.Should().BeEmpty();
        source.GetEnumeratorCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_SourceEnumerationException_IsPreservedAndStateRemainsReusable(
        CollectionDestination destination)
    {
        var sentinel = new CollectionEnumerationException();
        var source = new ThrowOnSecondMoveNextEnumerable<int>(42, sentinel);

        var failed = await CaptureAsync(
            destination,
            count: 2,
            source,
            flushRate: 1);

        failed.Exception.Should().BeSameAs(sentinel);

        var values = new[] { 1, 2, 3, 4 };
        var recovered = await CaptureAsync(
            destination,
            values.Length,
            values,
            flushRate: 1);

        recovered.Exception.Should().BeNull();
        recovered.Bytes.Should().Equal(SharpPackSerializer.Serialize(values));
    }

    [Fact]
    public async Task Serialize_PipeCancellation_DoesNotCorruptReusableState()
    {
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 32,
            resumeWriterThreshold: 16,
            minimumSegmentSize: 16,
            useSynchronizationContext: false));
        using var cancellation = new CancellationTokenSource();
        var values = Enumerable.Range(0, 256).ToArray();

        var serialization = SharpPackStreamingSerializer.SerializeAsync(
            pipe.Writer,
            values.Length,
            values,
            flushRate: 1,
            cancellationToken: cancellation.Token).AsTask();

        for (var i = 0; i < 100 && serialization.IsCompleted; i++)
        {
            await Task.Yield();
        }

        serialization.IsCompleted.Should().BeFalse(
            "the writer should be blocked by pipe backpressure before cancellation");
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await serialization);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();

        var recoveredValues = new[] { 7, 8, 9 };
        var recovered = await CaptureAsync(
            CollectionDestination.Pipe,
            recoveredValues.Length,
            recoveredValues,
            flushRate: 1);

        recovered.Exception.Should().BeNull();
        recovered.Bytes.Should().Equal(
            SharpPackSerializer.Serialize(recoveredValues));
    }

    [Fact]
    public async Task Serialize_StreamCancellation_DoesNotCorruptReusableState()
    {
        using var cancellation = new CancellationTokenSource();
        await using var stream = new CancelOnWriteStream(cancellation);
        var values = Enumerable.Range(0, 32).ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await SharpPackStreamingSerializer.SerializeAsync(
                stream,
                values.Length,
                values,
                flushRate: 1,
                cancellationToken: cancellation.Token));

        var recoveredValues = new[] { 11, 12, 13 };
        var recovered = await CaptureAsync(
            CollectionDestination.Stream,
            recoveredValues.Length,
            recoveredValues,
            flushRate: 1);

        recovered.Exception.Should().BeNull();
        recovered.Bytes.Should().Equal(
            SharpPackSerializer.Serialize(recoveredValues));
    }

    [Theory]
    [InlineData(CollectionDestination.Pipe)]
    [InlineData(CollectionDestination.Stream)]
    public async Task Serialize_ValidCount_PreservesCoreWireBytes(
        CollectionDestination destination)
    {
        var values = Enumerable.Range(1, 257).ToArray();

        var attempt = await CaptureAsync(
            destination,
            values.Length,
            values,
            flushRate: 17);

        attempt.Exception.Should().BeNull();
        attempt.Bytes.Should().Equal(SharpPackSerializer.Serialize(values));
    }

    static async Task<SerializationAttempt> CaptureAsync<T>(
        CollectionDestination destination,
        int count,
        IEnumerable<T> source,
        int flushRate,
        SharpPackSerializerContext? context = null)
    {
        if (destination == CollectionDestination.Stream)
        {
            using var stream = new MemoryStream();
            var exception = await Record.ExceptionAsync(
                async () => await SharpPackStreamingSerializer.SerializeAsync(
                    stream,
                    count,
                    source,
                    flushRate,
                    context));

            return new SerializationAttempt(exception, stream.ToArray());
        }

        var pipe = new Pipe();
        var pipeException = await Record.ExceptionAsync(
            async () => await SharpPackStreamingSerializer.SerializeAsync(
                pipe.Writer,
                count,
                source,
                flushRate,
                context));

        await pipe.Writer.CompleteAsync();
        var bytes = await ReadAllAsync(pipe.Reader);
        await pipe.Reader.CompleteAsync();
        return new SerializationAttempt(pipeException, bytes);
    }

    static async Task<byte[]> ReadAllAsync(PipeReader reader)
    {
        using var stream = new MemoryStream();
        while (true)
        {
            var read = await reader.ReadAsync();
            foreach (var segment in read.Buffer)
            {
                stream.Write(segment.Span);
            }

            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
            {
                return stream.ToArray();
            }
        }
    }

    public enum CollectionDestination
    {
        Pipe,
        Stream,
    }

    readonly record struct SerializationAttempt(
        Exception? Exception,
        byte[] Bytes);

    sealed class TrackingEnumerable<T>(IReadOnlyList<T> values)
        : IEnumerable<T>
    {
        public int GetEnumeratorCalls { get; private set; }

        public int MoveNextCalls { get; private set; }

        public int CurrentReads { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCalls++;
            return new Enumerator(this, values);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        sealed class Enumerator(
            TrackingEnumerable<T> owner,
            IReadOnlyList<T> values)
            : IEnumerator<T>
        {
            int index = -1;

            public T Current
            {
                get
                {
                    owner.CurrentReads++;
                    return values[index];
                }
            }

            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                owner.MoveNextCalls++;
                index++;
                return index < values.Count;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    sealed class KnownCountCollection<T>(int actualCount)
        : ICollection<T>
    {
        public int GetEnumeratorCalls { get; private set; }

        public int Count => actualCount;

        public bool IsReadOnly => true;

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCalls++;
            throw new InvalidOperationException(
                "Known-count mismatch should not enumerate.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(T item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Contains(T item) => false;

        public void CopyTo(T[] array, int arrayIndex)
            => throw new NotSupportedException();

        public bool Remove(T item) => throw new NotSupportedException();
    }

    sealed class ThrowOnSecondMoveNextEnumerable<T>(
        T first,
        Exception exception)
        : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
            => new Enumerator(first, exception);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        sealed class Enumerator(T first, Exception exception)
            : IEnumerator<T>
        {
            int moveNextCalls;

            public T Current => first;

            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                moveNextCalls++;
                if (moveNextCalls == 1)
                {
                    return true;
                }

                throw exception;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }

    sealed class CancelOnWriteStream(CancellationTokenSource cancellation)
        : MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    sealed class CollectionEnumerationException : Exception;
}

public sealed record CountingCollectionItem(int Value);

public sealed class CountingCollectionItemFormatter
    : SharpPackFormatter<CountingCollectionItem>
{
    public int SerializeCalls { get; private set; }

    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref CountingCollectionItem? value)
    {
        SerializeCalls++;
        writer.WriteUnmanaged(value!.Value);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref CountingCollectionItem? value)
    {
        value = new CountingCollectionItem(reader.ReadUnmanaged<int>());
    }
}
