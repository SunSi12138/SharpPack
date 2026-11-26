using SharpPack;

// Two ordinary objects with one Int32 each, in a managed array.
// Keep this app free of any Serialize call, model construction, or explicit
// formatter root. It exercises the same call a packaged app makes.
byte[] payload = [2, 0, 0, 0, 1, 17, 0, 0, 0, 1, 29, 0, 0, 0];
if (SharpPackSerializer.Deserialize<ArrayOnlyItem[]>(payload) is not [{ Value: 17 }, { Value: 29 }])
    throw new InvalidOperationException("The deserialize-only native array lost its generated elements.");
Console.WriteLine("The deserialize-only native array returned both values.");

[SharpPackable]
public partial class ArrayOnlyItem
{
    public int Value { get; set; }
}
