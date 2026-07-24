using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class Versioning0
{
}

[SharpPackable]
public partial class Versioning1
{
    public int MyProperty1 { get; set; }
}

[SharpPackable]
public partial class Versioning2
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
}

[SharpPackable]
public partial class Versioning3
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
    public int MyProperty3 { get; set; }
}

[SharpPackable]
public partial class Versioning4
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
    public int MyProperty3 { get; set; }
    public int MyProperty4 { get; set; }
}

[SharpPackable]
public partial class WrappedVersioning2
{
    public string? Before { get; set; }
    public Versioning2? V2 { get; set; }
    public string? After { get; set; }
}


[SharpPackable]
public partial class WrappedVersioning4
{
    public string? Before { get; set; }
    public Versioning4? V4 { get; set; }
    public string? After { get; set; }
}
