using System.Globalization;
using SharpPack.Internal;

namespace SharpPack.Formatters;

[Preserve]
public sealed class CultureInfoFormatter : SharpPackFormatter<CultureInfo>
{
    // treat as a string(Name).

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref CultureInfo? value)
    {
        writer.WriteString(value?.Name);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref CultureInfo? value)
    {
        var str = reader.ReadString();
        if (str == null)
        {
            value = null;
        }
        else
        {
            value = CultureInfo.GetCultureInfo(str);
        }
    }
}