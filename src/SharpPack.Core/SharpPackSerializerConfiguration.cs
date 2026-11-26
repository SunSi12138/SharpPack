namespace SharpPack;

/// <summary>
/// Immutable protocol settings owned by a serializer context.
/// </summary>
public readonly record struct SharpPackSerializerConfiguration
{
    public static SharpPackSerializerConfiguration Default => new();

    public static SharpPackSerializerConfiguration Utf8 => new()
    {
        StringEncoding = SharpPackStringEncoding.Utf8,
    };

    public static SharpPackSerializerConfiguration Utf16 => new()
    {
        StringEncoding = SharpPackStringEncoding.Utf16,
    };

    public SharpPackStringEncoding StringEncoding { get; init; }

    public TypeSerializationMode TypeSerializationMode { get; init; }

    public SharpPackReadLimits ReadLimits { get; init; }

    /// <summary>
    /// Maximum accepted top-level payload size for known-length reads.
    /// Zero preserves the current unlimited behavior.
    /// </summary>
    public int MaxPayloadBytes { get; init; }
}

public enum SharpPackStringEncoding : byte
{
    Utf8 = 0,
    Utf16 = 1,
}

/// <summary>Controls the context-owned System.Type payload capability.</summary>
public enum TypeSerializationMode : byte
{
    Disabled = 0,
    RegisteredOnly = 1,
}
