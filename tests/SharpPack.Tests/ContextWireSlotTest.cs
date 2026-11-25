namespace SharpPack.Tests;

public class ContextWireSlotTest
{
    [Fact]
    public void DiscardedSequentialMemberDoesNotSelectAContextFormatter()
    {
        var formatter = new CountingIntFormatter();
        var context = new SharpPackSerializerContextBuilder().Register(formatter).Build();
        var value = new ContextWireSlotModel { Value = "kept", Ignored = 42 };

        var payload = SharpPackSerializer.Serialize(value, context);
        var decoded = SharpPackSerializer.Deserialize<ContextWireSlotModel>(payload, context)!;
        Assert.Equal(SharpPackSerializer.Serialize(value), payload);
        Assert.Equal("kept", decoded.Value);
        Assert.Equal(0, decoded.Ignored);
        Assert.Equal(0, formatter.Calls);
    }

    sealed class CountingIntFormatter : SharpPackFormatter<int>
    {
        public int Calls { get; private set; }

        public override void Serialize<TBufferWriter>(
            ref SharpPackWriter<TBufferWriter> writer, scoped ref int value)
        {
            Calls++;
            writer.WriteUnmanaged(value);
        }

        public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
        {
            Calls++;
            reader.ReadUnmanaged(out value);
        }
    }
}

[SharpPackable(GenerateType.VersionTolerant, SerializeLayout.Sequential)]
public partial class ContextWireSlotModel
{
    [SharpPackOrder(0), Utf8StringFormatter] public string? Value;
    [SharpPackOrder(0)] public int Ignored;
}
