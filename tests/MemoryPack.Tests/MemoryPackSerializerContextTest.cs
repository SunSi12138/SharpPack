using MemoryPack.Tests.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace MemoryPack.Tests;

public class MemoryPackSerializerContextTest
{
    [Fact]
    public void ExplicitContext_PreservesDefaultWireFormat()
    {
        var value = CreateGraph();
        var context = new MemoryPackSerializerContext();

        var contextBytes = MemoryPackSerializer.Serialize(value, context);
        var defaultBytes = MemoryPackSerializer.Serialize(value);

        contextBytes.Should().Equal(defaultBytes);
        MemoryPackSerializer.Deserialize<ContextGraph>(contextBytes, context)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void Configuration_IsOwnedByContext()
    {
        const string value = "明示的 context";
        var utf8 = new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf8);
        var utf16 = new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf16);

        var utf8Bytes = MemoryPackSerializer.Serialize(value, utf8);
        var utf16Bytes = MemoryPackSerializer.Serialize(value, utf16);

        utf8Bytes.Should().NotEqual(utf16Bytes);
        MemoryPackSerializer.Deserialize<string>(utf8Bytes, utf8).Should().Be(value);
        MemoryPackSerializer.Deserialize<string>(utf16Bytes, utf16).Should().Be(value);
    }

    [Fact]
    public void Registrations_AreFrozenAndIsolatedPerContext()
    {
        var first = new MemoryPackSerializerContextBuilder()
            .Register(new OffsetFormatter(10))
            .Build();
        var second = new MemoryPackSerializerContextBuilder()
            .Register(new OffsetFormatter(20))
            .Build();

        var value = new ContextCustomValue { Value = 1 };
        var firstBytes = MemoryPackSerializer.Serialize(value, first);
        var secondBytes = MemoryPackSerializer.Serialize(value, second);

        firstBytes.Should().NotEqual(secondBytes);
        MemoryPackSerializer.Deserialize<ContextCustomValue>(firstBytes, first)!.Value
            .Should().Be(1);
        MemoryPackSerializer.Deserialize<ContextCustomValue>(secondBytes, second)!.Value
            .Should().Be(1);
    }

    [Fact]
    public void Registration_PropagatesThroughNestedFormatterGraph()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Register(new StandardTypeOneOffsetFormatter(1000))
            .Build();
        var value = CreateGraph();

        var primitiveBytes = MemoryPackSerializer.Serialize(10, context);
        var graphBytes = MemoryPackSerializer.Serialize(value, context);

        primitiveBytes.Should().NotEqual(MemoryPackSerializer.Serialize(10));
        graphBytes.Should().NotEqual(MemoryPackSerializer.Serialize(value));
        MemoryPackSerializer.Deserialize<int>(primitiveBytes, context).Should().Be(10);
        MemoryPackSerializer.Deserialize<ContextGraph>(graphBytes, context)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughBulkCollectionFastPaths()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var values = new[] { 1, 2, 3 };

        var arrayPayload = MemoryPackSerializer.Serialize(values, context);
        arrayPayload.Should().NotEqual(MemoryPackSerializer.Serialize(values));
        MemoryPackSerializer.Deserialize<int[]>(arrayPayload, context)
            .Should().Equal(values);

        var list = values.ToList();
        var listPayload = MemoryPackSerializer.Serialize(list, context);
        listPayload.Should().NotEqual(MemoryPackSerializer.Serialize(list));
        MemoryPackSerializer.Deserialize<List<int>>(listPayload, context)
            .Should().Equal(values);

        var segment = new ArraySegment<int>(values, 1, 2);
        var segmentPayload = MemoryPackSerializer.Serialize(segment, context);
        segmentPayload.Should().NotEqual(MemoryPackSerializer.Serialize(segment));
        MemoryPackSerializer.Deserialize<ArraySegment<int>>(segmentPayload, context)
            .Should().Equal(segment);

        var memory = values.AsMemory();
        var memoryPayload = MemoryPackSerializer.Serialize(memory, context);
        memoryPayload.Should().NotEqual(MemoryPackSerializer.Serialize(memory));
        MemoryPackSerializer.Deserialize<Memory<int>>(memoryPayload, context)
            .ToArray().Should().Equal(values);

        var readOnlyMemory = new ReadOnlyMemory<int>(values);
        var readOnlyMemoryPayload = MemoryPackSerializer.Serialize(readOnlyMemory, context);
        readOnlyMemoryPayload.Should().NotEqual(
            MemoryPackSerializer.Serialize(readOnlyMemory));
        MemoryPackSerializer.Deserialize<ReadOnlyMemory<int>>(
            readOnlyMemoryPayload,
            context).ToArray().Should().Equal(values);

        var sequence = CreateSegmentedIntSequence(values);
        var sequencePayload = MemoryPackSerializer.Serialize(sequence, context);
        sequencePayload.Should().NotEqual(MemoryPackSerializer.Serialize(sequence));
        MemoryPackSerializer.Deserialize<ReadOnlySequence<int>>(
            sequencePayload,
            context).ToArray().Should().Equal(values);

        var generated = new ContextIntArrayContainer { Values = values };
        var generatedPayload = MemoryPackSerializer.Serialize(generated, context);
        generatedPayload.Should().NotEqual(MemoryPackSerializer.Serialize(generated));
        MemoryPackSerializer.Deserialize<ContextIntArrayContainer>(
            generatedPayload,
            context)!.Values.Should().Equal(values);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughVersionTolerantArray()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var value = new ContextVersionTolerantIntArray
        {
            Values = [1, 200, 30_000],
            Tail = "tail",
        };

        var payload = MemoryPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(MemoryPackSerializer.Serialize(value));

        var decoded =
            MemoryPackSerializer.Deserialize<ContextVersionTolerantIntArray>(
                payload,
                context);
        decoded.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughMultiDimensionalArrays()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var two = new int[,] { { 1, 2 }, { 3, 4 } };
        var three = new int[,,] { { { 5, 6 }, { 7, 8 } } };
        var four = new int[,,,] { { { { 9, 10 }, { 11, 12 } } } };

        var twoPayload = MemoryPackSerializer.Serialize(two, context);
        var threePayload = MemoryPackSerializer.Serialize(three, context);
        var fourPayload = MemoryPackSerializer.Serialize(four, context);

        twoPayload.Should().NotEqual(MemoryPackSerializer.Serialize(two));
        threePayload.Should().NotEqual(MemoryPackSerializer.Serialize(three));
        fourPayload.Should().NotEqual(MemoryPackSerializer.Serialize(four));

        AssertArrayEqual(
            two,
            MemoryPackSerializer.Deserialize<int[,]>(twoPayload, context)!);
        AssertArrayEqual(
            three,
            MemoryPackSerializer.Deserialize<int[,,]>(threePayload, context)!);
        AssertArrayEqual(
            four,
            MemoryPackSerializer.Deserialize<int[,,,]>(fourPayload, context)!);

        var overwrite = new int[2, 2];
        var original = overwrite;
        MemoryPackSerializer.Deserialize(twoPayload, ref overwrite, context);
        overwrite.Should().BeSameAs(original);
        AssertArrayEqual(two, overwrite!);
    }

    [Fact]
    public void VariableLengthPrimitiveRegistration_PropagatesThroughMultiDimensionalArrays()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var two = new int[,] { { 1, 200 }, { 30_000, 4_000_000 } };
        var three = new int[,,] { { { 1, 200 }, { 30_000, 4_000_000 } } };
        var four = new int[,,,] { { { { 1, 200 }, { 30_000, 4_000_000 } } } };

        var twoPayload = MemoryPackSerializer.Serialize(two, context);
        var threePayload = MemoryPackSerializer.Serialize(three, context);
        var fourPayload = MemoryPackSerializer.Serialize(four, context);

        twoPayload.Should().NotEqual(MemoryPackSerializer.Serialize(two));
        threePayload.Should().NotEqual(MemoryPackSerializer.Serialize(three));
        fourPayload.Should().NotEqual(MemoryPackSerializer.Serialize(four));
        AssertArrayEqual(
            two,
            MemoryPackSerializer.Deserialize<int[,]>(twoPayload, context)!);
        AssertArrayEqual(
            three,
            MemoryPackSerializer.Deserialize<int[,,]>(threePayload, context)!);
        AssertArrayEqual(
            four,
            MemoryPackSerializer.Deserialize<int[,,,]>(fourPayload, context)!);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughGeneratedScalarFastPaths()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var objectValue = new ContextObjectScalars
        {
            First = 1,
            Second = 30_000,
        };
        var versionTolerantValue = new ContextVersionTolerantScalars
        {
            First = 200,
            Second = 4_000_000,
        };
        var circularValue = new ContextCircularScalars
        {
            First = 30_000,
            Second = 200,
        };

        AssertContextPayloadRoundTrip(objectValue, context);
        AssertContextPayloadRoundTrip(versionTolerantValue, context);
        AssertContextPayloadRoundTrip(circularValue, context);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughUnmanagedCompositeFormatters()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();

        int? nullable = 30_000;
        var nullablePayload = MemoryPackSerializer.Serialize(nullable, context);
        nullablePayload.Should().NotEqual(MemoryPackSerializer.Serialize(nullable));
        MemoryPackSerializer.Deserialize<int?>(nullablePayload, context)
            .Should().Be(nullable);

        var pair = new KeyValuePair<int, int>(200, 30_000);
        var pairPayload = MemoryPackSerializer.Serialize(pair, context);
        pairPayload.Should().NotEqual(MemoryPackSerializer.Serialize(pair));
        MemoryPackSerializer.Deserialize<KeyValuePair<int, int>>(
            pairPayload,
            context).Should().Be(pair);

        var tuple = (First: 200, Second: 30_000);
        var tuplePayload = MemoryPackSerializer.Serialize(tuple, context);
        tuplePayload.Should().NotEqual(MemoryPackSerializer.Serialize(tuple));
        MemoryPackSerializer.Deserialize<(int First, int Second)>(
            tuplePayload,
            context).Should().Be(tuple);

        var dictionary = new Dictionary<int, int>
        {
            [200] = 30_000,
            [4_000_000] = 1,
        };
        var dictionaryPayload = MemoryPackSerializer.Serialize(dictionary, context);
        dictionaryPayload.Should().NotEqual(
            MemoryPackSerializer.Serialize(dictionary));
        MemoryPackSerializer.Deserialize<Dictionary<int, int>>(
            dictionaryPayload,
            context).Should().Equal(dictionary);

        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(30_000, 200);
        queue.Enqueue(4_000_000, 1);
        var queuePayload = MemoryPackSerializer.Serialize(queue, context);
        queuePayload.Should().NotEqual(MemoryPackSerializer.Serialize(queue));
        var decodedQueue = MemoryPackSerializer.Deserialize<PriorityQueue<int, int>>(
            queuePayload,
            context)!;
        decodedQueue.Dequeue().Should().Be(4_000_000);
        decodedQueue.Dequeue().Should().Be(30_000);
    }

    [Fact]
    public void UnrelatedRegistration_PreservesBulkCollectionFastPathWireFormat()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new OffsetFormatter(100))
            .Build();
        var values = new[] { 1, 2, 3 };
        var two = new int[,] { { 1, 2 }, { 3, 4 } };
        var three = new int[,,] { { { 1, 2 }, { 3, 4 } } };
        var four = new int[,,,] { { { { 1, 2 }, { 3, 4 } } } };
        int? nullable = 30_000;
        var pair = new KeyValuePair<int, int>(200, 30_000);
        var tuple = (First: 200, Second: 30_000);
        var dictionary = new Dictionary<int, int> { [200] = 30_000 };

        MemoryPackSerializer.Serialize(values, context)
            .Should().Equal(MemoryPackSerializer.Serialize(values));
        MemoryPackSerializer.Serialize(values.ToList(), context)
            .Should().Equal(MemoryPackSerializer.Serialize(values.ToList()));
        MemoryPackSerializer.Serialize(two, context)
            .Should().Equal(MemoryPackSerializer.Serialize(two));
        MemoryPackSerializer.Serialize(three, context)
            .Should().Equal(MemoryPackSerializer.Serialize(three));
        MemoryPackSerializer.Serialize(four, context)
            .Should().Equal(MemoryPackSerializer.Serialize(four));
        MemoryPackSerializer.Serialize(nullable, context)
            .Should().Equal(MemoryPackSerializer.Serialize(nullable));
        MemoryPackSerializer.Serialize(pair, context)
            .Should().Equal(MemoryPackSerializer.Serialize(pair));
        MemoryPackSerializer.Serialize(tuple, context)
            .Should().Equal(MemoryPackSerializer.Serialize(tuple));
        MemoryPackSerializer.Serialize(dictionary, context)
            .Should().Equal(MemoryPackSerializer.Serialize(dictionary));
    }

    [Fact]
    public void Builder_CanOnlyBuildOnce()
    {
        var builder = new MemoryPackSerializerContextBuilder();
        _ = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(
            () => builder.Register(new OffsetFormatter(1)));
    }

    [Fact]
    public void Builder_RegistersClosedGenericCollections()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .RegisterCollection<ContextList<int>, int>()
            .RegisterSet<ContextSet<int>, int>()
            .RegisterDictionary<ContextDictionary<string, int>, string, int>()
            .Build();

        var list = new ContextList<int> { 1, 2, 3 };
        var set = new ContextSet<int> { 4, 5, 6 };
        var dictionary = new ContextDictionary<string, int>
        {
            ["a"] = 7,
            ["b"] = 8,
        };

        MemoryPackSerializer.Deserialize<ContextList<int>>(
            MemoryPackSerializer.Serialize(list, context),
            context).Should().Equal(list);
        MemoryPackSerializer.Deserialize<ContextSet<int>>(
            MemoryPackSerializer.Serialize(set, context),
            context).Should().BeEquivalentTo(set);
        MemoryPackSerializer.Deserialize<ContextDictionary<string, int>>(
            MemoryPackSerializer.Serialize(dictionary, context),
            context).Should().Equal(dictionary);
    }

    [Fact]
    public void Registration_PropagatesThroughUnion()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new Impl1OffsetFormatter(1000))
            .Build();
        IUnionInterface value = new Impl1
        {
            MyProperty = 10,
            Foo = 20,
        };

        var payload = MemoryPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(MemoryPackSerializer.Serialize(value));

        var decoded = MemoryPackSerializer.Deserialize<IUnionInterface>(
            payload,
            context);
        decoded.Should().BeEquivalentTo(value);
    }

    static ContextGraph CreateGraph() => new()
    {
        Name = "graph",
        Item = new StandardTypeOne { One = 10 },
        Items = [new StandardTypeOne { One = 20 }],
        List = [new StandardTypeOne { One = 30 }],
        Map = new() { ["item"] = new StandardTypeOne { One = 40 } },
        Pair = (50, "tuple"),
        Optional = 60,
    };

    static ReadOnlySequence<int> CreateSegmentedIntSequence(int[] values)
    {
        var first = new IntSequenceSegment(values.AsMemory(0, 2));
        var last = first.Append(values.AsMemory(2, 1));
        return new ReadOnlySequence<int>(
            first,
            0,
            last,
            last.Memory.Length);
    }

    static void AssertArrayEqual(Array expected, Array actual)
    {
        actual.Rank.Should().Be(expected.Rank);
        for (var dimension = 0; dimension < expected.Rank; dimension++)
        {
            actual.GetLength(dimension).Should().Be(expected.GetLength(dimension));
        }
        actual.Cast<object?>().Should().Equal(expected.Cast<object?>());
    }

    static void AssertContextPayloadRoundTrip<T>(
        T value,
        MemoryPackSerializerContext context)
    {
        var payload = MemoryPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(MemoryPackSerializer.Serialize(value));
        MemoryPackSerializer.Deserialize<T>(payload, context)
            .Should().BeEquivalentTo(value);
    }
}

