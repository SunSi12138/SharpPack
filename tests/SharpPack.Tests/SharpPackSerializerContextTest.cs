using SharpPack.Tests.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class SharpPackSerializerContextTest
{
    [Fact]
    public void EmptyContext_PreservesRootUnmanagedStructPayload()
    {
        var value = new ContextUnmanagedStruct
        {
            Id = 42,
            Timestamp = 123_456_789,
        };
        var context = new SharpPackSerializerContext();
        var defaultPayload = SharpPackSerializer.Serialize(value);
        var contextPayload = SharpPackSerializer.Serialize(value, context);

        contextPayload.Should().Equal(defaultPayload);
        SharpPackSerializer.Deserialize<ContextUnmanagedStruct>(
            defaultPayload,
            context).Should().Be(value);
    }

    [Fact]
    public void FreshContext_ResolvesRootTypePayload()
    {
        var payload = SharpPackSerializer.Serialize<Type>(typeof(ContextGraph));
        var context = new SharpPackSerializerContext();

        SharpPackSerializer.Deserialize<Type>(payload, context)
            .Should().Be(typeof(ContextGraph));
    }

    [Fact]
    public void ExplicitContext_PreservesDefaultWireFormat()
    {
        var value = CreateGraph();
        var context = new SharpPackSerializerContext();

        var contextBytes = SharpPackSerializer.Serialize(value, context);
        var defaultBytes = SharpPackSerializer.Serialize(value);

        contextBytes.Should().Equal(defaultBytes);
        SharpPackSerializer.Deserialize<ContextGraph>(contextBytes, context)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void Configuration_IsOwnedByContext()
    {
        const string value = "明示的 context";
        var utf8 = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf8);
        var utf16 = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf16);

        var utf8Bytes = SharpPackSerializer.Serialize(value, utf8);
        var utf16Bytes = SharpPackSerializer.Serialize(value, utf16);

        utf8Bytes.Should().NotEqual(utf16Bytes);
        SharpPackSerializer.Deserialize<string>(utf8Bytes, utf8).Should().Be(value);
        SharpPackSerializer.Deserialize<string>(utf16Bytes, utf16).Should().Be(value);
    }

    [Fact]
    public void ThreadStateClearsContextAfterSuccessAndFormatterFailure()
    {
        const string value = "state isolation";
        var defaultPayload = SharpPackSerializer.Serialize(value);
        var prefixContext = new SharpPackSerializerContextBuilder()
            .Register(new PrefixStringFormatter())
            .Build();
        var throwingContext = new SharpPackSerializerContextBuilder()
            .Register(new ThrowingStringFormatter())
            .Build();

        var contextPayload = SharpPackSerializer.Serialize(value, prefixContext);
        SharpPackSerializer.Deserialize<string>(contextPayload, prefixContext)
            .Should().Be(value);
        SharpPackSerializer.Serialize(value).Should().Equal(defaultPayload);
        SharpPackSerializer.Deserialize<string>(defaultPayload).Should().Be(value);

        Assert.Throws<InvalidOperationException>(
            () => SharpPackSerializer.Serialize(value, throwingContext));
        SharpPackSerializer.Serialize(value).Should().Equal(defaultPayload);

        Assert.Throws<InvalidOperationException>(
            () => SharpPackSerializer.Deserialize<string>(
                defaultPayload,
                throwingContext));
        SharpPackSerializer.Deserialize<string>(defaultPayload).Should().Be(value);
    }

    [Fact]
    public void Registrations_AreFrozenAndIsolatedPerContext()
    {
        var first = new SharpPackSerializerContextBuilder()
            .Register(new OffsetFormatter(10))
            .Build();
        var second = new SharpPackSerializerContextBuilder()
            .Register(new OffsetFormatter(20))
            .Build();

        var value = new ContextCustomValue { Value = 1 };
        var firstBytes = SharpPackSerializer.Serialize(value, first);
        var secondBytes = SharpPackSerializer.Serialize(value, second);

        firstBytes.Should().NotEqual(secondBytes);
        SharpPackSerializer.Deserialize<ContextCustomValue>(firstBytes, first)!.Value
            .Should().Be(1);
        SharpPackSerializer.Deserialize<ContextCustomValue>(secondBytes, second)!.Value
            .Should().Be(1);
    }

    [Fact]
    public void Registration_PropagatesThroughNestedFormatterGraph()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Register(new StandardTypeOneOffsetFormatter(1000))
            .Build();
        var value = CreateGraph();

        var primitiveBytes = SharpPackSerializer.Serialize(10, context);
        var graphBytes = SharpPackSerializer.Serialize(value, context);

        primitiveBytes.Should().NotEqual(SharpPackSerializer.Serialize(10));
        graphBytes.Should().NotEqual(SharpPackSerializer.Serialize(value));
        SharpPackSerializer.Deserialize<int>(primitiveBytes, context).Should().Be(10);
        SharpPackSerializer.Deserialize<ContextGraph>(graphBytes, context)
            .Should().BeEquivalentTo(value);
    }

    [Fact]
    public void Registration_PropagatesThroughGeneratedGrandchildGraph()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var value = new ContextNestedRoot
        {
            Child = new ContextNestedChild { Value = 42 },
        };

        var defaultPayload = SharpPackSerializer.Serialize(value);
        var contextPayload = SharpPackSerializer.Serialize(value, context);

        contextPayload.Should().NotEqual(defaultPayload);
        SharpPackSerializer.Deserialize<ContextNestedRoot>(
            contextPayload,
            context)!.Child!.Value.Should().Be(42);
    }

    [Fact]
    public async Task ContextFormatterCreation_IsSafeUnderConcurrentFirstUse()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var value = new ContextNestedRoot
        {
            Child = new ContextNestedChild { Value = 42 },
        };

        var payloads = await Task.WhenAll(
            Enumerable.Range(0, Environment.ProcessorCount * 4)
                .Select(_ => Task.Run(() =>
                {
                    var payload = SharpPackSerializer.Serialize(value, context);
                    return SharpPackSerializer.Deserialize<ContextNestedRoot>(
                        payload,
                        context);
                })));

        payloads.Should().OnlyContain(
            static value =>
                value != null &&
                value.Child != null &&
                value.Child.Value == 42);
    }

    [Fact]
    public void RecursiveGeneratedGraph_WithLeafOverride_RoundTrips()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var value = new ContextRecursiveNode
        {
            Value = 1,
            Next = new ContextRecursiveNode { Value = 2 },
        };

        var payload = SharpPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));

        var decoded = SharpPackSerializer.Deserialize<ContextRecursiveNode>(
            payload,
            context);
        decoded!.Value.Should().Be(1);
        decoded.Next!.Value.Should().Be(2);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughBulkCollectionFastPaths()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var values = new[] { 1, 2, 3 };

        var arrayPayload = SharpPackSerializer.Serialize(values, context);
        arrayPayload.Should().NotEqual(SharpPackSerializer.Serialize(values));
        SharpPackSerializer.Deserialize<int[]>(arrayPayload, context)
            .Should().Equal(values);

        var list = values.ToList();
        var listPayload = SharpPackSerializer.Serialize(list, context);
        listPayload.Should().NotEqual(SharpPackSerializer.Serialize(list));
        SharpPackSerializer.Deserialize<List<int>>(listPayload, context)
            .Should().Equal(values);

        var segment = new ArraySegment<int>(values, 1, 2);
        var segmentPayload = SharpPackSerializer.Serialize(segment, context);
        segmentPayload.Should().NotEqual(SharpPackSerializer.Serialize(segment));
        SharpPackSerializer.Deserialize<ArraySegment<int>>(segmentPayload, context)
            .Should().Equal(segment);

        var memory = values.AsMemory();
        var memoryPayload = SharpPackSerializer.Serialize(memory, context);
        memoryPayload.Should().NotEqual(SharpPackSerializer.Serialize(memory));
        SharpPackSerializer.Deserialize<Memory<int>>(memoryPayload, context)
            .ToArray().Should().Equal(values);

        var readOnlyMemory = new ReadOnlyMemory<int>(values);
        var readOnlyMemoryPayload = SharpPackSerializer.Serialize(readOnlyMemory, context);
        readOnlyMemoryPayload.Should().NotEqual(
            SharpPackSerializer.Serialize(readOnlyMemory));
        SharpPackSerializer.Deserialize<ReadOnlyMemory<int>>(
            readOnlyMemoryPayload,
            context).ToArray().Should().Equal(values);

        var sequence = CreateSegmentedIntSequence(values);
        var sequencePayload = SharpPackSerializer.Serialize(sequence, context);
        sequencePayload.Should().NotEqual(SharpPackSerializer.Serialize(sequence));
        SharpPackSerializer.Deserialize<ReadOnlySequence<int>>(
            sequencePayload,
            context).ToArray().Should().Equal(values);

        var generated = new ContextIntArrayContainer { Values = values };
        var generatedPayload = SharpPackSerializer.Serialize(generated, context);
        generatedPayload.Should().NotEqual(SharpPackSerializer.Serialize(generated));
        SharpPackSerializer.Deserialize<ContextIntArrayContainer>(
            generatedPayload,
            context)!.Values.Should().Equal(values);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughVersionTolerantArray()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var value = new ContextVersionTolerantIntArray
        {
            Values = [1, 200, 30_000],
            Tail = "tail",
        };

        var payload = SharpPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));

        var decoded =
            SharpPackSerializer.Deserialize<ContextVersionTolerantIntArray>(
                payload,
                context);
        decoded.Should().BeEquivalentTo(value);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughMultiDimensionalArrays()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new IntOffsetFormatter(100))
            .Build();
        var two = new int[,] { { 1, 2 }, { 3, 4 } };
        var three = new int[,,] { { { 5, 6 }, { 7, 8 } } };
        var four = new int[,,,] { { { { 9, 10 }, { 11, 12 } } } };

        var twoPayload = SharpPackSerializer.Serialize(two, context);
        var threePayload = SharpPackSerializer.Serialize(three, context);
        var fourPayload = SharpPackSerializer.Serialize(four, context);

        twoPayload.Should().NotEqual(SharpPackSerializer.Serialize(two));
        threePayload.Should().NotEqual(SharpPackSerializer.Serialize(three));
        fourPayload.Should().NotEqual(SharpPackSerializer.Serialize(four));

        AssertArrayEqual(
            two,
            SharpPackSerializer.Deserialize<int[,]>(twoPayload, context)!);
        AssertArrayEqual(
            three,
            SharpPackSerializer.Deserialize<int[,,]>(threePayload, context)!);
        AssertArrayEqual(
            four,
            SharpPackSerializer.Deserialize<int[,,,]>(fourPayload, context)!);

        var overwrite = new int[2, 2];
        var original = overwrite;
        SharpPackSerializer.Deserialize(twoPayload, ref overwrite, context);
        overwrite.Should().BeSameAs(original);
        AssertArrayEqual(two, overwrite!);
    }

    [Fact]
    public void VariableLengthPrimitiveRegistration_PropagatesThroughMultiDimensionalArrays()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var two = new int[,] { { 1, 200 }, { 30_000, 4_000_000 } };
        var three = new int[,,] { { { 1, 200 }, { 30_000, 4_000_000 } } };
        var four = new int[,,,] { { { { 1, 200 }, { 30_000, 4_000_000 } } } };

        var twoPayload = SharpPackSerializer.Serialize(two, context);
        var threePayload = SharpPackSerializer.Serialize(three, context);
        var fourPayload = SharpPackSerializer.Serialize(four, context);

        twoPayload.Should().NotEqual(SharpPackSerializer.Serialize(two));
        threePayload.Should().NotEqual(SharpPackSerializer.Serialize(three));
        fourPayload.Should().NotEqual(SharpPackSerializer.Serialize(four));
        AssertArrayEqual(
            two,
            SharpPackSerializer.Deserialize<int[,]>(twoPayload, context)!);
        AssertArrayEqual(
            three,
            SharpPackSerializer.Deserialize<int[,,]>(threePayload, context)!);
        AssertArrayEqual(
            four,
            SharpPackSerializer.Deserialize<int[,,,]>(fourPayload, context)!);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughGeneratedScalarFastPaths()
    {
        var context = new SharpPackSerializerContextBuilder()
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
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();

        int? nullable = 30_000;
        var nullablePayload = SharpPackSerializer.Serialize(nullable, context);
        nullablePayload.Should().NotEqual(SharpPackSerializer.Serialize(nullable));
        SharpPackSerializer.Deserialize<int?>(nullablePayload, context)
            .Should().Be(nullable);

        var pair = new KeyValuePair<int, int>(200, 30_000);
        var pairPayload = SharpPackSerializer.Serialize(pair, context);
        pairPayload.Should().NotEqual(SharpPackSerializer.Serialize(pair));
        SharpPackSerializer.Deserialize<KeyValuePair<int, int>>(
            pairPayload,
            context).Should().Be(pair);

        var tuple = (First: 200, Second: 30_000);
        var tuplePayload = SharpPackSerializer.Serialize(tuple, context);
        tuplePayload.Should().NotEqual(SharpPackSerializer.Serialize(tuple));
        SharpPackSerializer.Deserialize<(int First, int Second)>(
            tuplePayload,
            context).Should().Be(tuple);

        var dictionary = new Dictionary<int, int>
        {
            [200] = 30_000,
            [4_000_000] = 1,
        };
        var dictionaryPayload = SharpPackSerializer.Serialize(dictionary, context);
        dictionaryPayload.Should().NotEqual(
            SharpPackSerializer.Serialize(dictionary));
        SharpPackSerializer.Deserialize<Dictionary<int, int>>(
            dictionaryPayload,
            context).Should().Equal(dictionary);

        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(30_000, 200);
        queue.Enqueue(4_000_000, 1);
        var queuePayload = SharpPackSerializer.Serialize(queue, context);
        queuePayload.Should().NotEqual(SharpPackSerializer.Serialize(queue));
        var decodedQueue = SharpPackSerializer.Deserialize<PriorityQueue<int, int>>(
            queuePayload,
            context)!;
        decodedQueue.Dequeue().Should().Be(4_000_000);
        decodedQueue.Dequeue().Should().Be(30_000);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughNullableCompositeShapes()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        int?[] array = [1, null, 30_000];
        var pair = new KeyValuePair<int?, int?>(200, 30_000);
        var tuple = ((int?)200, (int?)30_000);

        var arrayPayload = SharpPackSerializer.Serialize(array, context);
        arrayPayload.Should().NotEqual(SharpPackSerializer.Serialize(array));
        SharpPackSerializer.Deserialize<int?[]>(arrayPayload, context)
            .Should().Equal(array);

        var pairPayload = SharpPackSerializer.Serialize(pair, context);
        pairPayload.Should().NotEqual(SharpPackSerializer.Serialize(pair));
        SharpPackSerializer.Deserialize<KeyValuePair<int?, int?>>(
            pairPayload,
            context).Should().Be(pair);

        var tuplePayload = SharpPackSerializer.Serialize(tuple, context);
        tuplePayload.Should().NotEqual(SharpPackSerializer.Serialize(tuple));
        SharpPackSerializer.Deserialize<(int?, int?)>(tuplePayload, context)
            .Should().Be(tuple);
    }

    [Fact]
    public void ExactArrayRegistration_PropagatesThroughGeneratedShapes()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntArrayFormatter())
            .Build();
        var values = new[] { 1, 200, 30_000 };
        var objectValue = new ContextIntArrayContainer { Values = values };
        var versionTolerantValue = new ContextVersionTolerantIntArray
        {
            Values = values,
            Tail = "tail",
        };

        AssertContextPayloadRoundTrip(values, context);
        AssertContextPayloadRoundTrip(objectValue, context);
        AssertContextPayloadRoundTrip(versionTolerantValue, context);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughGeneratedCompositeMembers()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var objectValue = new ContextCompositeScalars
        {
            Pair = new KeyValuePair<int, int>(200, 30_000),
            Tuple = (4_000_000, 1),
        };
        var versionTolerantValue = new ContextVersionTolerantCompositeScalars
        {
            Pair = new KeyValuePair<int, int>(1, 4_000_000),
            Tuple = (200, 30_000),
        };

        AssertContextPayloadRoundTrip(objectValue, context);
        AssertContextPayloadRoundTrip(versionTolerantValue, context);
    }

    [Fact]
    public void StringRegistration_PropagatesThroughGeneratedShapes()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new PrefixStringFormatter())
            .Build();
        var objectValue = new ContextStringContainer { Value = "object" };
        var versionTolerantValue = new ContextVersionTolerantStringContainer
        {
            Value = "version-tolerant",
        };

        AssertContextPayloadRoundTrip("root", context);
        AssertContextPayloadRoundTrip(objectValue, context);
        AssertContextPayloadRoundTrip(versionTolerantValue, context);
    }

    [Fact]
    public void PrimitiveRegistration_PropagatesThroughValueTupleRest()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new VarIntIntFormatter())
            .Build();
        var value = (
            1L,
            2L,
            3L,
            4L,
            5L,
            6L,
            7L,
            30_000);

        var payload = SharpPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));
        SharpPackSerializer.Deserialize<
            (long, long, long, long, long, long, long, int)>(
                payload,
                context).Should().Be(value);
    }

    [Fact]
    public void UnrelatedRegistration_PreservesBulkCollectionFastPathWireFormat()
    {
        var context = new SharpPackSerializerContextBuilder()
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

        SharpPackSerializer.Serialize(values, context)
            .Should().Equal(SharpPackSerializer.Serialize(values));
        SharpPackSerializer.Serialize(values.ToList(), context)
            .Should().Equal(SharpPackSerializer.Serialize(values.ToList()));
        SharpPackSerializer.Serialize(two, context)
            .Should().Equal(SharpPackSerializer.Serialize(two));
        SharpPackSerializer.Serialize(three, context)
            .Should().Equal(SharpPackSerializer.Serialize(three));
        SharpPackSerializer.Serialize(four, context)
            .Should().Equal(SharpPackSerializer.Serialize(four));
        SharpPackSerializer.Serialize(nullable, context)
            .Should().Equal(SharpPackSerializer.Serialize(nullable));
        SharpPackSerializer.Serialize(pair, context)
            .Should().Equal(SharpPackSerializer.Serialize(pair));
        SharpPackSerializer.Serialize(tuple, context)
            .Should().Equal(SharpPackSerializer.Serialize(tuple));
        SharpPackSerializer.Serialize(dictionary, context)
            .Should().Equal(SharpPackSerializer.Serialize(dictionary));
    }

    [Fact]
    public void Builder_CanOnlyBuildOnce()
    {
        var builder = new SharpPackSerializerContextBuilder();
        _ = builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(
            () => builder.Register(new OffsetFormatter(1)));
    }

    [Fact]
    public void Builder_RegistersClosedGenericCollections()
    {
        var context = new SharpPackSerializerContextBuilder()
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

        SharpPackSerializer.Deserialize<ContextList<int>>(
            SharpPackSerializer.Serialize(list, context),
            context).Should().Equal(list);
        SharpPackSerializer.Deserialize<ContextSet<int>>(
            SharpPackSerializer.Serialize(set, context),
            context).Should().BeEquivalentTo(set);
        SharpPackSerializer.Deserialize<ContextDictionary<string, int>>(
            SharpPackSerializer.Serialize(dictionary, context),
            context).Should().Equal(dictionary);
    }

    [Fact]
    public void Registration_PropagatesThroughUnion()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register(new Impl1OffsetFormatter(1000))
            .Build();
        IUnionInterface value = new Impl1
        {
            MyProperty = 10,
            Foo = 20,
        };

        var payload = SharpPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));

        var decoded = SharpPackSerializer.Deserialize<IUnionInterface>(
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
        SharpPackSerializerContext context)
    {
        var payload = SharpPackSerializer.Serialize(value, context);
        payload.Should().NotEqual(SharpPackSerializer.Serialize(value));
        SharpPackSerializer.Deserialize<T>(payload, context)
            .Should().BeEquivalentTo(value);
    }
}

