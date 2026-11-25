using SharpPack.Internal;
using System.Runtime.CompilerServices;

namespace SharpPack.Formatters;


[Preserve]
public sealed class SharpPackableFormatter<T> : SharpPackFormatter<T>
    where T : ISharpPackable<T>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        T.Serialize(ref writer, ref Unsafe.AsRef(in value));
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref T? value)
    {
        T.Deserialize(ref reader, ref value);
    }
}
