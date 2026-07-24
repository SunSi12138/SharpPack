namespace MemoryPack;

/// <summary>
/// Immutable protocol settings owned by a serializer context.
/// </summary>
public readonly record struct MemoryPackSerializerConfiguration
{
    public static MemoryPackSerializerConfiguration Default => new();

    public static MemoryPackSerializerConfiguration Utf8 => new()
    {
        StringEncoding = MemoryPackStringEncoding.Utf8,
    };

    public static MemoryPackSerializerConfiguration Utf16 => new()
    {
        StringEncoding = MemoryPackStringEncoding.Utf16,
    };

    public MemoryPackStringEncoding StringEncoding { get; init; }
}

public enum MemoryPackStringEncoding : byte
{
    Utf8 = 0,
    Utf16 = 1,
}
