using SharpPack.Internal;

namespace SharpPack.Formatters;

[Preserve]
public sealed class VersionFormatter : SharpPackFormatter<Version>
{
    // Serialize as [Major, Minor, Build, Revision]

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Version? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteUnmanagedWithObjectHeader(4, value.Major, value.Minor, value.Build, value.Revision);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Version? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 4) SharpPackSerializationException.ThrowInvalidPropertyCount(4, count);

        reader.ReadUnmanaged(out int major, out int minor, out int build, out int revision);

        // when use new Version(major, minor), build and revision will be -1, it can not use constructor.
        if (revision == -1)
        {
            if (build == -1)
            {
                value = new Version(major, minor);
            }
            else
            {
                value = new Version(major, minor, build);
            }
        }
        else
        {
            value = new Version(major, minor, build, revision);
        }
    }
}
