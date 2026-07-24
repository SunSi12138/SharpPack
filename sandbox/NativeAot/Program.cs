using SharpPack;

var value = new AotSharpPackModel
{
    Id = 42,
    Name = "NativeAOT 明示",
    Encoded = 99,
    Values = [1, 2, 3, 5, 8],
    RuntimeType = typeof(AotSharpPackModel),
    Item = new AotUnionItem { Value = 123 },
};

var defaultPayload = SharpPackSerializer.Serialize(value);
AssertModel(
    SharpPackSerializer.Deserialize<AotSharpPackModel>(defaultPayload),
    value);

var context = new SharpPackSerializerContext();
var contextPayload = SharpPackSerializer.Serialize(value, context);
if (!defaultPayload.AsSpan().SequenceEqual(contextPayload))
{
    throw new InvalidOperationException(
        "The empty Context changed the SharpPack wire format.");
}
AssertModel(
    SharpPackSerializer.Deserialize<AotSharpPackModel>(
        contextPayload,
        context),
    value);

var utf8Context = new SharpPackSerializerContext(
    SharpPackSerializerConfiguration.Utf8);
var utf8Payload = SharpPackSerializer.Serialize(value, utf8Context);
AssertModel(
    SharpPackSerializer.Deserialize<AotSharpPackModel>(
        utf8Payload,
        utf8Context),
    value);

IAotExternalUnion external = new AotExternalUnionItem { Value = 456 };
var externalPayload = SharpPackSerializer.Serialize(external);
if (SharpPackSerializer.Deserialize<IAotExternalUnion>(externalPayload)
        is not AotExternalUnionItem { Value: 456 })
{
    throw new InvalidOperationException(
        "The generated external-union factory failed under NativeAOT.");
}

IAotClosedExternalUnion<string> closedExternal =
    new AotClosedExternalUnionItem<string> { Value = "closed-aot" };
var closedExternalPayload = SharpPackSerializer.Serialize(closedExternal);
if (SharpPackSerializer.Deserialize<IAotClosedExternalUnion<string>>(
        closedExternalPayload) is not
    AotClosedExternalUnionItem<string> { Value: "closed-aot" })
{
    throw new InvalidOperationException(
        "The generated closed external-union factory failed under NativeAOT.");
}

Console.WriteLine("SharpPack NativeAOT verification passed.");

static void AssertModel(
    AotSharpPackModel? actual,
    AotSharpPackModel expected)
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
            "SharpPack NativeAOT round-trip failed.");
    }
}

[SharpPackable]
public partial class AotSharpPackModel
{
    public int Id { get; set; }

    public string? Name { get; set; }

    [AotPlusOneFormatter]
    public int Encoded { get; set; }

    public List<int> Values { get; set; } = [];

    public Type? RuntimeType { get; set; }

    public AotSharpPackModel? OptionalNext { get; set; }

    public IAotUnion? Item { get; set; }
}

[SharpPackable]
[SharpPackUnion(7, typeof(AotUnionItem))]
public partial interface IAotUnion;

[SharpPackable]
public partial class AotUnionItem : IAotUnion
{
    public int Value { get; set; }
}

public sealed class AotPlusOneFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + 1);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - 1;
    }
}

public sealed class AotPlusOneFormatterAttribute
    : SharpPackCustomFormatterAttribute<AotPlusOneFormatter, int>
{
    public override AotPlusOneFormatter GetFormatter() => new();
}

[SharpPackable(GenerateType.NoGenerate)]
public partial interface IAotExternalUnion;

[SharpPackable]
public partial class AotExternalUnionItem : IAotExternalUnion
{
    public int Value { get; set; }
}

[SharpPackUnionFormatter(typeof(IAotExternalUnion))]
[SharpPackUnion(3, typeof(AotExternalUnionItem))]
public partial class AotExternalUnionFormatter;

[SharpPackable(GenerateType.NoGenerate)]
public partial interface IAotClosedExternalUnion<T>;

[SharpPackable]
public partial class AotClosedExternalUnionItem<T>
    : IAotClosedExternalUnion<T>
{
    public T? Value { get; set; }
}

[SharpPackUnionFormatter(typeof(IAotClosedExternalUnion<string>))]
[SharpPackUnion(4, typeof(AotClosedExternalUnionItem<string>))]
public partial class AotClosedExternalUnionFormatter;
