using SharpPack;
using MessagePack;
using Orleans;
using ProtoBuf;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Benchmark.Models;

/// <summary>
/// My serialize class.
/// </summary>
[MessagePackObject]
[ProtoContract]
[SharpPackable]
public partial class MyClass
{
    [Key(0)]
    [ProtoBuf.ProtoMember(1)]
    [Id(0)]
    public int X { get; set; }
    [Key(1)]
    [ProtoBuf.ProtoMember(2)]
    [Id(1)]
    public int Y { get; set; }
    [Key(2)]
    [ProtoBuf.ProtoMember(3)]
    [Id(2)]
    public int Z { get; set; }
    [Key(3)]
    [ProtoBuf.ProtoMember(4)]
    [Id(3)]
    public string? FirstName { get; set; }
    [Key(4)]
    [ProtoBuf.ProtoMember(5)]
    [Id(4)]
    public string? LastName { get; set; }
}


[SharpPackable(GenerateType.VersionTolerant)]
public partial class VersionTolerantMyClass
{
    [SharpPackOrder(0)]
    public int X { get; set; }
    [SharpPackOrder(1)]
    public int Y { get; set; }
    [SharpPackOrder(2)]
    public int Z { get; set; }
    [SharpPackOrder(3)]
    public string? FirstName { get; set; }
    [SharpPackOrder(4)]
    public string? LastName { get; set; }
}
