using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpPack.Tests.Utils;

namespace SharpPack.Tests;

public class UnmanagedRawCopyCompatibilityTest
{
    [Fact]
    public void AnnotatedMembers_AreIgnoredByRawCopyPaths()
    {
        var value = CreateValue(30_000, 123_456_789);
        var emptyContext = new SharpPackSerializerContext();

        MemberCountingFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);
        payload.Should().HaveCount(Unsafe.SizeOf<RawAnnotatedValue>());

        var bufferWriter = new ArrayBufferWriter<byte>();
        SharpPackSerializer.Serialize(ref bufferWriter, value)
            .Should().Be(payload.Length);
        bufferWriter.WrittenSpan.ToArray().Should().Equal(payload);

        var contextPayload = SharpPackSerializer.Serialize(
            value,
            emptyContext);
        contextPayload.Should().Equal(payload);

        SharpPackSerializer.Deserialize<RawAnnotatedValue>(payload)
            .Should().Be(value);
        SharpPackSerializer.Deserialize<RawAnnotatedValue>(
            payload,
            emptyContext).Should().Be(value);
        MemberCountingFormatter.SerializeCalls.Should().Be(0);
        MemberCountingFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void AnnotatedMembers_AreIgnoredInsideSupportedGraphs()
    {
        var first = CreateValue(30_000, 123_456_789);
        var second = CreateValue(-123, 987_654_321);
        var values = new[] { first, second };
        var list = values.ToList();
        var container = new RawAnnotatedContainer
        {
            Value = first,
            Values = values,
            List = list,
        };

        MemberCountingFormatter.Reset();
        AssertRoundTrip(values);
        AssertRoundTrip(list);
        AssertRoundTrip(container);

        MemberCountingFormatter.SerializeCalls.Should().Be(0);
        MemberCountingFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void LeafContextOverride_DoesNotPenetrateRawCopiedStruct()
    {
        var value = CreateValue(30_000, 123_456_789);
        var context = new SharpPackSerializerContextBuilder()
            .Register(new MemberCountingFormatter())
            .Build();
        var baseline = SharpPackSerializer.Serialize(value);

        MemberCountingFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value, context);

        payload.Should().Equal(baseline);
        SharpPackSerializer.Deserialize<RawAnnotatedValue>(payload, context)
            .Should().Be(value);
        MemberCountingFormatter.SerializeCalls.Should().Be(0);
        MemberCountingFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void WholeStructContextOverride_RemainsSupported()
    {
        var first = CreateValue(30_000, 123_456_789);
        var second = CreateValue(-123, 987_654_321);
        var context = new SharpPackSerializerContextBuilder()
            .Register(new WholeRawAnnotatedValueFormatter())
            .Build();

        WholeRawAnnotatedValueFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(first, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(first));
        SharpPackSerializer.Deserialize<RawAnnotatedValue>(payload, context)
            .Should().Be(first);

        var values = new[] { first, second };
        var arrayPayload = SharpPackSerializer.Serialize(values, context);
        arrayPayload.Should().NotEqual(SharpPackSerializer.Serialize(values));
        SharpPackSerializer.Deserialize<RawAnnotatedValue[]>(
            arrayPayload,
            context).Should().Equal(values);

        var container = new RawAnnotatedContainer
        {
            Value = first,
            Values = values,
            List = values.ToList(),
        };
        var containerPayload = SharpPackSerializer.Serialize(
            container,
            context);
        containerPayload.Should().NotEqual(
            SharpPackSerializer.Serialize(container));
        SharpPackSerializer.Deserialize<RawAnnotatedContainer>(
            containerPayload,
            context).Should().BeEquivalentTo(container);

        WholeRawAnnotatedValueFormatter.SerializeCalls.Should().Be(8);
        WholeRawAnnotatedValueFormatter.DeserializeCalls.Should().Be(8);
    }

    [Fact]
    public void GenericUnmanagedWrapper_RemainsRawCopied()
    {
        var value = new RawGeneratedWrapper<RawAnnotatedValue>
        {
            Value = CreateValue(30_000, 123_456_789),
        };

        MemberCountingFormatter.Reset();
        var payload = SharpPackSerializer.Serialize(value);

        payload.Should().HaveCount(
            Unsafe.SizeOf<RawGeneratedWrapper<RawAnnotatedValue>>());
        SharpPackSerializer.Deserialize<
            RawGeneratedWrapper<RawAnnotatedValue>>(payload)
            .Should().Be(value);
        MemberCountingFormatter.SerializeCalls.Should().Be(0);
        MemberCountingFormatter.DeserializeCalls.Should().Be(0);
    }

    [Fact]
    public void RawCopyClassification_DoesNotInvokeStructConstructor()
    {
        RawWrapperWithConstructor<int>.ConstructorCalls = 0;
        var value = new[] { default(RawWrapperWithConstructor<int>) };

        var payload = SharpPackSerializer.Serialize(value);
        SharpPackSerializer.Deserialize<RawWrapperWithConstructor<int>[]>(
            payload).Should().Equal(value);

        RawWrapperWithConstructor<int>.ConstructorCalls.Should().Be(0);
    }

    [Fact]
    public void SegmentedSequence_UsesRawCopyAndReportsConsumption()
    {
        var value = CreateValue(30_000, 123_456_789);
        var payload = SharpPackSerializer.Serialize(value);
        var sequence = ReadOnlySequenceBuilder.Create(
            payload.Select(static item => new[] { item }).ToArray());

        MemberCountingFormatter.Reset();
        RawAnnotatedValue decoded = default;
        SharpPackSerializer.Deserialize(sequence, ref decoded)
            .Should().Be(payload.Length);

        decoded.Should().Be(value);
        MemberCountingFormatter.DeserializeCalls.Should().Be(0);
    }

    static RawAnnotatedValue CreateValue(int value, long tail)
        => new() { Value = value, Tail = tail };

    static void AssertRoundTrip<T>(T value)
    {
        var payload = SharpPackSerializer.Serialize(value);
        SharpPackSerializer.Deserialize<T>(payload)
            .Should().BeEquivalentTo(value);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SharpPackable]
public partial struct RawAnnotatedValue
{
    [MemberCountingFormatter]
    public int Value { get; set; }

    public long Tail { get; set; }
}

[SharpPackable]
public partial class RawAnnotatedContainer
{
    public RawAnnotatedValue Value { get; set; }
    public RawAnnotatedValue[]? Values { get; set; }
    public List<RawAnnotatedValue>? List { get; set; }
}

[SharpPackable]
public partial struct RawGeneratedWrapper<T>
    where T : unmanaged
{
    public T Value { get; set; }
}

[SharpPackable]
public partial struct RawWrapperWithConstructor<T>
    where T : unmanaged
{
    public static int ConstructorCalls;
    public T Value { get; set; }

    public RawWrapperWithConstructor()
    {
        ConstructorCalls++;
        Value = default;
    }
}

public sealed class MemberCountingFormatter : SharpPackFormatter<int>
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

public sealed class MemberCountingFormatterAttribute
    : SharpPackCustomFormatterAttribute<MemberCountingFormatter, int>
{
    public override MemberCountingFormatter GetFormatter() => new();
}

public sealed class WholeRawAnnotatedValueFormatter
    : SharpPackFormatter<RawAnnotatedValue>
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
        scoped ref RawAnnotatedValue value)
    {
        SerializeCalls++;
        writer.WriteVarInt(value.Value);
        writer.WriteVarInt(value.Tail);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref RawAnnotatedValue value)
    {
        DeserializeCalls++;
        value.Value = reader.ReadVarIntInt32();
        value.Tail = reader.ReadVarIntInt64();
    }
}
