namespace SharpPack.Generator;

// should synchronize with SharpPack.Core.Attributes.cs GenerateType
public enum GenerateType
{
    Object,
    VersionTolerant,
    CircularReference,
    Collection,
    NoGenerate,

    // only used in Generator
    Union
}

public enum SerializeLayout
{
    Sequential, // default
    Explicit
}
