using SharpPack;
using SharpPack.Formatters;

if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    // Root the closed collection formatters used directly as serializer root
    // types. Runtime shape discovery cannot manufacture native generic code.
    GC.KeepAlive(new ArrayFormatter<
        AotUnmanagedWrapper<AotUnmanagedFormatted>>());
    GC.KeepAlive(new ArrayFormatter<
        AotUnmanagedWrapper<int>>());
    GC.KeepAlive(new ListFormatter<int>());
    GC.KeepAlive(new ListFormatter<
        AotUnmanagedWrapper<AotUnmanagedFormatted>>());
}

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

var exactValue = new AotExactSizeModel
{
    Id = 7,
    Name = "exact-aot",
    Payload = [1, 2, 3, 4],
};
var exactPayload = SharpPackSerializer.Serialize(exactValue);
var exactRoundTrip =
    SharpPackSerializer.Deserialize<AotExactSizeModel>(exactPayload);
if (exactRoundTrip is not
    { Id: 7, Name: "exact-aot", Payload: [1, 2, 3, 4] })
{
    throw new InvalidOperationException(
        "The generated exact-size serializer failed under NativeAOT.");
}

var unmanagedFormatted = new AotUnmanagedFormatted
{
    Value = 30_000,
    Tail = 123_456_789,
};
AotVarIntFormatter.Reset();
var unmanagedPayload = SharpPackSerializer.Serialize(unmanagedFormatted);
var unmanagedRoundTrip =
    SharpPackSerializer.Deserialize<AotUnmanagedFormatted>(
        unmanagedPayload);
if (unmanagedPayload.Length !=
        System.Runtime.CompilerServices.Unsafe.SizeOf<
            AotUnmanagedFormatted>() ||
    unmanagedRoundTrip.Value != unmanagedFormatted.Value ||
    unmanagedRoundTrip.Tail != unmanagedFormatted.Tail ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The unmanaged raw-copy contract failed under NativeAOT.");
}

var closedFormatted =
    new AotUnmanagedWrapper<AotUnmanagedFormatted>
    {
        Value = unmanagedFormatted,
    };
var closedFormattedPayload = SharpPackSerializer.Serialize(closedFormatted);
var closedFormattedRoundTrip = SharpPackSerializer.Deserialize<
    AotUnmanagedWrapper<AotUnmanagedFormatted>>(closedFormattedPayload);
if (closedFormattedPayload.Length !=
        System.Runtime.CompilerServices.Unsafe.SizeOf<
            AotUnmanagedWrapper<AotUnmanagedFormatted>>() ||
    closedFormattedRoundTrip.Value.Value !=
        closedFormatted.Value.Value ||
    closedFormattedRoundTrip.Value.Tail !=
        closedFormatted.Value.Tail)
{
    throw new InvalidOperationException(
        "The closed unmanaged raw-copy policy failed under NativeAOT.");
}

var closedPlain = new AotUnmanagedWrapper<int> { Value = 42 };
var closedPlainPayload = SharpPackSerializer.Serialize(closedPlain);
if (closedPlainPayload.Length !=
        System.Runtime.CompilerServices.Unsafe.SizeOf<
            AotUnmanagedWrapper<int>>() ||
    SharpPackSerializer.Deserialize<AotUnmanagedWrapper<int>>(
        closedPlainPayload).Value != closedPlain.Value)
{
    throw new InvalidOperationException(
        "The plain closed unmanaged type lost its raw path under NativeAOT.");
}

AotVarIntFormatter.Reset();
var genericFormattedClass = new AotGenericContainer<
    AotUnmanagedFormatted>
{
    Value = unmanagedFormatted,
};
var genericFormattedClassPayload = SharpPackSerializer.Serialize(
    genericFormattedClass);
var genericFormattedClassRoundTrip = SharpPackSerializer.Deserialize<
    AotGenericContainer<AotUnmanagedFormatted>>(
        genericFormattedClassPayload);
if (genericFormattedClassRoundTrip?.Value.Value !=
        unmanagedFormatted.Value ||
    genericFormattedClassRoundTrip.Value.Tail !=
        unmanagedFormatted.Tail ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The generic class did not preserve unmanaged raw-copy under NativeAOT.");
}

AotVarIntFormatter.Reset();
var genericPlainClass = new AotGenericContainer<int> { Value = 42 };
var genericPlainClassPayload = SharpPackSerializer.Serialize(
    genericPlainClass);
