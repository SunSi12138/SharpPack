using MemoryPack.Streaming;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MemoryPack.Tests.Streaming;

public class StreamingSerializer
{
    [Fact]
    public async Task Serialize()
    {
        var seq = Enumerable.Range(1, 10000).ToArray();

        {
            var ms = new MemoryStream();
            await MemoryPackStreamingSerializer.SerializeAsync(ms, seq.Length, seq);
            var v2 = MemoryPackSerializer.Deserialize<int[]>(ms.ToArray());

            v2.Should().Equal(seq);
        }

        {
            var pipe = new Pipe();

            await MemoryPackStreamingSerializer.SerializeAsync(pipe.Writer, seq.Length, seq);

            await pipe.Writer.CompleteAsync();

            pipe.Reader.TryRead(out var result);

            result.IsCompleted.Should().BeTrue();
            var v2 = MemoryPackSerializer.Deserialize<int[]>(result.Buffer);

            v2.Should().Equal(seq);
        }
    }

    [Fact]
    public async Task Deserialize()
    {
        var seq = Enumerable.Range(1, 10000).ToArray();
        var bin = MemoryPackSerializer.Serialize(seq);

        {
            var ms = new MemoryStream(bin);

            var list = new List<int>();
            await foreach (var item in MemoryPackStreamingSerializer.DeserializeAsync<int>(ms))
            {
                list.Add(item);
            }

            list.Should().Equal(seq);
        }




    }

    [Fact]
    public async Task SingleMessagePipeApi_IsZeroCopyAndFrameBounded()
    {
        var context = new MemoryPackSerializerContext();
        var value = new SampleClassForMemoryPack(42, "rpc");
        var expected = MemoryPackSerializer.Serialize(value, context);
        var pipe = new Pipe();

        var written = await MemoryPackStreamingSerializer.SerializeFrameAsync(
            pipe.Writer,
            value,
            context);
        pipe.Writer.Write(new byte[] { 0xCA, 0xFE });
        await pipe.Writer.FlushAsync();

        written.Should().Be(expected.Length);
        var restored = await MemoryPackStreamingSerializer.DeserializeFrameAsync<SampleClassForMemoryPack>(
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
        var payload = MemoryPackSerializer.Serialize(123);
        var pipe = new Pipe();
        pipe.Writer.Write(payload);
        pipe.Writer.Write(new byte[] { 0x00 });
        await pipe.Writer.FlushAsync();

        await Assert.ThrowsAsync<MemoryPackSerializationException>(
            async () => await MemoryPackStreamingSerializer.DeserializeFrameAsync<int>(
                pipe.Reader,
                payload.Length + 1));
    }

}


[MemoryPackable]
public partial class SampleClassForMemoryPack
{
    public int Id { get; set; }
    public string Name { get; set; }



    public SampleClassForMemoryPack(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public bool Equals(SampleClassForMemoryPack? other)
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

        return Equals((SampleClassForMemoryPack)obj);
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
