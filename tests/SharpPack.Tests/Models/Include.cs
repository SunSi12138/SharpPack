#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable CS0169

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class Include
{
    public int PublicProp { get; set; }
    public int PublicField;

    [SharpPackIgnore]
    public string? NoInclude { get; set; }

    public string? PrivateSet { get; private set; }
    public string? PrivateGet { private get; set; }

    [SharpPackInclude]
    private string? PrivateProp { get; set; }
    [SharpPackInclude]
    private int PrivateField;

    public void SetAll(int publicProp, int publicFIeld, string privateSet, string privateGet, string privateProp, int privateField)
    {
        this.PublicProp = publicProp;
        this.PublicField = publicFIeld;
        this.PrivateSet = privateSet;
        this.PrivateGet = privateGet;
        this.PrivateProp = privateProp;
        this.PrivateField = privateField;
    }

    public (int, int, string?, string?, string?, int) GetAll()
    {
        return (PublicProp, PublicField, PrivateSet, PrivateGet, PrivateProp, PrivateField);
    }
}

[SharpPackable]
public partial class NoInclude
{
    public int PublicProp { get; set; }
    public int PublicField;

    public string? PrivateSet { get; private set; }
    public string? PrivateGet { private get; set; }

    private string? PrivateProp { get; set; }
    private int PrivateField;

    public void SetAll(int publicProp, int publicFIeld, string privateSet, string privateGet, string privateProp, int privateField)
    {
        this.PublicProp = publicProp;
        this.PublicField = publicFIeld;
        this.PrivateSet = privateSet;
        this.PrivateGet = privateGet;
        this.PrivateProp = privateProp;
        this.PrivateField = privateField;
    }

    public (int, int, string?, string?, string?, int) GetAll()
    {
        return (PublicProp, PublicField, PrivateSet, PrivateGet, PrivateProp, PrivateField);
    }
}
