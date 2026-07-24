using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
//using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class MethodRefTest
{
    [Fact]
    public void WriteId()
    {
        var data = new EmitIdData { MyProperty = 9999 };
        var bin = SharpPackSerializer.Serialize(data);

        EmitIdData.privateData = Guid.Empty;
        var v2 = SharpPackSerializer.Deserialize<EmitIdData>(bin);
        v2!.MyProperty.Should().Be(data.MyProperty);

        EmitIdData.privateData.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ReadOther()
    {
        var data = new EmitFromOther();
        data.Set(9999);

        var reference = new EmitFromOther();
        EmitFromOther.other = reference;

        var bin = SharpPackSerializer.Serialize(data);


        var v2 = SharpPackSerializer.Deserialize<EmitFromOther>(bin);
        v2!.MyProperty.Should().Be(data.MyProperty);

        v2!.Should().BeSameAs(reference);
    }
}

[SharpPackable]
public partial class EmitIdData
{
    public int MyProperty { get; set; }

    public static Guid privateData;

    [SharpPackOnSerializing]
    static void WriteId<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, ref EmitIdData? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        writer.WriteUnmanaged(Guid.NewGuid()); // emit GUID in header.
    }

    [SharpPackOnDeserializing]
    static void ReadId(ref SharpPackReader reader, ref EmitIdData? value)
    {
        // read custom header before deserialize
        var guid = reader.ReadUnmanaged<Guid>();
        Console.WriteLine(guid);
        privateData = guid;
    }
}


[SharpPackable]
public partial class EmitFromOther
{
    public static EmitFromOther other = null!;

    public int MyProperty { get; private set; }

    public void Set(int v)
    {
        MyProperty = v;
    }

    [SharpPackOnDeserializing]
    static void ReadId(ref SharpPackReader reader, ref EmitFromOther? value)
    {
        value = other!;
    }
}
