using System.Buffers;
using MemoryPack;

// require this unused line for reproduce error?
var bufferWriter = new ArrayBufferWriter<byte>();

var mc = new MemPackObject();

var formatter = new MemoryPackableFormatter2<MemPackObject>();
formatter.Serialize<ArrayBufferWriter<byte>>(ref mc);

var memoryPackValue = new AotMemoryPackModel
{
    Id = 42,
    Name = "NativeAOT",
};
var context = new MemoryPackSerializerContextBuilder()
    .RegisterFactory<AotMemoryPackModel, AotMemoryPackModel>()
    .Build();
var payload = MemoryPackSerializer.Serialize(memoryPackValue, context);
var memoryPackRoundTrip =
    MemoryPackSerializer.Deserialize<AotMemoryPackModel>(payload, context);
if (memoryPackRoundTrip is null ||
    memoryPackRoundTrip.Id != memoryPackValue.Id ||
    memoryPackRoundTrip.Name != memoryPackValue.Name)
{
    throw new InvalidOperationException("MemoryPack NativeAOT round-trip failed.");
}


public interface IMemoryPackable2<T>
{
    static abstract void Serialize<TBufferWriter>(scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
}

public interface IMemoryPackFormatter2<T>
{
    void Serialize<TBufferWriter>(scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
}

public abstract class MemoryPackFormatter2<T> : IMemoryPackFormatter2<T>
{
    public abstract void Serialize<TBufferWriter>(scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
}

public sealed class MemoryPackableFormatter2<T> : MemoryPackFormatter2<T>
    where T : IMemoryPackable2<T>
{
    public override void Serialize<TBufferWriter>(scoped ref T? value)
    {
        Console.WriteLine("Before");
        T.Serialize<TBufferWriter>(ref value);
        Console.WriteLine("After");
    }
}

public class MemPackObject : IMemoryPackable2<MemPackObject>
{
    public static void Serialize<TBufferWriter>(scoped ref MemPackObject? value)
          where TBufferWriter : IBufferWriter<byte>
    {
        Console.WriteLine("OK");
    }
}

[MemoryPackable]
public partial class AotMemoryPackModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
}
