namespace SharpPack;

/// <summary>
/// Immutable settings owned by a serializer context.
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

    public TypeResolutionMode TypeResolutionMode { get; init; }
}

public enum TypeResolutionMode : byte
{
    GlobalCompatibility = 0,
    ContextCatalogOnly = 1,
    Disabled = 2,
}

public enum SharpPackStringEncoding : byte
{
    Utf8 = 0,
    Utf16 = 1,
}