[SharpPackable]
public partial struct ContextUnmanagedStruct
{
    public int Id { get; set; }

    public long Timestamp { get; set; }
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

[SharpPackable]
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

[SharpPackable]
public partial class ContextNestedRoot
{
    public ContextNestedChild? Child { get; set; }
}

[SharpPackable]
public partial class ContextNestedChild
{
    public int Value { get; set; }
}

[SharpPackable]
public partial class ContextRecursiveNode
{
    public int Value { get; set; }
    public ContextRecursiveNode? Next { get; set; }
}

[SharpPackable]
public partial class ContextIntArrayContainer
{
    public int[]? Values { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantIntArray
{
    [SharpPackOrder(0)]
    public int[]? Values { get; set; }

    [SharpPackOrder(1)]
    public string? Tail { get; set; }
}

[SharpPackable]
public partial class ContextObjectScalars
{
    public int First { get; set; }
    public int Second { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantScalars
{
    [SharpPackOrder(0)]
    public int First { get; set; }

    [SharpPackOrder(1)]
    public int Second { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class ContextCircularScalars
{
    [SharpPackOrder(0)]
    public int First { get; set; }

    [SharpPackOrder(1)]
    public int Second { get; set; }
}

[SharpPackable]
public partial class ContextCompositeScalars
{
    public KeyValuePair<int, int> Pair { get; set; }
    public (int First, int Second) Tuple { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantCompositeScalars
{
    [SharpPackOrder(0)]
    public KeyValuePair<int, int> Pair { get; set; }

    [SharpPackOrder(1)]
    public (int First, int Second) Tuple { get; set; }
}

[SharpPackable]
public partial class ContextStringContainer
{
    public string? Value { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class ContextVersionTolerantStringContainer
{
    [SharpPackOrder(0)]
    public string? Value { get; set; }
}

public sealed class ContextCustomValue
{
    public int Value { get; set; }
}

public sealed class OffsetFormatter(int offset) : SharpPackFormatter<ContextCustomValue>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
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
        ref SharpPackReader reader,
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

public sealed class IntOffsetFormatter(int offset) : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteUnmanaged(value + offset);

    public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - offset;
    }
}

public sealed class VarIntIntFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteVarInt(value);

    public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
        => value = reader.ReadVarIntInt32();
}

public sealed class VarIntIntArrayFormatter : SharpPackFormatter<int[]>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int[]? value)
    {
        if (value is null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }

        writer.WriteCollectionHeader(value.Length);
        foreach (var item in value)
        {
            writer.WriteVarInt(item);
        }
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int[]? value)
    {
        if (!reader.TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        value = new int[length];
        for (var index = 0; index < length; index++)
        {
            value[index] = reader.ReadVarIntInt32();
        }
    }
}

public sealed class PrefixStringFormatter : SharpPackFormatter<string>
{
    const string Prefix = "context:";

    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref string? value)
        => writer.WriteString(value is null ? null : Prefix + value);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref string? value)
    {
        var encoded = reader.ReadString();
        if (encoded is null)
        {
            value = null;
            return;
        }
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new SharpPackSerializationException(
                "The context string prefix is missing.");
        }
        value = encoded[Prefix.Length..];
    }
}

public sealed class ThrowingStringFormatter : SharpPackFormatter<string>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref string? value)
        => throw new InvalidOperationException("Injected formatter failure.");

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref string? value)
        => throw new InvalidOperationException("Injected formatter failure.");
}

public sealed class StandardTypeOneOffsetFormatter(int offset)
    : SharpPackFormatter<StandardTypeOne>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
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
        ref SharpPackReader reader,
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

public sealed class Impl1OffsetFormatter(int offset) : SharpPackFormatter<Impl1>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
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
        ref SharpPackReader reader,
        scoped ref Impl1? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 2)
        {
            SharpPackSerializationException.ThrowInvalidPropertyCount(2, count);
        }

        reader.ReadUnmanaged(out int property, out long foo);
        value = new Impl1
        {
            MyProperty = property - offset,
            Foo = foo - offset,
        };
    }
}
