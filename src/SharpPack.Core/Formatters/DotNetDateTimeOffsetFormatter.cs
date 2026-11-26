using SharpPack.Internal;

namespace SharpPack.Formatters;

/// <summary>
/// Reads 16-byte little-endian <see cref="DateTimeOffset"/> values written on
/// .NET 9 and .NET 10 or later. Writes the .NET 10 layout with zero padding.
/// </summary>
[Preserve]
public sealed class DotNetDateTimeOffsetFormatter : SharpPackFormatter<DateTimeOffset>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref DateTimeOffset value)
    {
        var offsetMinutes = (int)(value.Offset.Ticks / TimeSpan.TicksPerMinute);
        writer.WriteUnmanaged(offsetMinutes, 0, value.UtcTicks);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref DateTimeOffset value)
    {
        reader.ReadUnmanaged(out int offsetSlot, out int _, out long utcTicks);
        // Older .NET stored a signed 16-bit offset; the upper bits were padding.
        // Valid offsets in both layouts fit in the signed low 16 bits.
        var offset = TimeSpan.FromMinutes(unchecked((short)offsetSlot));
        value = new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(offset);
    }
}
