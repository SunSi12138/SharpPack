using System;

namespace MemoryPack.Tests;

public class ReentrancyTest
{
    [Fact]
    public void NestedSerializerCalls_UseIndependentThreadState()
    {
        var formatter = new ReentrantEnvelopeFormatter();
        var context = new MemoryPackSerializerContextBuilder()
            .Register(formatter)
            .Build();
        formatter.Context = context;

        var value = new ReentrantEnvelope
        {
            Nested = new ReentrantNested
            {
                Id = 123,
                Name = "nested",
            },
        };

        var payload = MemoryPackSerializer.Serialize(value, context);
        var decoded = MemoryPackSerializer.Deserialize<ReentrantEnvelope>(
            payload,
            context);

        decoded.Should().BeEquivalentTo(value);
    }
}

public sealed class ReentrantEnvelope
{
    public ReentrantNested? Nested { get; set; }
}

[MemoryPackable]
public partial class ReentrantNested
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public sealed class ReentrantEnvelopeFormatter
    : MemoryPackFormatter<ReentrantEnvelope>
{
    public MemoryPackSerializerContext Context { get; set; } = null!;

    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref ReentrantEnvelope? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        var nestedPayload = MemoryPackSerializer.Serialize(
            value.Nested,
            Context);
        writer.WriteUnmanagedArray(nestedPayload);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref ReentrantEnvelope? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1)
        {
            MemoryPackSerializationException.ThrowInvalidPropertyCount(1, count);
        }

        var nestedPayload = reader.ReadUnmanagedArray<byte>()!;
        value = new ReentrantEnvelope
        {
            Nested = MemoryPackSerializer.Deserialize<ReentrantNested>(
                nestedPayload,
                Context),
        };
    }
}
