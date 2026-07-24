using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class VTWrapper<T>
{
    public T? Versioned { get; set; }
    public int[]? Values { get; set; }
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant0
{
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant1
{
    [SharpPackOrder(0)]
    public int MyProperty1 { get; set; } = default;
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant2
{
    [SharpPackOrder(0)]
    public int MyProperty1 { get; set; } = default;

    [SharpPackOrder(1)]
    public long MyProperty2 { get; set; } = default;
}



[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant3
{
    [SharpPackOrder(0)]
    public int MyProperty1 { get; set; } = default;

    [SharpPackOrder(1)]
    public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant4
{
    [SharpPackOrder(0)]
    public int MyProperty1 { get; set; } = default;

    //[SharpPackOrder(1)]
    //public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerant5
{
    //[SharpPackOrder(0)]
    //public int MyProperty1 { get; set; } = default;

    //[SharpPackOrder(1)]
    //public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;

    [SharpPackOrder(5)]
    public ushort[] MyProperty6 { get; set; } = default!;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Version1
{
    [SharpPackOrder(0)]
    public int Id { get; set; }

    [SharpPackOrder(1)]
    public string Name { get; set; } = default!;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Version2
{
    [SharpPackOrder(0)]
    public int Id { get; set; }

    //deleted
    //[SharpPackOrder(1)]
    //public string Name { get; set; } = default!;

    [SharpPackOrder(2)]
    public string FirstName { get; set; } = default!;
    [SharpPackOrder(3)]
    public string LastName { get; set; } = default!;
}





[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersionTolerant1
{
    [SharpPackOrder(0)]
    public Version MyProperty1 { get; set; } = default!;
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersionTolerant2
{
    [SharpPackOrder(0)]
    public Version MyProperty1 { get; set; } = default!;

    [SharpPackOrder(1)]
    public long MyProperty2 { get; set; } = default;
}



[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersionTolerant3
{
    [SharpPackOrder(0)]
    public Version MyProperty1 { get; set; } = default!;

    [SharpPackOrder(1)]
    public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersionTolerant4
{
    [SharpPackOrder(0)]
    public Version MyProperty1 { get; set; } = default!;

    //[SharpPackOrder(1)]
    //public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersionTolerant5
{
    //[SharpPackOrder(0)]
    //public int MyProperty1 { get; set; } = default;

    //[SharpPackOrder(1)]
    //public long MyProperty2 { get; set; } = default;

    [SharpPackOrder(2)]
    public short MyProperty3 { get; set; } = default;

    [SharpPackOrder(5)]
    public Version MyProperty6 { get; set; } = default!;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersion1
{
    [SharpPackOrder(0)]
    public Version? Id { get; set; }

    [SharpPackOrder(1)]
    public string Name { get; set; } = default!;
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class MoreVersion2
{
    [SharpPackOrder(0)]
    public Version? Id { get; set; }

    //deleted
    //[SharpPackOrder(1)]
    //public string Name { get; set; } = default!;

    [SharpPackOrder(2)]
    public string FirstName { get; set; } = default!;
    [SharpPackOrder(3)]
    public string LastName { get; set; } = default!;
}
