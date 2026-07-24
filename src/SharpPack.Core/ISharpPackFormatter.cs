using SharpPack.Internal;
using System.Buffers;

namespace SharpPack;

[Preserve]
public interface ISharpPackFormatter<T>
{
    [Preserve]
    void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    [Preserve]
    void Deserialize(ref SharpPackReader reader, scoped ref T? value);
}

[Preserve]
public abstract class SharpPackFormatter<T> : ISharpPackFormatter<T>
{
    internal virtual bool HasFormatterOverrideDependency(FormatterGraph graph)
        => false;

    [Preserve]
    public abstract void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    [Preserve]
    public abstract void Deserialize(ref SharpPackReader reader, scoped ref T? value);
}
