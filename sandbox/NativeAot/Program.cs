using MemoryPack;

var value = new AotMemoryPackModel
{
    Id = 42,
    Name = "NativeAOT 明示",
    Encoded = 99,
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

IAotExternalUnion external = new AotExternalUnionItem { Value = 456 };
var externalPayload = MemoryPackSerializer.Serialize(external);
if (MemoryPackSerializer.Deserialize<IAotExternalUnion>(externalPayload)
        is not AotExternalUnionItem { Value: 456 })
{
    throw new InvalidOperationException(
        "The generated external-union factory failed under NativeAOT.");
}

Console.WriteLine("MemoryPack NativeAOT verification passed.");

static void AssertModel(
    AotMemoryPackModel? actual,
    AotMemoryPackModel expected)
{
    if (actual is null ||
        actual.Id != expected.Id ||
        actual.Name != expected.Name ||
        actual.Encoded != expected.Encoded ||
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

    [AotPlusOneFormatter]
    public int Encoded { get; set; }

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

public sealed class AotPlusOneFormatter : MemoryPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + 1);

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - 1;
    }
}

public sealed class AotPlusOneFormatterAttribute
    : MemoryPackCustomFormatterAttribute<AotPlusOneFormatter, int>
{
    public override AotPlusOneFormatter GetFormatter() => new();
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IAotExternalUnion;

[MemoryPackable]
public partial class AotExternalUnionItem : IAotExternalUnion
{
    public int Value { get; set; }
}

[MemoryPackUnionFormatter(typeof(IAotExternalUnion))]
[MemoryPackUnion(3, typeof(AotExternalUnionItem))]
public partial class AotExternalUnionFormatter;
