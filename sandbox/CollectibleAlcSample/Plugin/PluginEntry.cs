using SharpPack;

namespace SharpPack.CollectibleAlcPlugin;

public static class PluginEntry
{
    public static bool Run(SharpPackSerializerContext context)
    {
        var value = new PluginMessage
        {
            Id = 42,
            Text = "loaded from a collectible plugin"
        };

        var payload = SharpPackSerializer.Serialize(value, context);
        var restored =
            SharpPackSerializer.Deserialize<PluginMessage>(payload, context);

        return restored is
        {
            Id: 42,
            Text: "loaded from a collectible plugin"
        };
    }
}

[SharpPackable]
public partial class PluginMessage
{
    public int Id { get; set; }

    public string? Text { get; set; }
}
