namespace SharpPack.Tests.Models;

[SharpPackable]
partial class DefaultValuePlaceholder
{
    public int X { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant, SerializeLayout.Sequential)]
partial class DefaultValuePlaceholderWithVersionTolerant
{
    public int X { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant, SerializeLayout.Sequential)]
partial class HasDefaultValueWithVersionTolerant
{
    public int X;

    public int Y = 12345;
    public float Z { get; set; } = 678.9f;

    [SuppressDefaultInitialization]
    public int Y2 = 12345;

    [SuppressDefaultInitialization]
    public float Z2 { get; set; } = 678.9f;
}

[SharpPackable]
partial class HasDefaultValue
{
    public int X;

    public int Y = 12345;
    public float Z { get; set; } = 678.9f;

    [SuppressDefaultInitialization]
    public int Y2 = 12345;

    [SuppressDefaultInitialization]
    public float Z2 { get; set; } = 678.9f;
}
