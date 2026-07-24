using MemoryPack.Tests.Models;
using System;
using System.Collections.Generic;

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