if (genericPlainClassPayload.Length != 1 + sizeof(int) ||
    SharpPackSerializer.Deserialize<AotGenericContainer<int>>(
        genericPlainClassPayload)?.Value != 42 ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The plain generic class lost its fixed-size path under NativeAOT.");
}

AotVarIntFormatter.Reset();
var genericExactFormatted = new AotExactGeneric<
    AotUnmanagedFormatted>
{
    Value = unmanagedFormatted,
    Text = "formatted exact aot",
};
var genericExactFormattedPayload = SharpPackSerializer.Serialize(
    genericExactFormatted);
var genericExactFormattedRoundTrip = SharpPackSerializer.Deserialize<
    AotExactGeneric<AotUnmanagedFormatted>>(
        genericExactFormattedPayload);
if (genericExactFormattedRoundTrip?.Value.Value !=
        unmanagedFormatted.Value ||
    genericExactFormattedRoundTrip.Value.Tail !=
        unmanagedFormatted.Tail ||
    genericExactFormattedRoundTrip.Text != "formatted exact aot" ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The generic exact path did not preserve unmanaged raw-copy under NativeAOT.");
}

AotVarIntFormatter.Reset();
var closedFormattedArray = new[] { closedFormatted, closedFormatted };
var closedFormattedArrayPayload =
    SharpPackSerializer.Serialize(closedFormattedArray);
if (AotVarIntFormatter.SerializeCalls != 0 ||
    closedFormattedArrayPayload.Length !=
        sizeof(int) +
        (System.Runtime.CompilerServices.Unsafe.SizeOf<
            AotUnmanagedWrapper<AotUnmanagedFormatted>>() *
         closedFormattedArray.Length))
{
    throw new InvalidOperationException(
        "The annotated unmanaged array lost its raw path under NativeAOT.");
}
AotVarIntFormatter.Reset();
var closedFormattedArrayRoundTrip = SharpPackSerializer.Deserialize<
    AotUnmanagedWrapper<AotUnmanagedFormatted>[]>(
        closedFormattedArrayPayload);
if (closedFormattedArrayRoundTrip is not { Length: 2 } ||
    AotVarIntFormatter.DeserializeCalls != 0 ||
    closedFormattedArrayRoundTrip.Any(item =>
        item.Value.Value != unmanagedFormatted.Value ||
        item.Value.Tail != unmanagedFormatted.Tail))
{
    throw new InvalidOperationException(
        "The annotated unmanaged array policy failed under NativeAOT.");
}

AotVarIntFormatter.Reset();
var closedPlainArray = new[] { closedPlain, closedPlain };
var closedPlainArrayPayload = SharpPackSerializer.Serialize(
    closedPlainArray);
if (AotVarIntFormatter.SerializeCalls != 0 ||
    closedPlainArrayPayload.Length !=
        sizeof(int) +
        (System.Runtime.CompilerServices.Unsafe.SizeOf<
            AotUnmanagedWrapper<int>>() * closedPlainArray.Length) ||
    SharpPackSerializer.Deserialize<AotUnmanagedWrapper<int>[]>(
        closedPlainArrayPayload) is not
        [{ Value: 42 }, { Value: 42 }] ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The plain unmanaged array lost its raw path under NativeAOT.");
}

var rootList = new List<int> { 1, 200, 30_000 };
var rootListPayload = SharpPackSerializer.Serialize(rootList);
if (SharpPackSerializer.Deserialize<List<int>>(rootListPayload) is not
    [1, 200, 30_000])
{
    throw new InvalidOperationException(
        "The root unmanaged List failed under NativeAOT.");
}

AotVarIntFormatter.Reset();
var formattedList = new List<
    AotUnmanagedWrapper<AotUnmanagedFormatted>>
{
    closedFormatted,
    closedFormatted,
};
var formattedListPayload = SharpPackSerializer.Serialize(formattedList);
var formattedListRoundTrip = SharpPackSerializer.Deserialize<List<
    AotUnmanagedWrapper<AotUnmanagedFormatted>>>(formattedListPayload);
if (formattedListRoundTrip is not { Count: 2 } ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0 ||
    formattedListRoundTrip.Any(item =>
        item.Value.Value != unmanagedFormatted.Value ||
        item.Value.Tail != unmanagedFormatted.Tail))
{
    throw new InvalidOperationException(
        "The unmanaged List raw-copy path failed under NativeAOT.");
}

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

AotVarIntFormatter.Reset();
var contextFormattedArrayPayload = SharpPackSerializer.Serialize(
    closedFormattedArray,
    context);
