using SharpPack.Streaming;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPack.Tests.Streaming;

public class StreamingSerializer
{
    [Fact]
    public async Task Serialize()
    {
        var seq = Enumerable.Range(1, 10000).ToArray();

        {
            var ms = new MemoryStream();
            await SharpPackStreamingSerializer.SerializeAsync(ms, seq.Length, seq);
            var v2 = SharpPackSerializer.Deserialize<int[]>(ms.ToArray());

            v2.Should().Equal(seq);
        }

        {
            var pipe = new Pipe();

            await SharpPackStreamingSerializer.SerializeAsync(pipe.Writer, seq.Length, seq);

            await pipe.Writer.CompleteAsync();

            pipe.Reader.TryRead(out var result);

            result.IsCompleted.Should().BeTrue();
            var v2 = SharpPackSerializer.Deserialize<int[]>(result.Buffer);

            v2.Should().Equal(seq);
        }
    }

    [Fact]
    public async Task Deserialize()
    {
        var seq = Enumerable.Range(1, 10000).ToArray();
        var bin = SharpPackSerializer.Serialize(seq);

        {
            var ms = new MemoryStream(bin);

            var list = new List<int>();
            await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(ms))
            {
                list.Add(item);
            }

            list.Should().Equal(seq);
        }

    }

    [Fact]
    public async Task DeserializeHandlesEmptyAndFinalPartialBatch()
    {
        foreach (var expected in new[] { Array.Empty<int>(), [1, 2, 3, 4, 5] })
        {
            var pipe = new Pipe();
            pipe.Writer.Write(SharpPackSerializer.Serialize(expected));
            await pipe.Writer.CompleteAsync();

            var actual = new List<int>();
            await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(
                pipe.Reader,
                bufferAtLeast: 4,
                readMinimumSize: 4))
            {
                actual.Add(item);
            }

            actual.Should().Equal(expected);
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task DeserializeHandlesByteAtATimePipe()
    {
        var expected = Enumerable.Range(0, 257).ToArray();
        var payload = SharpPackSerializer.Serialize(expected);
        var pipe = new Pipe();

        var producer = Task.Run(async () =>
        {
            foreach (var value in payload)
            {
                pipe.Writer.Write([value]);
                await pipe.Writer.FlushAsync();
            }

            await pipe.Writer.CompleteAsync();
        });

        var actual = new List<int>();
        await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(
            pipe.Reader,
            bufferAtLeast: 4,
            readMinimumSize: 4))
        {
            actual.Add(item);
        }

        await producer;
        actual.Should().Equal(expected);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeHonorsCancellationAndEarlyTermination()
    {
        var canceledPipe = new Pipe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in SharpPackStreamingSerializer.DeserializeAsync<int>(
                canceledPipe.Reader,
                cancellationToken: cancellation.Token))
            {
            }
        });
        await canceledPipe.Reader.CompleteAsync();
        await canceledPipe.Writer.CompleteAsync();

        var pipe = new Pipe();
        pipe.Writer.Write(SharpPackSerializer.Serialize(
            Enumerable.Range(0, 100).ToArray()));
        await pipe.Writer.CompleteAsync();

        await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(
            pipe.Reader,
            bufferAtLeast: 4,
            readMinimumSize: 4))
        {
            item.Should().Be(0);
            break;
        }

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializePreservesTrailingBytesAndCallerOwnership()
    {
        var payload = SharpPackSerializer.Serialize(new[] { 1, 2, 3 });
        var pipe = new Pipe();
        pipe.Writer.Write(payload);
        pipe.Writer.Write(new byte[] { 0xCA, 0xFE });
        await pipe.Writer.CompleteAsync();

        var actual = new List<int>();
        await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(
            pipe.Reader,
            bufferAtLeast: 4,
            readMinimumSize: 4))
        {
            actual.Add(item);
        }

        actual.Should().Equal(1, 2, 3);
        var trailing = await pipe.Reader.ReadAsync();
        trailing.Buffer.ToArray().Should().Equal(0xCA, 0xFE);
        pipe.Reader.AdvanceTo(trailing.Buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeBalancesReadWhenConsumerStopsEarly()
    {
        var payload = SharpPackSerializer.Serialize(
            Enumerable.Range(0, 100).ToArray());
        var pipe = new Pipe();
        pipe.Writer.Write(payload);
        pipe.Writer.Write(new byte[] { 0xCA, 0xFE });
        await pipe.Writer.CompleteAsync();

        await foreach (var item in SharpPackStreamingSerializer.DeserializeAsync<int>(
            pipe.Reader,
            bufferAtLeast: 4,
            readMinimumSize: 4))
        {
            item.Should().Be(0);
            break;
        }

        var trailing = await pipe.Reader.ReadAsync();
        trailing.Buffer.ToArray().Should().Equal(0xCA, 0xFE);
        pipe.Reader.AdvanceTo(trailing.Buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeTurnsCanceledReadResultIntoCancellation()
    {
        var pipe = new Pipe();
        await using var enumerator = SharpPackStreamingSerializer
            .DeserializeAsync<int>(
                pipe.Reader,
                bufferAtLeast: 4,
                readMinimumSize: 4)
            .GetAsyncEnumerator();

        var moveNext = enumerator.MoveNextAsync().AsTask();
        pipe.Reader.CancelPendingRead();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNext);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeRejectsTruncatedFinalItem()
    {
        var payload = SharpPackSerializer.Serialize(new[] { 1, 2, 3 });
        var pipe = new Pipe();
        pipe.Writer.Write(payload.AsSpan(0, payload.Length - 1));
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(async () =>
        {
            await foreach (var _ in SharpPackStreamingSerializer.DeserializeAsync<int>(
                pipe.Reader,
                bufferAtLeast: 4,
                readMinimumSize: 4))
            {
            }
        });

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeRegistersFreshContextRootType()
    {
        var expected = new[]
        {
            new StreamingTypeValue
            {
                Value = typeof(StreamingSerializer),
            },
        };
        var pipe = new Pipe();
        pipe.Writer.Write(SharpPackSerializer.Serialize(expected));
        await pipe.Writer.CompleteAsync();

        var context = new SharpPackSerializerContext();
        var actual = new List<StreamingTypeValue?>();
        await foreach (var item in SharpPackStreamingSerializer
                           .DeserializeAsync<StreamingTypeValue>(
                               pipe.Reader,
                               bufferAtLeast: 4,
                               readMinimumSize: 4,
                               context))
        {
            actual.Add(item);
        }

        actual.Should().ContainSingle();
        actual[0]!.Value.Should().Be(typeof(StreamingSerializer));
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task SingleMessagePipeApi_IsZeroCopyAndFrameBounded()
    {
        var context = new SharpPackSerializerContext();
        var value = new SampleClassForSharpPack(42, "rpc");
        var expected = SharpPackSerializer.Serialize(value, context);
        var pipe = new Pipe();

        var written = await SharpPackStreamingSerializer.SerializeFrameAsync(
            pipe.Writer,
            value,
            context);
        pipe.Writer.Write(new byte[] { 0xCA, 0xFE });
        await pipe.Writer.FlushAsync();

        written.Should().Be(expected.Length);
        var restored = await SharpPackStreamingSerializer.DeserializeFrameAsync<SampleClassForSharpPack>(
            pipe.Reader,
            written,
            context);

        restored.Should().Be(value);
        var remainder = await pipe.Reader.ReadAsync();
        remainder.Buffer.ToArray().Should().Equal(0xCA, 0xFE);
        pipe.Reader.AdvanceTo(remainder.Buffer.End);
    }

    [Fact]
    public async Task SingleMessagePipeApi_RejectsTrailingFrameBytes()
    {
        var payload = SharpPackSerializer.Serialize(123);
        var pipe = new Pipe();
        pipe.Writer.Write(payload);
        pipe.Writer.Write(new byte[] { 0x00 });
        await pipe.Writer.FlushAsync();

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackStreamingSerializer.DeserializeFrameAsync<int>(
                pipe.Reader,
                payload.Length + 1));
    }
}


[SharpPackable]
public partial class SampleClassForSharpPack
{
    public int Id { get; set; }
    public string Name { get; set; }



    public SampleClassForSharpPack(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public bool Equals(SampleClassForSharpPack? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Id == other.Id && Name == other.Name;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((SampleClassForSharpPack)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }

    public override string ToString()
    {
        return $"{Id}-{Name}";
    }
}

[SharpPackable]
public partial class StreamingTypeValue
{
    public Type? Value { get; set; }
}
