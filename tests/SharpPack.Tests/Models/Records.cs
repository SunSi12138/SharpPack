using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial struct UnmanagedStruct
{
    public int X;
    public int Y;
    public int Z;
}

[SharpPackable]
public partial struct IncludesReferenceStruct
{
    public int X;
    public string? Y;
}


[SharpPackable]
public partial class RequiredType
{
    public required int MyProperty1 { get; set; }
    public required string MyProperty2 { get; set; }
}

[SharpPackable]
public partial struct RequiredType2
{
    public required int MyProperty1 { get; set; }
    public required string MyProperty2 { get; set; }

    public void F()
    {
        // new MyRecord()
    }
}


[SharpPackable]
public partial struct StructWithConstructor1
{
    public string MyProperty { get; set; }

    public StructWithConstructor1(string myProperty)
    {
        this.MyProperty = myProperty;
    }
}

[SharpPackable]
public partial record MyRecord(int foo, int bar, string baz);

[SharpPackable]
public partial record struct StructRecordUnmanaged(int foo, int bar);


[SharpPackable]
public partial record struct StructRecordWithReference(int foo, string bar);
