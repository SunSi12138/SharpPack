using MemoryPack.Internal;
using System.Buffers;

namespace MemoryPack;

[Preserve]
public interface IMemoryPackFormatter<T>
{
    [Preserve]
    void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    [Preserve]
    void Deserialize(ref MemoryPackReader reader, scoped ref T? value);
}

[Preserve]
public abstract class MemoryPackFormatter<T> : IMemoryPackFormatter<T>
{
    [Preserve]
    public abstract void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    [Preserve]
    public abstract void Deserialize(ref MemoryPackReader reader, scoped ref T? value);
}
