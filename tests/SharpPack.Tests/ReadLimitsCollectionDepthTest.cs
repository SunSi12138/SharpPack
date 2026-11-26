using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using SharpPack.Formatters;

namespace SharpPack.Tests;

public class ReadLimitsCollectionDepthTest
{
    [Fact]
    public void ManagedMultidimensionalArraysUseOneElementScopePerArray()
    {
        var item = new ReadLimitCollectionItem { Value = 7 };
        Assert.Equal(7, ReadAtBoundary(new[,] { { item, item } })[0, 1].Value);
        Assert.Equal(7, ReadAtBoundary(new[,,] { { { item, item } } })[0, 0, 1].Value);
        Assert.Equal(7, ReadAtBoundary(new[,,,] { { { { item, item } } } })[0, 0, 0, 1].Value);
    }

    [Fact]
    public void MutableCollectionsCheckBeforeClearingAnExistingDestination()
    {
        var earlier = new ReadLimitCollectionItem { Value = 9 };
        Queue<ReadLimitCollectionItem>? destination = new([earlier]);
        var original = destination;
        var payload = SharpPackSerializer.Serialize(new Queue<ReadLimitCollectionItem>(
            [new() { Value = 3 }, new() { Value = 7 }]));

        AssertDepthFailure(() => SharpPackSerializer.Deserialize(payload, ref destination, AtDepth(1)));
        Assert.Same(original, destination);
        Assert.Same(earlier, Assert.Single(destination!));

        Assert.Equal(payload.Length, SharpPackSerializer.Deserialize(payload, ref destination, AtDepth(2)));
        Assert.Same(original, destination);
        Assert.Equal(new[] { 3, 7 }, destination!.Select(item => item.Value));

        Assert.Equal(new[] { 2, 1 }, ReadAtBoundary(new Stack<int>([1, 2])));
        Assert.Equal(2, ReadAtBoundary(new HashSet<int> { 1, 2 }).Count);
    }

