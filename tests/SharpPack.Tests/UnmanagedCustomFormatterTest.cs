using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using SharpPack.Tests.Utils;

namespace SharpPack.Tests;

public class UnmanagedCustomFormatterTest
{
    [Fact]
    public void RootValue_UsesDeclaredFormatterAcrossByteArrayAndBufferWriter()
    {
        var value = CreateValue(30_000, 123_456_789);

        CountingVarIntFormatter.Reset();
        var arrayPayload = SharpPackSerializer.Serialize(value);
        CountingVarIntFormatter.SerializeCalls.Should().Be(1);

        CountingVarIntFormatter.Reset();
        var bufferWriter = new ArrayBufferWriter<byte>();
        _ = SharpPackSerializer.Serialize(ref bufferWriter, value);
        CountingVarIntFormatter.SerializeCalls.Should().Be(1);
        bufferWriter.WrittenSpan.ToArray().Should().Equal(arrayPayload);

        CountingVarIntFormatter.Reset();
        SharpPackSerializer.Deserialize<UnmanagedFormattedValue>(arrayPayload)
            .Should().Be(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(1);
    }

    [Fact]
    public void DeclaredFormatter_PropagatesThroughCompositeShapes()
    {
        var first = CreateValue(30_000, 123_456_789);
        var second = CreateValue(-123, 987_654_321);

        AssertFormatterRoundTrip(new[] { first, second }, 2);
        AssertFormatterRoundTrip(new List<UnmanagedFormattedValue>
        {
            first,
            second,
        }, 2);
        AssertFormatterRoundTrip<UnmanagedFormattedValue?>(first, 1);
        AssertFormatterRoundTrip((first, 42), 1);
        AssertFormatterRoundTrip(new KeyValuePair<
            UnmanagedFormattedValue, int>(first, 42), 1);
        AssertFormatterRoundTrip(new[,]
        {
            { first, second },
        }, 2);
    }

    [Fact]
    public void DeclaredFormatter_PropagatesThroughGeneratedUnmanagedGraphs()
    {
        var first = CreateValue(30_000, 123_456_789);
        var second = CreateValue(-123, 987_654_321);
        var nested = new NestedUnmanagedFormattedValue
        {
            Value = first,
            Code = 7,
        };
        var container = new UnmanagedFormattedContainer
        {
            Value = nested,
            Values = [first, second],
        };

        AssertFormatterRoundTrip(nested, 1);
        AssertFormatterRoundTrip(container, 3);
    }

    [Fact]
    public void DeclaredFormatter_PropagatesThroughGeneratedCompositeMembers()
    {
        var first = CreateValue(30_000, 123_456_789);
        var second = CreateValue(-123, 987_654_321);
        var value = new UnmanagedFormattedCompositeContainer
        {
            Optional = first,
            Tuple = (second, 42),
            Pair = new KeyValuePair<UnmanagedFormattedValue, int>(
                first,
                7),
            PairArray =
            [
                new KeyValuePair<UnmanagedFormattedValue, int>(second, 8),
            ],
        };

        AssertFormatterRoundTrip(value, 4);
    }

    [Fact]
    public void PlainGeneratedCompositeMembers_PreserveUnmanagedWire()
    {
        var value = new PlainUnmanagedCompositeContainer
        {
            Optional = 42,
            Tuple = (7, 123_456_789),
            Pair = new KeyValuePair<int, long>(9, 987_654_321),
        };

        CountingVarIntFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);

        payload[0].Should().Be(3);
        payload.Should().HaveCount(
            1 +
            System.Runtime.CompilerServices.Unsafe.SizeOf<int?>() +
            System.Runtime.CompilerServices.Unsafe.SizeOf<(int, long)>() +
            System.Runtime.CompilerServices.Unsafe.SizeOf<
                KeyValuePair<int, long>>());
        SharpPackSerializer.Deserialize<PlainUnmanagedCompositeContainer>(
            payload).Should().BeEquivalentTo(value);
        CountingVarIntFormatter.SerializeCalls.Should().Be(0);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void UnannotatedGenericWrapper_PreservesRawUnmanagedSemantics()
    {
        var value = new RawUnmanagedWrapper<UnmanagedFormattedValue>
        {
            Value = CreateValue(30_000, 123_456_789),
        };

        CountingVarIntFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);

        CountingVarIntFormatter.SerializeCalls.Should().Be(0);
        payload.Should().HaveCount(
            System.Runtime.CompilerServices.Unsafe.SizeOf<
                RawUnmanagedWrapper<UnmanagedFormattedValue>>());
        SharpPackSerializer.Deserialize<
            RawUnmanagedWrapper<UnmanagedFormattedValue>>(payload)
            .Should().Be(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void GenericGeneratedWrapper_SelectsPolicyPerClosedType()
    {
        ((ISharpPackConditionalFormatterAware)
            default(GeneratedUnmanagedWrapper<UnmanagedFormattedValue>))
            .RequiresFormatterAwareSerialization.Should().BeTrue();
        ((ISharpPackConditionalFormatterAware)
            default(GeneratedUnmanagedWrapper<int>))
            .RequiresFormatterAwareSerialization.Should().BeFalse();
        var formatted = new GeneratedUnmanagedWrapper<
            UnmanagedFormattedValue>
        {
            Value = CreateValue(30_000, 123_456_789),
        };
        var plain = new GeneratedUnmanagedWrapper<int>
        {
            Value = 42,
        };

        AssertFormatterRoundTrip(formatted, 1);

        CountingVarIntFormatter.Reset();
        var plainPayload = SharpPackSerializer.Serialize(plain);
        CountingVarIntFormatter.SerializeCalls.Should().Be(0);
        plainPayload.Should().HaveCount(
            System.Runtime.CompilerServices.Unsafe.SizeOf<
                GeneratedUnmanagedWrapper<int>>());
        SharpPackSerializer.Deserialize<GeneratedUnmanagedWrapper<int>>(
            plainPayload).Should().Be(plain);

        var formattedArray = new[] { formatted, formatted };
        AssertFormatterRoundTrip(formattedArray, 2);

        CountingVarIntFormatter.Reset();
        var plainArray = new[] { plain, plain };
        var plainArrayPayload = SharpPackSerializer.Serialize(plainArray);
        CountingVarIntFormatter.SerializeCalls.Should().Be(0);
        plainArrayPayload.Should().HaveCount(
            sizeof(int) +
            (System.Runtime.CompilerServices.Unsafe.SizeOf<
                GeneratedUnmanagedWrapper<int>>() * plainArray.Length));
        SharpPackSerializer.Deserialize<GeneratedUnmanagedWrapper<int>[]>(
            plainArrayPayload).Should().Equal(plainArray);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void GenericGeneratedClass_SelectsMemberPolicyPerClosedType()
    {
        var formatted = new GeneratedGenericContainer<
            UnmanagedFormattedValue>
        {
            Value = CreateValue(30_000, 123_456_789),
        };
        var plain = new GeneratedGenericContainer<int> { Value = 42 };

        ((ISharpPackConditionalFormatterAware)formatted)
            .RequiresFormatterAwareSerialization.Should().BeTrue();
        ((ISharpPackConditionalFormatterAware)plain)
            .RequiresFormatterAwareSerialization.Should().BeFalse();

        AssertFormatterRoundTrip(formatted, 1);

        CountingVarIntFormatter.Reset();
        var plainPayload = SharpPackSerializer.Serialize(plain);
        plainPayload.Should().HaveCount(1 + sizeof(int));
        SharpPackSerializer.Deserialize<GeneratedGenericContainer<int>>(
            plainPayload)!.Value.Should().Be(42);
        CountingVarIntFormatter.SerializeCalls.Should().Be(0);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void GenericExactSizeClass_FallsBackOnlyForFormattedClosedType()
    {
        var value = new ExactGenericModel<UnmanagedFormattedValue>
        {
            Value = CreateValue(30_000, 123_456_789),
            Text = "formatted exact",
        };

        CountingVarIntFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);
        CountingVarIntFormatter.SerializeCalls.Should().Be(1);

        CountingVarIntFormatter.Reset();
        SharpPackSerializer.Deserialize<
            ExactGenericModel<UnmanagedFormattedValue>>(payload)
            .Should().BeEquivalentTo(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(1);

        ((ISharpPackExactSizeSerializable<
            ExactGenericModel<UnmanagedFormattedValue>>)value)
            .SerializeExact().Should().Equal(payload);
    }

    [Fact]
    public void RootArrayPolicy_DoesNotInvokeUserStructConstructor()
    {
        GeneratedWrapperWithConstructor<int>.ConstructorCalls = 0;
        var value = new[]
        {
            default(GeneratedWrapperWithConstructor<int>),
        };

        var payload = SharpPackSerializer.Serialize(value);
        var roundTrip = SharpPackSerializer.Deserialize<
            GeneratedWrapperWithConstructor<int>[]>(payload);

        GeneratedWrapperWithConstructor<int>.ConstructorCalls
            .Should().Be(0);
        roundTrip.Should().Equal(value);
    }

    [Fact]
    public void EmptyAndCustomContexts_PreserveMemberFormatterBinding()
    {
        var value = CreateValue(30_000, 123_456_789);
        var emptyContext = new SharpPackSerializerContext();
        var customContext = new SharpPackSerializerContextBuilder()
            .Register(new CountingVarInt64Formatter())
            .Build();
        var defaultPayload = SharpPackSerializer.Serialize(value);

        CountingVarIntFormatter.Reset();
        var emptyPayload = SharpPackSerializer.Serialize(
            value,
            emptyContext);
        emptyPayload.Should().Equal(defaultPayload);
        CountingVarIntFormatter.SerializeCalls.Should().Be(1);

        CountingVarIntFormatter.Reset();
        CountingVarInt64Formatter.Reset();
        var customPayload = SharpPackSerializer.Serialize(
            value,
            customContext);
        customPayload.Should().NotEqual(defaultPayload);
        CountingVarIntFormatter.SerializeCalls.Should().Be(1);
        CountingVarInt64Formatter.SerializeCalls.Should().Be(1);

        CountingVarIntFormatter.Reset();
        CountingVarInt64Formatter.Reset();
        SharpPackSerializer.Deserialize<UnmanagedFormattedValue>(
            customPayload,
            customContext).Should().Be(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(1);
        CountingVarInt64Formatter.DeserializeCalls.Should().Be(1);
    }

    [Fact]
    public void SegmentedSequence_UsesDeclaredFormatterAndReportsConsumption()
    {
        var value = CreateValue(30_000, 123_456_789);
        var payload = SharpPackSerializer.Serialize(value);
        var sequence = ReadOnlySequenceBuilder.Create(
            payload.Select(static item => new[] { item }).ToArray());
        var emptyContext = new SharpPackSerializerContext();

        CountingVarIntFormatter.Reset();
        UnmanagedFormattedValue defaultValue = default;
        var defaultConsumed = SharpPackSerializer.Deserialize(
            sequence,
            ref defaultValue);
        defaultConsumed.Should().Be(payload.Length);
        defaultValue.Should().Be(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(1);

        CountingVarIntFormatter.Reset();
        UnmanagedFormattedValue contextValue = default;
        var contextConsumed = SharpPackSerializer.Deserialize(
            sequence,
            ref contextValue,
            emptyContext);
        contextConsumed.Should().Be(payload.Length);
        contextValue.Should().Be(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(1);
    }

    static UnmanagedFormattedValue CreateValue(int value, long tail)
        => new()
        {
            Value = value,
            Tail = tail,
        };

    static void AssertFormatterRoundTrip<T>(T value, int formatterCalls)
    {
        CountingVarIntFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);
        CountingVarIntFormatter.SerializeCalls.Should().Be(formatterCalls);

        CountingVarIntFormatter.Reset();
        SharpPackSerializer.Deserialize<T>(payload)
            .Should().BeEquivalentTo(value);
        CountingVarIntFormatter.DeserializeCalls.Should().Be(formatterCalls);
    }
}

[SharpPackable]
public partial struct UnmanagedFormattedValue
{
    [CountingVarIntFormatter]
    public int Value { get; set; }

    public long Tail { get; set; }
}

[SharpPackable]
public partial struct NestedUnmanagedFormattedValue
{
    public UnmanagedFormattedValue Value { get; set; }
    public short Code { get; set; }
}

[SharpPackable]
public partial class UnmanagedFormattedContainer
{
    public NestedUnmanagedFormattedValue Value { get; set; }
    public UnmanagedFormattedValue[]? Values { get; set; }
}

[SharpPackable]
public partial class UnmanagedFormattedCompositeContainer
{
    public UnmanagedFormattedValue? Optional { get; set; }

    public (UnmanagedFormattedValue Value, int Code) Tuple { get; set; }

    public KeyValuePair<UnmanagedFormattedValue, int> Pair { get; set; }

    public KeyValuePair<UnmanagedFormattedValue, int>[]? PairArray
    {
        get;
        set;
    }
}

[SharpPackable]
public partial class PlainUnmanagedCompositeContainer
{
    public int? Optional { get; set; }

    public (int Value, long Tail) Tuple { get; set; }

    public KeyValuePair<int, long> Pair { get; set; }
}

public struct RawUnmanagedWrapper<T>
    where T : unmanaged
{
    public T Value;
}

[SharpPackable]
public partial struct GeneratedUnmanagedWrapper<T>
    where T : unmanaged
{
    public T Value { get; set; }
}

[SharpPackable]
public partial class GeneratedGenericContainer<T>
    where T : unmanaged
{
    public T Value { get; set; }
}

[SharpPackable]
public partial struct GeneratedWrapperWithConstructor<T>
    where T : unmanaged
{
    public static int ConstructorCalls;

    public T Value { get; set; }

    public GeneratedWrapperWithConstructor()
    {
        ConstructorCalls++;
        Value = default;
    }
}

public sealed class CountingVarIntFormatter : SharpPackFormatter<int>
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

public sealed class CountingVarIntFormatterAttribute
    : SharpPackCustomFormatterAttribute<CountingVarIntFormatter, int>
{
    public override CountingVarIntFormatter GetFormatter() => new();
}

public sealed class CountingVarInt64Formatter : SharpPackFormatter<long>
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
        scoped ref long value)
    {
        SerializeCalls++;
        writer.WriteVarInt(value);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref long value)
    {
        DeserializeCalls++;
        value = reader.ReadVarIntInt64();
    }
}
