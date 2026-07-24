using SharpPack.Internal;
using System.Buffers;

namespace SharpPack.Formatters;

[Preserve]
public sealed class TimeZoneInfoFormatter : SharpPackFormatter<TimeZoneInfo>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref TimeZoneInfo? value)
    {
        writer.WriteString(value?.ToSerializedString());
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref TimeZoneInfo? value)
    {
        var source = reader.ReadString();
        if (source == null)
        {
            value = null;
            return;
        }

        value = TimeZoneInfo.FromSerializedString(source);
    }
}
