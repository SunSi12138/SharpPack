using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable(GenerateType.CircularReference)]
public partial class Node
{
    [SharpPackOrder(0)]
    public Node? Parent { get; set; }
    [SharpPackOrder(1)]
    public Node[]? Children { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class PureNode
{
    [SharpPackOrder(0)]
    public int Id { get; set; }
    [SharpPackOrder(1)]
    public ulong Id2 { get; set; }
}

[SharpPackable]
public partial class CircularHolder
{
    public List<Node>? List { get; set; }
    public List<PureNode>? ListPure { get; set; }
}


// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/preserve-references?pivots=dotnet-7-0
[SharpPackable(GenerateType.CircularReference)]
public partial class Employee
{
    [SharpPackOrder(0)]
    public string? Name { get; set; }
    [SharpPackOrder(1)]
    public Employee? Manager { get; set; }
    [SharpPackOrder(2)]
    public List<Employee>? DirectReports { get; set; }
}



[SharpPackable(GenerateType.CircularReference, SerializeLayout.Sequential)]
public partial class SequentialCircularReference
{
    public string? Name { get; set; }
    public SequentialCircularReference? Manager { get; set; }
    public List<SequentialCircularReference>? DirectReports { get; set; }
}
