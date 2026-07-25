using System;
using System.Buffers;
using System.Linq;
using SharpPack.Internal;

namespace SharpPack.Tests;

public class ExactSizeSerializationTest
{
    [Fact]
    public void DefaultByteArrayPath_SelectsExactContract()
    {
        SharpPackSerializer.Serialize(new ManualExactProbe())
            .Should().Equal(0xCA, 0xFE, 0x42);
    }

    [Fact]
    public void GeneralBufferWriterPath_DoesNotSelectExactContract()
    {
        var writer = new ArrayBufferWriter<byte>();
        var value = new ManualExactProbe();

        var serialize = () =>
            SharpPackSerializer.Serialize(ref writer, value);

        serialize.Should().Throw<SharpPackSerializationException>();
        writer.WrittenCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ascii")]
    [InlineData("中文")]
    [InlineData("emoji 😀 mixed")]
    public void ExactPayload_MatchesGeneralBufferWriterPath(string? text)
    {
        var value = new ExactSizeModel
        {
            Id = 42,
            Optional = 17,
            Text = text,
            Bytes = text is null ? null : [1, 2, 3, 4],
        };
        var baselineWriter = new ArrayBufferWriter<byte>();

        _ = SharpPackSerializer.Serialize(ref baselineWriter, value);
        var exactPayload = SharpPackSerializer.Serialize(value);

        IsExact<ExactSizeModel>().Should().BeTrue();
        exactPayload.Should().Equal(baselineWriter.WrittenSpan.ToArray());
        SharpPackSerializer.Deserialize<ExactSizeModel>(exactPayload)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void ExactPayload_HandlesNullAndEmptyArrays()
    {
        AssertMatchesGeneralPath(new ExactSizeModel
        {
            Text = null,
            Bytes = null,
        });
        AssertMatchesGeneralPath(new ExactSizeModel
        {
            Text = string.Empty,
            Bytes = [],
        });

        SharpPackSerializer.Serialize<ExactSizeModel>(null)
            .Should().Equal(SharpPackCode.NullObject);
    }

    [Fact]
    public void ExactPayload_RejectsInvalidUtf16LikeGeneralPath()
    {
        var value = new ExactSizeModel { Text = "invalid \uD800" };
        var baselineWriter = new ArrayBufferWriter<byte>();

        var exact = () => SharpPackSerializer.Serialize(value);
        var general = () =>
            SharpPackSerializer.Serialize(ref baselineWriter, value);

        exact.Should().Throw<SharpPackSerializationException>();
        general.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void CustomGetters_BypassExactAndPreserveSingleReadSemantics()
    {
        var value = new ExactGetterModel(42, "once", [1, 2, 3]);

        var payload = SharpPackSerializer.Serialize(value);

        value.GetterCalls.Should().Be(3);
        SharpPackSerializer.Deserialize<ExactGetterModel>(payload)!.Id
            .Should().Be(42);
    }

    [Fact]
    public void ClosedUnmanagedGeneric_UsesExactPayloadPath()
    {
        var value = new ExactGenericModel<long>
        {
            Value = 42,
            Text = "generic",
        };
        var writer = new ArrayBufferWriter<byte>();

        _ = SharpPackSerializer.Serialize(ref writer, value);
        SharpPackSerializer.Serialize(value)
            .Should().Equal(writer.WrittenSpan.ToArray());
        IsExact<ExactGenericModel<long>>().Should().BeTrue();
    }

    [Fact]
    public void PositionalRecord_UsesExactPayloadPath()
    {
        var value = new ExactRecordModel(42, "record");
        var writer = new ArrayBufferWriter<byte>();

        _ = SharpPackSerializer.Serialize(ref writer, value);
        SharpPackSerializer.Serialize(value)
            .Should().Equal(writer.WrittenSpan.ToArray());
        IsExact<ExactRecordModel>().Should().BeTrue();
    }

    [Fact]
    public void StaticBaseAndUnionTypes_PreserveTheirFormatterContracts()
    {
        var derived = new ExactDerived
        {
            BaseValue = 10,
            BaseName = "base",
            DerivedValue = 20,
        };
        var baseWriter = new ArrayBufferWriter<byte>();
        var derivedWriter = new ArrayBufferWriter<byte>();
        ExactBase asBase = derived;
        _ = SharpPackSerializer.Serialize(ref baseWriter, asBase);
        _ = SharpPackSerializer.Serialize(ref derivedWriter, derived);

        IsExact<ExactBase>().Should().BeTrue();
        SharpPackSerializer.Serialize<ExactBase>(derived)
            .Should().Equal(baseWriter.WrittenSpan.ToArray());
        SharpPackSerializer.Serialize(derived)
            .Should().Equal(derivedWriter.WrittenSpan.ToArray())
            .And.NotEqual(baseWriter.WrittenSpan.ToArray());

        IExactUnion union = new ExactUnionValue { Value = 123 };
        var unionWriter = new ArrayBufferWriter<byte>();
        _ = SharpPackSerializer.Serialize(ref unionWriter, union);
        SharpPackSerializer.Serialize(union)
            .Should().Equal(unionWriter.WrittenSpan.ToArray());
    }

    [Fact]
    public void ContextOverride_BypassesExactDefaultPath()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var value = new ExactSizeModel { Id = 42, Text = "context" };

        var defaultPayload = SharpPackSerializer.Serialize(value);
        var contextPayload = SharpPackSerializer.Serialize(value, context);

        contextPayload.Should().NotEqual(defaultPayload);
        SharpPackSerializer.Deserialize<ExactSizeModel>(
            contextPayload,
            context)!.Id.Should().Be(42);
    }

    [Fact]
    public void Utf16Context_PreservesConfiguredStringEncoding()
    {
        var context = new SharpPackSerializerContext(
            SharpPackSerializerConfiguration.Utf16);
        var value = new ExactSizeModel { Id = 42, Text = "中文" };
        var writer = new ArrayBufferWriter<byte>();

        _ = SharpPackSerializer.Serialize(ref writer, value, context);
        var payload = SharpPackSerializer.Serialize(value, context);

        payload.Should().Equal(writer.WrittenSpan.ToArray());
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));
    }

