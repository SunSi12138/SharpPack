using System;

namespace SharpPack.Tests;

public class ReentrancyTest
{
    [Fact]
    public void NestedSerializerCalls_UseIndependentThreadState()
    {
        var formatter = new ReentrantEnvelopeFormatter();
        var context = new SharpPackSerializerContextBuilder()
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

        var payload = SharpPackSerializer.Serialize(value, context);
        var decoded = SharpPackSerializer.Deserialize<ReentrantEnvelope>(
            payload,
            context);

        decoded.Should().BeEquivalentTo(value);

        var secondPayload = SharpPackSerializer.Serialize(value, context);
        SharpPackSerializer.Deserialize<ReentrantEnvelope>(
            secondPayload,
            context).Should().BeEquivalentTo(value);
    }
}

public sealed class ReentrantEnvelope
{
    public ReentrantNested? Nested { get; set; }
}

[SharpPackable]
public partial class ReentrantNested
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public sealed class ReentrantEnvelopeFormatter
    : SharpPackFormatter<ReentrantEnvelope>
{
    public SharpPackSerializerContext Context { get; set; } = null!;

    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref ReentrantEnvelope? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        var nestedPayload = SharpPackSerializer.Serialize(
            value.Nested,
            Context);
        writer.WriteUnmanagedArray(nestedPayload);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref ReentrantEnvelope? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1)
        {
            SharpPackSerializationException.ThrowInvalidPropertyCount(1, count);
        }

        var nestedPayload = reader.ReadUnmanagedArray<byte>()!;
        value = new ReentrantEnvelope
        {
            Nested = SharpPackSerializer.Deserialize<ReentrantNested>(
                nestedPayload,
                Context),
        };
    }
}