public sealed class IntSequenceSegment : ReadOnlySequenceSegment<int>
{
    public IntSequenceSegment(ReadOnlyMemory<int> memory)
    {
        Memory = memory;
    }

    public IntSequenceSegment Append(ReadOnlyMemory<int> memory)
    {
        var segment = new IntSequenceSegment(memory)
        {
            RunningIndex = RunningIndex + Memory.Length,
        };
        Next = segment;
        return segment;
    }
}

public sealed class ContextList<T> : List<T>;

public sealed class ContextSet<T> : HashSet<T>;

public sealed class ContextDictionary<TKey, TValue> : Dictionary<TKey, TValue>
    where TKey : notnull;

[MemoryPackable]
public partial class ContextGraph
{
    public string? Name { get; set; }
    public StandardTypeOne? Item { get; set; }
    public StandardTypeOne[]? Items { get; set; }
    public List<StandardTypeOne>? List { get; set; }
    public Dictionary<string, StandardTypeOne>? Map { get; set; }
    public (int, string)? Pair { get; set; }
    public int? Optional { get; set; }
}

[MemoryPackable]
public partial class ContextIntArrayContainer
{
    public int[]? Values { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantIntArray
{
    [MemoryPackOrder(0)]
    public int[]? Values { get; set; }

    [MemoryPackOrder(1)]
    public string? Tail { get; set; }
}

[MemoryPackable]
public partial class ContextObjectScalars
{
    public int First { get; set; }
    public int Second { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantScalars
{
    [MemoryPackOrder(0)]
    public int First { get; set; }

    [MemoryPackOrder(1)]
    public int Second { get; set; }
}

[MemoryPackable(GenerateType.CircularReference)]
public partial class ContextCircularScalars
{
    [MemoryPackOrder(0)]
    public int First { get; set; }

    [MemoryPackOrder(1)]
    public int Second { get; set; }
}

public sealed class ContextCustomValue
{
    public int Value { get; set; }
}

public sealed class OffsetFormatter(int offset) : MemoryPackFormatter<ContextCustomValue>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref ContextCustomValue? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(value.Value + offset);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref ContextCustomValue? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out int encoded);
        value = new ContextCustomValue { Value = encoded - offset };
    }
}

public sealed class IntOffsetFormatter(int offset) : MemoryPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + offset);

    public override void Deserialize(ref MemoryPackReader reader, scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - offset;
    }
}

public sealed class VarIntIntFormatter : MemoryPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteVarInt(value);

    public override void Deserialize(ref MemoryPackReader reader, scoped ref int value)
        => value = reader.ReadVarIntInt32();
}

public sealed class StandardTypeOneOffsetFormatter(int offset)
    : MemoryPackFormatter<StandardTypeOne>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref StandardTypeOne? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(value.One + offset);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref StandardTypeOne? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out int encoded);
        value = new StandardTypeOne { One = encoded - offset };
    }
}

public sealed class Impl1OffsetFormatter(int offset) : MemoryPackFormatter<Impl1>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref Impl1? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(2);
        writer.WriteUnmanaged(value.MyProperty + offset, value.Foo + offset);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref Impl1? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 2)
        {
            MemoryPackSerializationException.ThrowInvalidPropertyCount(2, count);
        }

        reader.ReadUnmanaged(out int property, out long foo);
        value = new Impl1
        {
            MyProperty = property - offset,
            Foo = foo - offset,
        };
    }
}
