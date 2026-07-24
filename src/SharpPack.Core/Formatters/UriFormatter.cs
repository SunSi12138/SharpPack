using SharpPack.Internal;
using System.Buffers;

namespace SharpPack.Formatters;

[Preserve]
public sealed class UriFormatter : SharpPackFormatter<Uri>
{
    // treat as a string(OriginalString).

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Uri? value)
    {
        writer.WriteString(value?.OriginalString);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Uri? value)
    {
        var str = reader.ReadString();
        if (str == null)
        {
            value = null;
        }
        else
        {
            value = new Uri(str, UriKind.RelativeOrAbsolute);
        }
    }
}
