using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPack.Tests;

public class StringTest
{
    [Fact]
    public void Utf16()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf16);
        var bin = SharpPackSerializer.Serialize(text, context);
        var newText = SharpPackSerializer.Deserialize<string>(bin, context);

        text.Should().Be(newText);
    }

    [Fact]
    public void Utf8()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf8);
        var bin = SharpPackSerializer.Serialize(text, context);
        var newText = SharpPackSerializer.Deserialize<string>(bin, context);

        text.Should().Be(newText);
    }

    [Fact]
    public void MalformedUtf8()
    {
        var text = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほわをん";

        var context = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf8);
        var bin = SharpPackSerializer.Serialize(text, context);

        ref var head = ref MemoryMarshal.GetArrayDataReference(bin);

        // (int ~utf8-byte-count, int utf16-length, utf8-bytes)
        // change utf16-length

        Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, 4), 9999);

        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<string>(bin, context));
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

        foreach (var context in new SharpPackSerializerContext?[]
                 {
                     null,
                     new SharpPackSerializerContext()
                 })
        {
            Action deserializeOverflow = context is null
                ? () => SharpPackSerializer.Deserialize<string>(overflow)
                : () => SharpPackSerializer.Deserialize<string>(overflow, context);
            Action deserializeMismatch = context is null
                ? () => SharpPackSerializer.Deserialize<string>(mismatch)
                : () => SharpPackSerializer.Deserialize<string>(mismatch, context);

            deserializeOverflow.Should().Throw<SharpPackSerializationException>();
            deserializeMismatch.Should().Throw<SharpPackSerializationException>();
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

        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<string>(payload));
    }

    [Fact]
    public void Intern()
    {
        var bin = SharpPackSerializer.Serialize(Guid.NewGuid().ToString());

        var str1 = SharpPackSerializer.Deserialize<string>(bin);
        var str2 = SharpPackSerializer.Deserialize<string>(bin);

        str1.Should().Be(str2);
        object.ReferenceEquals(str1, str2).Should().BeFalse();

        var value = new InternStringTest { Foo = Guid.NewGuid().ToString() };

        var bin2 = SharpPackSerializer.Serialize(value);

        var v1 = SharpPackSerializer.Deserialize<InternStringTest>(bin2)!;
        var v2 = SharpPackSerializer.Deserialize<InternStringTest>(bin2)!;

        v1.Foo.Should().Be(v2.Foo);
        object.ReferenceEquals(v1.Foo, v2.Foo).Should().BeTrue();

        string.IsInterned(v1.Foo!).Should().NotBeNull();
    }
}


[SharpPackable]
public partial class InternStringTest
{
    [InternStringFormatter]
    public string? Foo { get; set; }
}
