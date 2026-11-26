using SharpPack;

[SharpPackable]
public partial class PackageModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Envelope
{
    [SharpPackOrder(0)]
    public PackageModel? Payload { get; set; }
}
