using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpPack.Compression;

namespace SharpPack.Tests;

public class ReadLimitsTest
{
    [Fact]
    public void PayloadLimit_ExactSucceeds_AndPlusOneFailsForSpanAndSequence()
    {
        int[] value = [1, 2, 3];
        var payload = SharpPackSerializer.Serialize(value);
        var exact = CreateContext(maxPayloadBytes: payload.Length);
        var tooSmall = CreateContext(maxPayloadBytes: payload.Length - 1);

        SharpPackSerializer.Deserialize<int[]>(payload, exact)
            .Should().Equal(value);

        var sequence = CreateSegmented(payload);
        SharpPackSerializer.Deserialize<int[]>(sequence, exact)
            .Should().Equal(value);

        var spanFailure = () =>
            SharpPackSerializer.Deserialize<int[]>(payload, tooSmall);
        var error = spanFailure.Should().Throw<SharpPackSerializationException>().Which;
        Assert.Equal(SharpPackErrorCode.ResourceLimitExceeded, error.ErrorCode);
        Assert.Equal(SharpPackReadLimitKind.PayloadBytes, error.ReadLimitKind);

        var sequenceFailure = () =>
            SharpPackSerializer.Deserialize<int[]>(sequence, tooSmall);
        sequenceFailure.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void PayloadLimit_DefaultDoesNotCapTheLengthOfASegmentedBuffer()
    {
        var prefix = SharpPackSerializer.Serialize(42);
        var trailingSegment = new byte[1024 * 1024];
        var sequence = ReadOnlySequenceBuilder.Create(
            Enumerable.Repeat(trailingSegment, 2048).Prepend(prefix).ToArray());
        Assert.True(sequence.Length > int.MaxValue);

        Assert.Equal(42, SharpPackSerializer.Deserialize<int>(sequence, new SharpPackSerializerContext()));
        var error = Assert.Throws<SharpPackSerializationException>(() =>
            SharpPackSerializer.Deserialize<int>(sequence, CreateContext(maxPayloadBytes: int.MaxValue)));
        Assert.Equal(SharpPackReadLimitKind.PayloadBytes, error.ReadLimitKind);
    }

    [Fact]
    public async Task PayloadLimit_LengthDelimitedStreamRejectsBeforeConsumption()
    {
        int[] value = [1, 2, 3];
        var payload = SharpPackSerializer.Serialize(value);
        using var stream = new MemoryStream(payload);
        var context = CreateContext(maxPayloadBytes: payload.Length - 1);

        await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackSerializer.DeserializeAsync<int[]>(
                stream,
                payload.Length,
                context));

        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task PayloadLimit_UnframedMemoryStreamUsesConsumedValueLength()
    {
        var first = SharpPackSerializer.Serialize(123);
        var second = SharpPackSerializer.Serialize(456);
        var bytes = new byte[first.Length + second.Length];
        first.CopyTo(bytes, 0);
        second.CopyTo(bytes, first.Length);
        using var stream = new MemoryStream(
            bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        var context = CreateContext(maxPayloadBytes: first.Length);

        var value = await SharpPackSerializer.DeserializeAsync<int>(
            stream,
            context);

        value.Should().Be(123);
        stream.Position.Should().Be(first.Length);
    }

    [Fact]
    public async Task PayloadLimit_NonExposedMemoryStreamRetainsReadToEndContract()
    {
        var first = SharpPackSerializer.Serialize(123);
        var second = SharpPackSerializer.Serialize(456);
        var bytes = new byte[first.Length + second.Length];
        first.CopyTo(bytes, 0);
        second.CopyTo(bytes, first.Length);
        using var stream = new MemoryStream(bytes);
        Assert.False(stream.TryGetBuffer(out _));
        var failure = await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackSerializer.DeserializeAsync<int>(
                stream, CreateContext(maxPayloadBytes: first.Length)));
        Assert.Equal(SharpPackReadLimitKind.PayloadBytes, failure.ReadLimitKind);
        Assert.Equal(first.Length + 1, stream.Position);
    }

    [Fact]
    public void DepthLimit_IsInclusive_AndNextLevelFails()
    {
        var value = new ReadLimitDepthNode
        {
            Next = new ReadLimitDepthNode
            {
                Next = new ReadLimitDepthNode(),
            },
        };
        var payload = SharpPackSerializer.Serialize(value);

        var exact = CreateContext(
            readLimits: SharpPackReadLimits.Default with { MaxDepth = 4 });
        SharpPackSerializer.Deserialize<ReadLimitDepthNode>(payload, exact)
            .Should().BeEquivalentTo(value);

        var tooShallow = CreateContext(
            readLimits: SharpPackReadLimits.Default with { MaxDepth = 3 });
        var deserialize = () =>
            SharpPackSerializer.Deserialize<ReadLimitDepthNode>(
                payload,
                tooShallow);

        deserialize.Should().Throw<SharpPackSerializationException>()
            .Which.ReadLimitKind.Should().Be(SharpPackReadLimitKind.Depth);
    }

    [Fact]
    public void DepthLimit_ContinuesAcrossBrotliMembers()
    {
        var payload = SharpPackSerializer.Serialize(new BrotliIntEnvelope { Value = 123 });

        Assert.Equal(123, SharpPackSerializer.Deserialize<BrotliIntEnvelope>(payload, AtDepth(3))!.Value);
        var error = Assert.Throws<SharpPackSerializationException>(() =>
            SharpPackSerializer.Deserialize<BrotliIntEnvelope>(payload, AtDepth(2)));
        Assert.Equal(SharpPackErrorCode.DepthLimitExceeded, error.ErrorCode);
        Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
    }

    [Fact]
    public void DepthLimit_CountsGeneratedArrayElementScopes()
    {
        var value = new ReadLimitArrayNode { Children = [new() { Children = [new()] }] };
        var payload = SharpPackSerializer.Serialize(value);

        SharpPackSerializer.Deserialize<ReadLimitArrayNode>(payload, AtDepth(3))
            .Should().BeEquivalentTo(value);
        var error = Assert.Throws<SharpPackSerializationException>(() =>
            SharpPackSerializer.Deserialize<ReadLimitArrayNode>(payload, AtDepth(2)));
        Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4097, false)] // Above the capped initial list capacity.
    [InlineData(4097, true)]
    public void DepthLimit_CountsGeneratedListElementsIndependentlyOfCapacity(int count, bool preallocated)
    {
        var value = new ReadLimitListNode { Children = Enumerable.Repeat<ReadLimitListNode?>(new(), count).ToList() };
        var payload = SharpPackSerializer.Serialize(value);
        var existing = preallocated ? new List<ReadLimitListNode?>(count) : null;
        ReadLimitListNode? destination = preallocated ? new() { Children = existing } : null;

        SharpPackSerializer.Deserialize(payload, ref destination, AtDepth(2));
        Assert.Equal(count, destination!.Children!.Count);
        if (preallocated)
        {
            Assert.Same(existing, destination.Children);
        }

        var marker = new ReadLimitListNode();
        var retained = preallocated ? new List<ReadLimitListNode?>(count) { marker } : null;
        destination = preallocated ? new() { Children = retained } : null;
        var error = Assert.Throws<SharpPackSerializationException>(() =>
            SharpPackSerializer.Deserialize(payload, ref destination, AtDepth(1)));
        Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
        if (preallocated)
        {
            Assert.Same(retained, destination!.Children);
            Assert.Same(marker, Assert.Single(retained!));
        }
    }