    [Fact]
    public void CollectionScopeUnwindsWhenAnElementReadFails()
    {
        var first = SharpPackSerializer.Serialize(new Queue<ReadLimitDepthNode>([new()]));
        var second = SharpPackSerializer.Serialize(new Queue<int>([17]));
        var payload = first.Concat(second).ToArray();
        using var state = SharpPackReaderOptionalStatePool.Rent(AtDepth(2));
        using var reader = new SharpPackReader(payload, state);

        try
        {
            reader.ReadValue<Queue<ReadLimitDepthNode>>();
            Assert.Fail("The inner object field must exceed the depth limit.");
        }
        catch (SharpPackSerializationException error)
        {
            Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
        }

        reader.Advance(first.Length - reader.Consumed);
        Assert.Equal(17, Assert.Single(reader.ReadValue<Queue<int>>()!));
        Assert.Equal(payload.Length, reader.Consumed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ImmutableSingletonAndBuilderPathsHaveTheSameDepth(int count)
    {
        var items = Enumerable.Range(1, count).ToArray();
        Assert.Equal(items, ReadAtBoundary(ImmutableList.CreateRange(items)));
        Assert.Equal(items, ReadAtBoundary<IImmutableList<int>>(ImmutableList.CreateRange(items)));
        Assert.Equal(items, ReadAtBoundary(ImmutableQueue.CreateRange(items)));
        Assert.Equal(items.Reverse(), ReadAtBoundary(ImmutableStack.CreateRange(items)));
        Assert.Equal(count, ReadAtBoundary<IImmutableSet<int>>(ImmutableHashSet.CreateRange(items)).Count);
    }

    [Fact]
    public void InterfaceAndFrozenCollectionsBoundTheirElementDispatch()
    {
        var items = new[] { new ReadLimitCollectionItem { Value = 3 }, new ReadLimitCollectionItem { Value = 7 } };
        Assert.Equal(2, ReadAtBoundary<IReadOnlySet<ReadLimitCollectionItem>>(items.ToHashSet()).Count);
        Assert.Equal(2, ReadAtBoundary(items.ToFrozenSet()).Count);

        var lookup = ReadAtBoundary(items.ToLookup(item => item.Value), depth: 3);
        Assert.Equal(7, Assert.Single(lookup[7]).Value);

        var dict = new Dictionary<int, ReadLimitCollectionItem> { [1] = items[0], [2] = items[1] };
        Assert.Equal(7, ReadAtBoundary(dict)[2].Value);
        Assert.Equal(7, ReadAtBoundary<IReadOnlyDictionary<int, ReadLimitCollectionItem>>(dict)[2].Value);
        Assert.Equal(7, ReadAtBoundary(dict.ToImmutableDictionary())[2].Value);
        Assert.Equal(7, ReadAtBoundary(dict.ToFrozenDictionary())[2].Value);
    }

    [Fact]
    public void RawAndEmptyCollectionsDoNotEnterAnElementScope()
    {
        var shallow = AtDepth(1);
        Assert.Empty(SharpPackSerializer.Deserialize<Queue<int>>(
            SharpPackSerializer.Serialize(new Queue<int>()), shallow)!);
        Assert.Empty(SharpPackSerializer.Deserialize<ImmutableList<int>>(
            SharpPackSerializer.Serialize(ImmutableList<int>.Empty), shallow)!);
        Assert.Equal(7, SharpPackSerializer.Deserialize<int[,]>(
            SharpPackSerializer.Serialize(new[,] { { 3, 7 } }), shallow)![0, 1]);
        Assert.Equal(3, SharpPackSerializer.Deserialize<ReadLimitCollectionItem[,]>(
            SharpPackSerializer.Serialize(new ReadLimitCollectionItem[3, 0]), shallow)!.GetLength(0));

        var value = new Dictionary<int, int> { [1] = 7, [2] = 9 };
        var payload = SharpPackSerializer.Serialize(value);
        Assert.Equal(9, SharpPackSerializer.Deserialize<Dictionary<int, int>>(payload, shallow)![2]);

        var withFormatter = AtDepth(2, builder => builder.Register(new IdentityIntFormatter()));
        var tooShallow = AtDepth(1, builder => builder.Register(new IdentityIntFormatter()));
        var withFormatterPayload = SharpPackSerializer.Serialize(value, withFormatter);
        AssertDepthFailure(() => SharpPackSerializer.Deserialize<Dictionary<int, int>>(withFormatterPayload, tooShallow));
        Assert.Equal(9, SharpPackSerializer.Deserialize<Dictionary<int, int>>(withFormatterPayload, withFormatter)![2]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4097)] // Also cover the generic formatter above the initial-capacity threshold.
    public void GenericRawListDoesNotDependOnTheSpecializedResolver(int count)
    {
        var list = Enumerable.Range(0, count).ToList();
        var shallow = AtDepth(1, builder => builder.Register(new ListFormatter<int>()));
        var payload = SharpPackSerializer.Serialize(list, shallow);
        Assert.Equal(list, SharpPackSerializer.Deserialize<List<int>>(payload, shallow));
    }

    [Fact]
    public void PooledMemoryUsesTheSameDepthBoundaryForWritableAndReadOnlyViews()
    {
        var data = new[] { new ReadLimitCollectionItem { Value = 3 }, new ReadLimitCollectionItem { Value = 7 } };
        Memory<ReadLimitCollectionItem?> writable = data;
        ReadOnlyMemory<ReadLimitCollectionItem?> readOnly = data;

        var writablePayload = SharpPackSerializer.Serialize(writable);
        var readOnlyPayload = SharpPackSerializer.Serialize(readOnly);
        static SharpPackSerializerContext Pooled(int depth) => AtDepth(depth, builder => builder
            .Register(new MemoryPoolFormatter<ReadLimitCollectionItem>())
            .Register(new ReadOnlyMemoryPoolFormatter<ReadLimitCollectionItem>()));

        AssertDepthFailure(() => SharpPackSerializer.Deserialize(writablePayload, ref writable, Pooled(1)));
        AssertDepthFailure(() => SharpPackSerializer.Deserialize(readOnlyPayload, ref readOnly, Pooled(1)));
        Assert.Equal(2, writable.Length);
        Assert.Equal(2, readOnly.Length);
        Assert.Same(data[1], writable.Span[1]);
        Assert.Same(data[1], readOnly.Span[1]);

        using var writablePool = new PooledTestBuffer(SharpPackSerializer.Deserialize<Memory<ReadLimitCollectionItem?>>(writablePayload, Pooled(2)));
        using var readOnlyPool = new PooledTestBuffer(SharpPackSerializer.Deserialize<ReadOnlyMemory<ReadLimitCollectionItem?>>(readOnlyPayload, Pooled(2)));
        Assert.Equal(7, writablePool.Memory.Span[1]!.Value);
        Assert.Equal(7, readOnlyPool.Memory.Span[1]!.Value);
    }

    static T ReadAtBoundary<T>(T input, int depth = 2)
    {
        var payload = SharpPackSerializer.Serialize(input);
        var result = SharpPackSerializer.Deserialize<T>(payload, AtDepth(depth));
        Assert.NotNull(result);
        AssertDepthFailure(() => SharpPackSerializer.Deserialize<T>(payload, AtDepth(depth - 1)));
        return result!;
    }

    static void AssertDepthFailure(Action read)
    {
        var error = Assert.Throws<SharpPackSerializationException>(read);
        Assert.Equal(SharpPackErrorCode.DepthLimitExceeded, error.ErrorCode);
        Assert.Equal(SharpPackReadLimitKind.Depth, error.ReadLimitKind);
    }

    static SharpPackSerializerContext AtDepth(int depth, Action<SharpPackSerializerContextBuilder>? configure = null)
    {
        var builder = new SharpPackSerializerContextBuilder().Configure(SharpPackSerializerConfiguration.Default with
        {
            ReadLimits = new SharpPackReadLimits { MaxDepth = depth },
        });
        configure?.Invoke(builder);
        return builder.Build();
    }

    sealed class PooledTestBuffer(ReadOnlyMemory<ReadLimitCollectionItem?> memory) : IDisposable
    {
        public ReadOnlyMemory<ReadLimitCollectionItem?> Memory => memory;

        public void Dispose()
        {
            if (MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is not null)
                ArrayPool<ReadLimitCollectionItem?>.Shared.Return(segment.Array, clearArray: true);
        }
    }

    sealed class IdentityIntFormatter : SharpPackFormatter<int>
    {
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value)
            => writer.WriteUnmanaged(value);

        public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
            => reader.ReadUnmanaged(out value);
    }
}

[SharpPackable]
internal partial class ReadLimitCollectionItem
{
    public int Value { get; set; }
}
