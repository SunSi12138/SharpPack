using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;


[SharpPackable]
public partial struct Hoge
{
    public string MyProperty { get; set; }
}

[SharpPackable]
public partial class MethodCall
{
    public static List<string> Log { get; } = new List<string>();

    int mp;
    public int MyProperty
    {
        get
        {
            Log.Add("Get");
            return mp;
        }
        set
        {
            Log.Add("Set");
            mp = value;
        }
    }

    public MethodCall()
    {
        Log.Add("Constructor");
    }

    [SharpPackOnSerializing]
    public static void OnSerializing1()
    {
        Log.Add(nameof(OnSerializing1));
    }

    // check allow private.
    [SharpPackOnSerializing]
    void OnSerializing2()
    {
        Log.Add(nameof(OnSerializing2));
    }


    [SharpPackOnSerialized]
    static void OnSerialized1()
    {
        Log.Add(nameof(OnSerialized1));
    }

    [SharpPackOnSerialized]
    public void OnSerialized2()
    {
        Log.Add(nameof(OnSerialized2));
    }

    [SharpPackOnDeserializing]
    public static void OnDeserializing1()
    {
        Log.Add(nameof(OnDeserializing1));
    }

    [SharpPackOnDeserializing]
    public void OnDeserializing2()
    {
        Log.Add(nameof(OnDeserializing2));
    }

    [SharpPackOnDeserialized]
    public static void OnDeserialized1()
    {
        Log.Add(nameof(OnDeserialized1));
    }

    [SharpPackOnDeserialized]
    public void OnDeserialized2()
    {
        Log.Add(nameof(OnDeserialized2));
    }

    // allow more



    [SharpPackOnSerializing]
    public static void OnSerializing_M1<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, ref MethodCall? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        Log.Add(nameof(OnSerializing_M1));
    }

    [SharpPackOnSerializing]
    public void OnSerializing_M2<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, ref MethodCall? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        Log.Add(nameof(OnSerializing_M2));
    }

    [SharpPackOnSerialized]
    public static void OnSerialized_M1<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, ref MethodCall? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        Log.Add(nameof(OnSerialized_M1));
    }


    [SharpPackOnSerialized]
    public void OnSerialized_M2<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, ref MethodCall? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        Log.Add(nameof(OnSerialized_M2));
    }



    [SharpPackOnDeserializing]
    public static void OnDeserializing_M1(ref SharpPackReader reader, ref MethodCall? value)
    {
        Log.Add(nameof(OnDeserializing_M1));
    }

    [SharpPackOnDeserializing]
    public void OnDeserializing_M2(ref SharpPackReader reader, ref MethodCall? value)
    {
        Log.Add(nameof(OnDeserializing_M2));
    }

    [SharpPackOnDeserialized]
    public static void OnDeserialized_M1(ref SharpPackReader reader, ref MethodCall? value)
    {
        Log.Add(nameof(OnDeserialized_M1));
    }

    [SharpPackOnDeserialized]
    public void OnDeserialized_M2(ref SharpPackReader reader, ref MethodCall? value)
    {
        Log.Add(nameof(OnDeserialized_M2));
    }


    // not allow parameter exists.

    //[SharpPackOnSerialized]
    //public void InvalidMethodThatHasParameter(int x)
    //{
    //}
}


// unmanaged type can't add attributes.
//[SharpPackable]
//public partial struct UnmanagedStructMethod
//{
//    public int X;

//    [SharpPackOnSerialized]
//    public void Foo()
//    {
//    }
//}
