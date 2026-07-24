using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryPack.Tests;

public class StringTest
{
    [Fact]
    public void Utf16()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf16);
        var bin = MemoryPackSerializer.Serialize(text, context);
        var newText = MemoryPackSerializer.Deserialize<string>(bin, context);

        text.Should().Be(newText);
    }

    [Fact]
    public void Utf8()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf8);
        var bin = MemoryPackSerializer.Serialize(text, context);
        var newText = MemoryPackSerializer.Deserialize<string>(bin, context);

        text.Should().Be(newText);
    }

    [Fact]
    public void MalformedUtf8()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf8);
        var bin = MemoryPackSerializer.Serialize(text, context);

        ref var head = ref MemoryMarshal.GetArrayDataReference(bin);

        // (int ~utf8-byte-count, int utf16-length, utf8-bytes)
        // change utf16-length

        Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, 4), 9999);

        Assert.Throws<MemoryPackSerializationException>(
            () => MemoryPackSerializer.Deserialize<string>(bin, context));
    }

    [Fact]
    public void Utf8HeaderOverflowAndDecodedLengthMismatchAreRejected()
    {
        var overflow = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(overflow, int.MinValue);

        var mismatch = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(mismatch, ~1);
        BinaryPrimitives.WriteInt32LittleEndian(mismatch.AsSpan(4), 2);
        mismatch[8] = (byte)'A';

        foreach (var context in new MemoryPackSerializerContext?[]
                 {
                     null,
                     new MemoryPackSerializerContext()
                 })
        {
            Action deserializeOverflow = context is null
                ? () => MemoryPackSerializer.Deserialize<string>(overflow)
                : () => MemoryPackSerializer.Deserialize<string>(overflow, context);
            Action deserializeMismatch = context is null
                ? () => MemoryPackSerializer.Deserialize<string>(mismatch)
                : () => MemoryPackSerializer.Deserialize<string>(mismatch, context);

            deserializeOverflow.Should().Throw<MemoryPackSerializationException>();
            deserializeMismatch.Should().Throw<MemoryPackSerializationException>();
        }
    }

    [Fact]
    public void UnknownUtf16LengthStillRequiresStrictUtf8()
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(payload, ~2);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), -1);
        payload[8] = 0xC3;
        payload[9] = 0x28;

        Assert.Throws<MemoryPackSerializationException>(
            () => MemoryPackSerializer.Deserialize<string>(payload));
    }

    [Fact]
    public void Intern()
    {
        var bin = MemoryPackSerializer.Serialize(Guid.NewGuid().ToString());

        var str1 = MemoryPackSerializer.Deserialize<string>(bin);
        var str2 = MemoryPackSerializer.Deserialize<string>(bin);

        str1.Should().Be(str2);
        object.ReferenceEquals(str1, str2).Should().BeFalse();

        var value = new InternStringTest { Foo = Guid.NewGuid().ToString() };

        var bin2 = MemoryPackSerializer.Serialize(value);

        var v1 = MemoryPackSerializer.Deserialize<InternStringTest>(bin2)!;
        var v2 = MemoryPackSerializer.Deserialize<InternStringTest>(bin2)!;

        v1.Foo.Should().Be(v2.Foo);
        object.ReferenceEquals(v1.Foo, v2.Foo).Should().BeTrue();

        string.IsInterned(v1.Foo!).Should().NotBeNull();
    }
}


[MemoryPackable]
public partial class InternStringTest
{
    [InternStringFormatter]
    public string? Foo { get; set; }
}
