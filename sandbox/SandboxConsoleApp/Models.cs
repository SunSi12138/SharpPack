using SharpPack;
using SharpPack.Formatters;
using SharpPack.Internal;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SandboxConsoleApp;

public interface IMore
{
    public Version Description { get; set; }
}

public class NewBase
{
    public long Description { get; set; }
}

[SharpPackable]
public partial struct FooUnman
{
    public float MyProperty { get; set; }
    public float MyProperty2 { get; set; }
}

[SharpPackable]
public partial class NewProp : NewBase, IMore
{
    Version IMore.Description { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public new string? Description { get; set; }

    public NewProp()
    {

    }
}


[SharpPackable]
public partial class NotNotOmu
{
    public Guid? GUIDNULLABLE { get; set; }
}


[SharpPackable]
public partial class Mop
{
    public NoGen? MyProperty { get; set; }
    public LisList? MyLisList { get; set; }
    public List<Suage>? SuageMan { get; set; }
}


[SharpPackable]
public partial class NotSample
{
    [Utf8StringFormatter]
    public string? Custom1 { get; set; }

}

[SharpPackable(GenerateType.CircularReference)]
public partial class Node
{
    [SharpPackOrder(0)]
    public Node? Parent { get; set; }
    [SharpPackOrder(1)]
    public Node[]? Children { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class TakoyakiY
{
    [SharpPackOrder(1)]
    public string? Bar { get; set; }
    [SharpPackOrder(10)]
    public int Foo { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class Suage
{
    [SharpPackOrder(0)]
    public int Prop1 { get; set; }
    [SharpPackOrder(2)]
    public int Prop2 { get; set; }

    //public Suage(int prop1, int prop2)
    //{
    //    this.Prop1 = prop1;
    //    this.Prop2 = prop2;
    //}
}



[SharpPackable(GenerateType.NoGenerate)]
public partial class NoGen
{
}

[SharpPackable(GenerateType.Collection)]
public partial class LisList : List<int>
{

}
