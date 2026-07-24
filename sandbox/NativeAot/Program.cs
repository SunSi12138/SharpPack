using MemoryPack;

var value = new AotMemoryPackModel
{
    Id = 42,
    Name = "NativeAOT 明示",
    Values = [1, 2, 3, 5, 8],
    RuntimeType = typeof(AotMemoryPackModel),
    Item = new AotUnionItem { Value = 123 },
};

var defaultPayload = MemoryPackSerializer.Serialize(value);
AssertModel(
    MemoryPackSerializer.Deserialize<AotMemoryPackModel>(defaultPayload),
    value);

var context = new MemoryPackSerializerContext();
var contextPayload = MemoryPackSerializer.Serialize(value, context);
if (!defaultPayload.AsSpan().SequenceEqual(contextPayload))
{
    throw new InvalidOperationException(
        "The empty Context changed the MemoryPack wire format.");
}
AssertModel(
    MemoryPackSerializer.Deserialize<AotMemoryPackModel>(
        contextPayload,
        context),
    value);

var utf8Context = new MemoryPackSerializerContext(
    MemoryPackSerializerConfiguration.Utf8);
var utf8Payload = MemoryPackSerializer.Serialize(value, utf8Context);
AssertModel(
    MemoryPackSerializer.Deserialize<AotMemoryPackModel>(
        utf8Payload,
        utf8Context),
    value);

Console.WriteLine("MemoryPack NativeAOT verification passed.");

static void AssertModel(
    AotMemoryPackModel? actual,
    AotMemoryPackModel expected)
{
    if (actual is null ||
        actual.Id != expected.Id ||
        actual.Name != expected.Name ||
        !actual.Values.SequenceEqual(expected.Values) ||
        actual.RuntimeType != expected.RuntimeType ||
        actual.OptionalNext is not null ||
        actual.Item is not AotUnionItem union ||
        union.Value != ((AotUnionItem)expected.Item!).Value)
    {
        throw new InvalidOperationException(
            "MemoryPack NativeAOT round-trip failed.");
    }
}

[MemoryPackable]
public partial class AotMemoryPackModel
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public List<int> Values { get; set; } = [];

    public Type? RuntimeType { get; set; }

    public AotMemoryPackModel? OptionalNext { get; set; }

    public IAotUnion? Item { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(7, typeof(AotUnionItem))]
public partial interface IAotUnion;

[MemoryPackable]
public partial class AotUnionItem : IAotUnion
{
    public int Value { get; set; }
}