    [Fact]
    public void ObjectStaticType_DoesNotUseRuntimeExactContract()
    {
        object value = new ExactSizeModel { Id = 42, Text = "object" };
        var writer = new ArrayBufferWriter<byte>();

        Action general = () =>
            SharpPackSerializer.Serialize(ref writer, value);
        Action array = () => SharpPackSerializer.Serialize(value);

        general.Should().Throw<SharpPackSerializationException>();
        array.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void IneligibleContracts_DoNotImplementExactSizeInterface()
    {
        IsExact<ExactSizeModel>().Should().BeTrue();
        IsExact<ExactGetterModel>().Should().BeFalse();
        IsExact<ExactVersionTolerant>().Should().BeFalse();
        IsExact<ExactCircularReference>().Should().BeFalse();
        IsExact<ExactCallbackModel>().Should().BeFalse();
        IsExact<ExactCustomFormatterModel>().Should().BeFalse();
        IsExact<IExactUnion>().Should().BeFalse();
    }

    [Fact]
    public void ExactWriter_RejectsNegativeAndOutOfRangeOperations()
    {
        var buffer = new byte[4];
        var writer = new SharpPackExactArrayBufferWriter(buffer);

        ((Action)(() => writer.Advance(-1)))
            .Should().Throw<SharpPackSerializationException>();
        ((Action)(() => writer.Advance(5)))
            .Should().Throw<SharpPackSerializationException>();
        ((Action)(() => writer.GetSpan(-1)))
            .Should().Throw<SharpPackSerializationException>();
        ((Action)(() => writer.GetMemory(5)))
            .Should().Throw<SharpPackSerializationException>();
        ((Func<byte[]>)(() => writer.GetFilledBuffer()))
            .Should().Throw<SharpPackSerializationException>();
    }

    static void AssertMatchesGeneralPath(ExactSizeModel value)
    {
        var baselineWriter = new ArrayBufferWriter<byte>();
        _ = SharpPackSerializer.Serialize(ref baselineWriter, value);
        SharpPackSerializer.Serialize(value)
            .Should().Equal(baselineWriter.WrittenSpan.ToArray());
    }

    static bool IsExact<T>()
        => typeof(T).GetInterfaces().Any(static type =>
            type.IsGenericType &&
            type.GetGenericTypeDefinition() ==
                typeof(ISharpPackExactSizeSerializable<>));
}

[SharpPackable]
public partial class ExactSizeModel
{
    public int Id { get; set; }
    public int? Optional { get; set; }
    public string? Text { get; set; }
    public byte[]? Bytes { get; set; }
}

[SharpPackable]
public partial class ExactGetterModel
{
    readonly int id;
    readonly string? text;
    readonly byte[]? bytes;

    [SharpPackIgnore]
    public int GetterCalls { get; private set; }

    public int Id
    {
        get
        {
            GetterCalls++;
            return id;
        }
    }

    public string? Text
    {
        get
        {
            GetterCalls++;
            return text;
        }
    }

    public byte[]? Bytes
    {
        get
        {
            GetterCalls++;
            return bytes;
        }
    }

    [SharpPackConstructor]
    public ExactGetterModel(int id, string? text, byte[]? bytes)
    {
        this.id = id;
        this.text = text;
        this.bytes = bytes;
    }
}

[SharpPackable]
public partial class ExactGenericModel<T>
    where T : unmanaged
{
    public T Value { get; set; }
    public string? Text { get; set; }
}

[SharpPackable]
public partial record ExactRecordModel(int Id, string? Text);

[SharpPackable]
public partial class ExactBase
{
    public int BaseValue { get; set; }
    public string? BaseName { get; set; }
}

[SharpPackable]
public partial class ExactDerived : ExactBase
{
    public int DerivedValue { get; set; }
}

[SharpPackable]
[SharpPackUnion(1, typeof(ExactUnionValue))]
public partial interface IExactUnion;

[SharpPackable]
public partial class ExactUnionValue : IExactUnion
{
    public int Value { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class ExactVersionTolerant
{
    [SharpPackOrder(0)]
    public int Value { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class ExactCircularReference
{
    [SharpPackOrder(0)]
    public string? Name { get; set; }

    [SharpPackOrder(1)]
    public ExactCircularReference? Next { get; set; }
}

[SharpPackable]
public partial class ExactCallbackModel
{
    public int Value { get; set; }

    [SharpPackOnSerializing]
    static void OnSerializing()
    {
    }
}

[SharpPackable]
public partial class ExactCustomFormatterModel
{
    [ExactPlusOneFormatter]
    public int Value { get; set; }
}

public sealed class ExactPlusOneFormatter : SharpPackFormatter<int>
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

public sealed class ExactPlusOneFormatterAttribute
    : SharpPackCustomFormatterAttribute<ExactPlusOneFormatter, int>
{
    public override ExactPlusOneFormatter GetFormatter() => new();
}

public sealed class ManualExactProbe
    : ISharpPackExactSizeSerializable<ManualExactProbe>
{
    public byte[] SerializeExact() => [0xCA, 0xFE, 0x42];
}
