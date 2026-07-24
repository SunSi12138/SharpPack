using SharpPack;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Samples;


[SharpPackable]
public partial class Sample2
{
    [SharpPackAllowSerialize]
    public NotSerializableType? NotSerializableProperty { get; set; }


}

public class NotSerializableType
{

}


[SharpPackable]
public partial class Person
{
    public readonly int Age;
    public readonly string Name;

    // You can use parametarized constructor
    public Person(int age, string name)
    {
        this.Age = age;
        this.Name = name;
    }
}

// also supports record primary constructor
[SharpPackable]
public partial record Person2(int Age, string Name);

public partial class Person3
{
    public int Age { get; set; }
    public string Name { get; set; }

    public Person3()
    {
        this.Age = 0;
        this.Name = "";
    }

    // If exists multiple constructors, must use [SharpPackConstructor]
    [SharpPackConstructor]
    public Person3(int age, string name)
    {
        this.Age = age;
        this.Name = name;
    }
}


[SharpPackable(GenerateType.Collection)]
public partial class MyList<T> : List<T>
{
}

[SharpPackable(GenerateType.Collection)]
public partial class MyStringDictionary<TValue> : Dictionary<string, TValue>
{

}

// Annotate inheritance types
[SharpPackable]
[SharpPackUnion(0, typeof(FooClass))]
[SharpPackUnion(249, typeof(BarClass))]
// [SharpPackUnion(250, typeof(BarClass), useWideTag: true)]
public partial interface IUnionSample
{
}

[SharpPackable]
public partial class FooClass : IUnionSample
{
    public int XYZ { get; set; }
}

[SharpPackable]
public partial class BarClass : IUnionSample
{
    public string? OPQ { get; set; }
}




public class Skelton : SharpPackFormatter<Skelton>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Skelton? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        // use writer method.
    }

    public override void Deserialize(ref SharpPackReader reader, scoped ref Skelton? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        // use reader method.
    }
}



[SharpPackable]
public partial class Version1
{
    public int Prop1 { get; set; }
    public long Prop2 { get; set; }
}

// Add is OK.
[SharpPackable]
public partial class Version2
{
    public int Prop1 { get; set; }
    public long Prop2 { get; set; }
    public int? AddedProp { get; set; }
}


[SharpPackable(SerializeLayout.Explicit)]
public partial class SampleExplicitOrder
{
    [SharpPackOrder(1)]
    public int Prop1 { get; set; }
    [SharpPackOrder(0)]
    public int Prop0 { get; set; }
}

[SharpPackable]
public partial class MyDictContainer
{
    public Dictionary<int, string>? MD { get; set; }


}


[SharpPackable]
public partial class PoolModelSample : IDisposable
{
    public int Id { get; }

    [MemoryPoolFormatter<byte>]
    public Memory<byte> Payload { get; private set; }

    public PoolModelSample(int id, Memory<byte> payload)
    {
        Id = id;
        Payload = payload;
    }

    bool usePool;

    [SharpPackOnDeserialized]
    void OnDeserialized()
    {
        usePool = true;
    }

    public void Dispose()
    {
        if (!usePool) return;

        Return(Payload); Payload = default;
    }

    static void Return<T>(Memory<T> memory) => Return((ReadOnlyMemory<T>)memory);

    static void Return<T>(ReadOnlyMemory<T> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is { Length: > 0 })
        {
            ArrayPool<T>.Shared.Return(segment.Array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
