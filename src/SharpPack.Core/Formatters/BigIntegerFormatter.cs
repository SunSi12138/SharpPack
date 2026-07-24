using SharpPack.Internal;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpPack.Formatters;

[Preserve]
public sealed class BigIntegerFormatter : SharpPackFormatter<BigInteger>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref BigInteger value)
    {
        Span<byte> temp = stackalloc byte[255];
        if (value.TryWriteBytes(temp, out var written))
        {
            writer.WriteUnmanagedSpan(temp[..written]);
            return;
        }
        else
        {
            var byteArray = value.ToByteArray();
            writer.WriteUnmanagedArray(byteArray);
        }
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref BigInteger value)
    {
        if (!reader.TryReadCollectionHeader(out var length))
        {
            value = default;
            return;
        }

        ref var src = ref reader.GetSpanReference(length);
        value = new BigInteger(MemoryMarshal.CreateReadOnlySpan(ref src, length));

        reader.Advance(length);
    }
}
