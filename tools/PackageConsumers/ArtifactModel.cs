using SharpPack;

namespace ArtifactFixture;

[SharpPackable, GenerateTypeScript, SharpPackUnion(19, typeof(Delivery))]
public partial interface INotice;

[SharpPackable, GenerateTypeScript]
public partial class Delivery : INotice
{
    public string? DeliveryNote { get; set; }
    public DeliveryState Status { get; set; }
    public int?[][]? ItemGroups { get; set; }
}

public enum DeliveryState : byte { Pending = 1, Sent = 4 }
