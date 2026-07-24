using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class PrimitiveTest
{
    [Fact]
    public void ArrayWriterInt()
    {
        var buffer = new ArrayBufferWriter<byte>(1024);

        SharpPackSerializer.Serialize(ref buffer, 123);

        buffer.WrittenCount.Should().Be(4);

        var i = SharpPackSerializer.Deserialize<int>(buffer.WrittenSpan);
        i.Should().Be(123);
    }

    [Fact]
    public void NonGenericInt()
    {
        var bin = SharpPackSerializer.Serialize(123);
        var i = SharpPackSerializer.Deserialize<int>(bin);
        i.Should().Be(123);

        var j = SharpPackSerializer.Deserialize<int>(bin);
        j.Should().Be(123);
    }
}