var contextFormattedArray = SharpPackSerializer.Deserialize<
    AotUnmanagedWrapper<AotUnmanagedFormatted>[]>(
        contextFormattedArrayPayload,
        context);
if (contextFormattedArray is not { Length: 2 } ||
    !contextFormattedArrayPayload.AsSpan().SequenceEqual(
        closedFormattedArrayPayload) ||
    AotVarIntFormatter.SerializeCalls != 0 ||
    AotVarIntFormatter.DeserializeCalls != 0)
{
    throw new InvalidOperationException(
        "The empty Context changed unmanaged raw-copy under NativeAOT.");
}

var contextPlainArrayPayload = SharpPackSerializer.Serialize(
    closedPlainArray,
    context);
if (SharpPackSerializer.Deserialize<AotUnmanagedWrapper<int>[]>(
        contextPlainArrayPayload,
        context) is not [{ Value: 42 }, { Value: 42 }])
{
    throw new InvalidOperationException(
        "The Context plain unmanaged array failed under NativeAOT.");
}

var listOverrideContext = new SharpPackSerializerContextBuilder()
    .Register(new AotVarIntFormatter())
    .Build();
AotVarIntFormatter.Reset();
var contextListPayload = SharpPackSerializer.Serialize(
    rootList,
    listOverrideContext);
var contextListRoundTrip = SharpPackSerializer.Deserialize<List<int>>(
    contextListPayload,
    listOverrideContext);
if (contextListRoundTrip is not [1, 200, 30_000] ||
    AotVarIntFormatter.SerializeCalls != rootList.Count ||
    AotVarIntFormatter.DeserializeCalls != rootList.Count ||
    contextListPayload.AsSpan().SequenceEqual(rootListPayload))
{
    throw new InvalidOperationException(
        "The Context List override failed under NativeAOT.");
}

var factoryContext = new SharpPackSerializerContextBuilder()
    .RegisterFactory<AotFactoryValue, AotPublicFactory>()
    .Build();
var factoryValue = new AotFactoryValue { Value = 314 };
var factoryPayload = SharpPackSerializer.Serialize(
    factoryValue,
    factoryContext);
if (SharpPackSerializer.Deserialize<AotFactoryValue>(
        factoryPayload,
        factoryContext)?.Value != factoryValue.Value)
{
    throw new InvalidOperationException(
        "The explicitly registered public factory failed under NativeAOT.");
}

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
public partial class AotExactSizeModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public byte[]? Payload { get; set; }
}

[SharpPackable]
public partial struct AotUnmanagedFormatted
{
    [AotVarIntFormatter]
    public int Value { get; set; }
    public long Tail { get; set; }
}

[SharpPackable]
public partial struct AotUnmanagedWrapper<T>
    where T : unmanaged
{
    public T Value { get; set; }
}

[SharpPackable]
public partial class AotGenericContainer<T>
    where T : unmanaged
{
    public T Value { get; set; }
}

[SharpPackable]
public partial class AotExactGeneric<T>
    where T : unmanaged
{
    public T Value { get; set; }
    public string? Text { get; set; }
}

public sealed class AotVarIntFormatter : SharpPackFormatter<int>
{
    public static int SerializeCalls { get; private set; }
    public static int DeserializeCalls { get; private set; }

    public static void Reset()
    {
        SerializeCalls = 0;
        DeserializeCalls = 0;
    }

    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
    {
        SerializeCalls++;
        writer.WriteVarInt(value);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
    {
        DeserializeCalls++;
        value = reader.ReadVarIntInt32();
    }
}

public sealed class AotVarIntFormatterAttribute
    : SharpPackCustomFormatterAttribute<AotVarIntFormatter, int>
{
    public override AotVarIntFormatter GetFormatter() => new();
}

public sealed class AotFactoryValue
{
    public int Value { get; set; }
}

public sealed class AotFactoryFormatter
    : SharpPackFormatter<AotFactoryValue>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref AotFactoryValue? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(value.Value);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref AotFactoryValue? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }
        if (count != 1)
        {
            SharpPackSerializationException.ThrowInvalidPropertyCount(
                typeof(AotFactoryValue),
                1,
                count);
        }

        reader.ReadUnmanaged(out int item);
        value ??= new AotFactoryValue();
        value.Value = item;
    }
}

public sealed class AotPublicFactory
    : ISharpPackFormatterFactory<AotFactoryValue>
{
    public static SharpPackFormatter<AotFactoryValue> CreateFormatter()
        => new AotFactoryFormatter();
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
