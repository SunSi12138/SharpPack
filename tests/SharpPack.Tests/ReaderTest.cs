using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class ReaderTest
{
    [Fact]
    public void ReadFromSequence()
    {
        var seq = ReadOnlySequenceBuilder.Create(
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6, 7, 8 },
            new byte[] { 9, 10 });

        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        var reader = new SharpPackReader(seq, state);

        reader.GetRemainingSource(out var single, out var multi);
        single.Length.Should().Be(0);
        multi.ToArray().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        ref var spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(1, 2);
        reader.Advance(2);
        reader.GetRemainingSource(out single, out multi);
        single.Length.Should().Be(0);
        multi.ToArray().Should().Equal(3, 4, 5, 6, 7, 8, 9, 10);

        spanRef = ref reader.GetSpanReference(4);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 4).ToArray().Should().Equal(3, 4, 5, 6);
        reader.Advance(4);
        reader.GetRemainingSource(out single, out multi);
        single.Length.Should().Be(0);
        multi.ToArray().Should().Equal(7, 8, 9, 10);

        spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(7, 8);
        reader.Advance(2);
        reader.GetRemainingSource(out single, out multi);
        single.Length.Should().Be(2);
        single.ToArray().Should().Equal(9, 10);

        spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(9, 10);
        reader.Advance(2); // end
        reader.GetRemainingSource(out single, out multi);
        single.Length.Should().Be(0);
        multi.Length.Should().Be(0);

        bool error = false;
        try
        {
            reader.GetSpanReference(1);
        }
        catch (SharpPackSerializationException)
        {
            error = true;
        }
        error.Should().BeTrue();
    }

    [Fact]
    public void ReadFromSpan()
    {
        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        var reader = new SharpPackReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, state);

        ref var spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(1, 2);
        reader.Advance(2);

        spanRef = ref reader.GetSpanReference(4);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 4).ToArray().Should().Equal(3, 4, 5, 6);
        reader.Advance(4);

        spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(7, 8);
        reader.Advance(2);

        spanRef = ref reader.GetSpanReference(2);
        MemoryMarshal.CreateReadOnlySpan(ref spanRef, 2).ToArray().Should().Equal(9, 10);
        reader.Advance(2); // end

        bool error = false;
        try
        {
            reader.GetSpanReference(1);
        }
        catch (SharpPackSerializationException)
        {
            error = true;
        }
        error.Should().BeTrue();
    }

    [Fact]
    public void OverAdvance()
    {
        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        var reader = new SharpPackReader(new byte[] { 1, 2, 3 }, state);
        reader.Advance(2); // ok

        bool error = false;
        try
        {
            reader.Advance(10); // ng
        }
        catch (SharpPackSerializationException)
        {
            error = true;
        }
        error.Should().BeTrue();
    }

    [Fact]
    public void NegativeAdvanceAndSpanRequestAreRejectedWithoutMovingReader()
    {
        using var state = SharpPackReaderOptionalStatePool.Rent();
        var reader = new SharpPackReader(new byte[] { 1, 2, 3 }, state);

        var advanceError = false;
        try
        {
            reader.Advance(-1);
        }
        catch (SharpPackSerializationException)
        {
            advanceError = true;
        }

        var spanError = false;
        try
        {
            reader.GetSpanReference(-1);
        }
        catch (SharpPackSerializationException)
        {
            spanError = true;
        }

        advanceError.Should().BeTrue();
        spanError.Should().BeTrue();
        reader.Consumed.Should().Be(0);
        reader.ReadUnmanaged<byte>().Should().Be(1);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void InvalidCollectionLengthIsRejected(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);

        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<byte[]>(bytes));
        Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<byte[]>(
                bytes,
                new SharpPackSerializerContext()));
    }

    [Fact]
    public void ValidateInvalidLengthTest()
    {
        // int[] but invalid length
        // 2(len), 99, 9999
        var bytes = SharpPackSerializer.Serialize(new[] { 99, 9999 });

        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        var reader = new SharpPackReader(bytes, state);

        reader.TryReadCollectionHeader(out var len).Should().BeTrue();
        len.Should().Be(2);

        reader.ReadUnmanaged(out int v1, out int v2);
        v1.Should().Be(99);
        v2.Should().Be(9999);

        // inject invalid length
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1000); // 1000(len), 99, 9999
        using var state2 = SharpPackReaderOptionalStatePool.Rent(null);
        reader = new SharpPackReader(bytes, state2);

        try
        {
            reader.TryReadCollectionHeader(out var len2);
            Assert.Fail("should throw");
        }
        catch (SharpPackSerializationException) { }

        // just
        bytes = SharpPackSerializer.Serialize(new byte[] { 99 });
        using var state3 = SharpPackReaderOptionalStatePool.Rent(null);
        reader = new SharpPackReader(bytes, state3);
        reader.TryReadCollectionHeader(out var len3);
        len3.Should().Be(1);

        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        using var state4 = SharpPackReaderOptionalStatePool.Rent(null);
        reader = new SharpPackReader(bytes, state4);

        try
        {
            reader.TryReadCollectionHeader(out var len4);
            Assert.Fail("should throw");
        }
        catch (SharpPackSerializationException) { }
    }

    [Fact]
    public void PeekIsNull()
    {
        var bin = SharpPackSerializer.Serialize<string>(null);

        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        var reader = new SharpPackReader(bin, state);

        var isNull = reader.PeekIsNull();
        isNull.Should().BeTrue();
    }
}
