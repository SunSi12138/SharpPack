using MemoryPack.Tests.Models;
using System;
using System.Numerics;

namespace MemoryPack.Tests;

public class WireFormatCompatibilityTest
{
    static readonly MemoryPackSerializerContext Context = new();
    static readonly MemoryPackSerializerContext Utf16Context =
        new(MemoryPackSerializerConfiguration.Utf16);

    [Fact]
    public void PrimitivePayload_MatchesOriginalMemoryPack()
    {
        AssertPayload(0x01020304, "04030201");
    }

    [Fact]
    public void PrimitiveArrayPayload_MatchesOriginalMemoryPack()
    {
        AssertPayload<int[]>([1, -2], "0200000001000000FEFFFFFF");
    }

    [Fact]
    public void Utf8StringPayload_MatchesOriginalMemoryPack()
    {
        AssertPayload("A", "FEFFFFFF0100000041");
    }

    [Fact]
    public void Utf16StringPayload_MatchesOriginalMemoryPack()
    {
        var originalPayload = Convert.FromHexString("010000004100");

        MemoryPackSerializer.Serialize("A", Utf16Context)
            .Should().Equal(originalPayload);
        MemoryPackSerializer.Deserialize<string>(originalPayload)
            .Should().Be("A");
        MemoryPackSerializer.Deserialize<string>(originalPayload, Utf16Context)
            .Should().Be("A");
    }

    [Fact]
    public void GeneratedObjectPayload_MatchesOriginalMemoryPack()
    {
        AssertPayload(
            new StandardTypeOne { One = 0x01020304 },
            "0104030201",
            static value => value!.One.Should().Be(0x01020304));
    }

    [Fact]
    public void BigInteger_UsesTheOriginalLengthPrefixedByteContract()
    {
        var value = BigInteger.Parse("123456789012345678901234567890");
        var expectedBytes = value.ToByteArray();
        var expectedPayload = new byte[sizeof(int) + expectedBytes.Length];
        BitConverter.TryWriteBytes(expectedPayload, expectedBytes.Length);
        expectedBytes.CopyTo(expectedPayload, sizeof(int));

        MemoryPackSerializer.Serialize(value).Should().Equal(expectedPayload);
        MemoryPackSerializer.Serialize(value, Context).Should().Equal(expectedPayload);
        MemoryPackSerializer.Deserialize<BigInteger>(expectedPayload).Should().Be(value);
        MemoryPackSerializer.Deserialize<BigInteger>(expectedPayload, Context).Should().Be(value);
    }

    [Fact]
    public void GeneratedUnionPayload_MatchesOriginalMemoryPack()
    {
        IUnionInterface value = new Impl1
        {
            MyProperty = 0x01020304,
            Foo = 0x0102030405060708,
        };
        var originalPayload = Convert.FromHexString(
            "0002040302010807060504030201");

        MemoryPackSerializer.Serialize(value).Should().Equal(originalPayload);
        MemoryPackSerializer.Serialize(value, Context).Should().Equal(originalPayload);

        foreach (var restored in new[]
        {
            MemoryPackSerializer.Deserialize<IUnionInterface>(originalPayload),
            MemoryPackSerializer.Deserialize<IUnionInterface>(originalPayload, Context),
        })
        {
            var item = restored.Should().BeOfType<Impl1>().Subject;
            item.MyProperty.Should().Be(0x01020304);
            item.Foo.Should().Be(0x0102030405060708);
        }
    }

    static void AssertPayload<T>(
        T value,
        string originalHex,
        Action<T?>? assert = null)
    {
        var originalPayload = Convert.FromHexString(originalHex);

        MemoryPackSerializer.Serialize(value).Should().Equal(originalPayload);
        MemoryPackSerializer.Serialize(value, Context).Should().Equal(originalPayload);

        var defaultValue = MemoryPackSerializer.Deserialize<T>(originalPayload);
        var contextValue = MemoryPackSerializer.Deserialize<T>(originalPayload, Context);

        if (assert is null)
        {
            defaultValue.Should().BeEquivalentTo(value);
            contextValue.Should().BeEquivalentTo(value);
        }
        else
        {
            assert(defaultValue);
            assert(contextValue);
        }
    }
}