    [Fact]
    public void DepthLimit_CountsGeneratedCollectionSetAndDictionaryElements()
    {
        AssertDepthLimit(new ReadLimitCollection { new() { new() } });
        AssertDepthLimit(new ReadLimitSet { new() { new() } });
        AssertDepthLimit(new ReadLimitDictionary { [1] = new() { [2] = new() } });

        static void AssertDepthLimit<T>(T value) where T : class
        {
            var payload = SharpPackSerializer.Serialize(value);
            T? result = null;
            Assert.Equal(payload.Length, SharpPackSerializer.Deserialize(payload, ref result, AtDepth(3)));
            Assert.NotNull(result);
            var error = Assert.Throws<SharpPackSerializationException>(() =>
                SharpPackSerializer.Deserialize<T>(payload, AtDepth(2)));
            Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
        }
    }

    [Fact]
    public void CollectionLimit_ExactSucceeds_AndPlusOneFailsBeforeReplacement()
    {
        int[] value = [1, 2, 3];
        var payload = SharpPackSerializer.Serialize(value);
        var exact = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxCollectionLength = 3,
            });

        SharpPackSerializer.Deserialize<int[]>(payload, exact)
            .Should().Equal(value);

        var limited = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxCollectionLength = 2,
            });
        int[]? destination = [99];
        var original = destination;

        var deserialize = () =>
            SharpPackSerializer.Deserialize(payload, ref destination, limited);

        var error = deserialize.Should().Throw<SharpPackSerializationException>().Which;
        Assert.Equal(SharpPackErrorCode.ResourceLimitExceeded, error.ErrorCode);
        Assert.Equal(SharpPackReadLimitKind.CollectionLength, error.ReadLimitKind);
        destination.Should().BeSameAs(original);
        destination.Should().Equal(99);

        SharpPackSerializer.Deserialize<int[]>(
            SharpPackSerializer.Serialize(new[] { 1, 2 }),
            limited).Should().Equal(1, 2);
    }

    [Fact]
    public void CollectionLimit_CoversCompactArrayAndListAtLargerCounts()
    {
        var values = new int[4097];
        var context = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxCollectionLength = 4096,
            });

        var array = () => SharpPackSerializer.Deserialize<int[]>(
            SharpPackSerializer.Serialize(values),
            context);
        array.Should().Throw<SharpPackSerializationException>();

        var list = () => SharpPackSerializer.Deserialize<List<int>>(
            SharpPackSerializer.Serialize(new List<int>(values)),
            context);
        list.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void CollectionLimit_CoversListGeneratedDangerousAndMultidimensionalPaths()
    {
        var limits = SharpPackReadLimits.Default with
        {
            MaxCollectionLength = 2,
        };
        var context = CreateContext(readLimits: limits);

        var list = () => SharpPackSerializer.Deserialize<List<int>>(
            SharpPackSerializer.Serialize(new List<int> { 1, 2, 3 }),
            context);
        list.Should().Throw<SharpPackSerializationException>();

        var generatedPayload = SharpPackSerializer.Serialize(
            new ReadLimitArrayEnvelope { Values = [1, 2, 3] });
        var generated = () =>
            SharpPackSerializer.Deserialize<ReadLimitArrayEnvelope>(
                generatedPayload,
                context);
        generated.Should().Throw<SharpPackSerializationException>();

        var bitPackContext = new SharpPackSerializerContextBuilder()
            .Configure(
                SharpPackSerializerConfiguration.Default with
                {
                    ReadLimits = limits,
                })
            .Register(BitPackFormatter.Default)
            .Build();
        bool[] bits = [true, false, true];
        var bitPayload = SharpPackSerializer.Serialize(bits, bitPackContext);
        var bitPack = () =>
            SharpPackSerializer.Deserialize<bool[]>(bitPayload, bitPackContext);
        bitPack.Should().Throw<SharpPackSerializationException>();

        var multidimensional = new int[,] { { 1, 2, 3 } };
        var multidimensionalPayload =
            SharpPackSerializer.Serialize(multidimensional);
        var multi = () =>
            SharpPackSerializer.Deserialize<int[,]>(
                multidimensionalPayload,
                context);
        multi.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void CollectionLimit_DoesNotApplyToStrings()
    {
        var context = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxCollectionLength = 1,
            });
        const string value = "not-a-collection-budget";

        SharpPackSerializer.Deserialize<string>(
            SharpPackSerializer.Serialize(value),
            context).Should().Be(value);
    }

    [Fact]
    public void CollectionLimit_CoversMultiSegmentSequence()
    {
        int[] value = [1, 2, 3];
        var sequence = CreateSegmented(SharpPackSerializer.Serialize(value));
        var context = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxCollectionLength = 2,
            });

        var deserialize = () =>
            SharpPackSerializer.Deserialize<int[]>(sequence, context);

        deserialize.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void ReferenceLimit_ExactSucceeds_NextInsertionFails_AndStateResets()
    {
        var single = new ReadLimitReferenceNode();
        single.Next = single;
        var singlePayload = SharpPackSerializer.Serialize(single);

        var two = new ReadLimitReferenceNode();
        two.Next = new ReadLimitReferenceNode { Next = two };
        var twoPayload = SharpPackSerializer.Serialize(two);

        var context = CreateContext(
            readLimits: SharpPackReadLimits.Default with
            {
                MaxReferenceCount = 1,
            });

        var restored = SharpPackSerializer.Deserialize<ReadLimitReferenceNode>(
            singlePayload,
            context);
        restored!.Next.Should().BeSameAs(restored);

        var overflow = () =>
            SharpPackSerializer.Deserialize<ReadLimitReferenceNode>(
                twoPayload,
                context);
        overflow.Should().Throw<SharpPackSerializationException>()
            .Which.ReadLimitKind.Should().Be(SharpPackReadLimitKind.ReferenceCount);

        var afterFailure =
            SharpPackSerializer.Deserialize<ReadLimitReferenceNode>(
                singlePayload,
                context);
        afterFailure!.Next.Should().BeSameAs(afterFailure);
    }

    [Fact]
    public void DefaultLimits_PreserveExistingWireAndBehavior()
    {
        var value = new ReadLimitArrayEnvelope { Values = [1, 2, 3] };
        var context = new SharpPackSerializerContext();

        SharpPackSerializer.Serialize(value, context)
            .Should().Equal(SharpPackSerializer.Serialize(value));
        SharpPackSerializer.Deserialize<ReadLimitArrayEnvelope>(
            SharpPackSerializer.Serialize(value),
            context).Should().BeEquivalentTo(value);

        var defaultStructContext =
            new SharpPackSerializerContext(default);
        SharpPackSerializer.Deserialize<ReadLimitArrayEnvelope>(
            SharpPackSerializer.Serialize(value),
            defaultStructContext).Should().BeEquivalentTo(value);
    }

    static SharpPackSerializerContext AtDepth(int maxDepth)
        => CreateContext(readLimits: new SharpPackReadLimits { MaxDepth = maxDepth });

    static SharpPackSerializerContext CreateContext(
        SharpPackReadLimits? readLimits = null,
        int maxPayloadBytes = int.MaxValue)
        => new(
            SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = readLimits ?? SharpPackReadLimits.Default,
                MaxPayloadBytes = maxPayloadBytes,
            });

    static ReadOnlySequence<byte> CreateSegmented(byte[] payload)
    {
        var split = Math.Max(1, payload.Length / 2);
        var first = new ByteSegment(payload.AsMemory(0, split));
        var last = first.Append(payload.AsMemory(split));
        return new ReadOnlySequence<byte>(
            first,
            0,
            last,
            last.Memory.Length);
    }

    sealed class ByteSegment : ReadOnlySequenceSegment<byte>
    {
        public ByteSegment(ReadOnlyMemory<byte> memory)
            => Memory = memory;

        public ByteSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new ByteSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = next;
            return next;
        }
    }
}

[SharpPackable]
public partial class ReadLimitDepthNode
{
    public ReadLimitDepthNode? Next { get; set; }
}

[SharpPackable]
public partial class ReadLimitArrayNode
{
    public ReadLimitArrayNode?[]? Children { get; set; }
}

[SharpPackable]
public partial class ReadLimitListNode
{
    public List<ReadLimitListNode?>? Children { get; set; }
}

[SharpPackable(GenerateType.Collection)]
public partial class ReadLimitCollection : List<ReadLimitCollection?>
{
}

[SharpPackable(GenerateType.Collection)]
public partial class ReadLimitSet : HashSet<ReadLimitSet?>
{
}

[SharpPackable(GenerateType.Collection)]
public partial class ReadLimitDictionary : Dictionary<int, ReadLimitDictionary?>
{
}

[SharpPackable]
public partial class ReadLimitArrayEnvelope
{
    public int[]? Values { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class ReadLimitReferenceNode
{
    [SharpPackOrder(0)]
    public ReadLimitReferenceNode? Next { get; set; }
}
