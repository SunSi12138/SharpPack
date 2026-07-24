using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class NoCtor
{
    public int X { get; set; }
}

[SharpPackable]
public partial class OneCtor
{
    public int X { get; set; }


    public OneCtor()
    {

    }
}

[SharpPackable]
public partial class OneCtor2
{
    public int X { get; }
    public int Y { get; }

    public OneCtor2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}

//[SharpPackable]
//public partial class TwoCtor
//{
//    public TwoCtor()
//    {

//    }

//    public TwoCtor(int x, int y)
//    {

//    }
//}

[SharpPackable]
public partial class ExplicitlyCtor
{
    public int X { get; }
    public int Y { get; set; }

    public ExplicitlyCtor()
    {

    }

    [SharpPackConstructor]
    public ExplicitlyCtor(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }
}



//[SharpPackable]
//public partial class MultipleExplicitlyCtor
//{
//    [SharpPackConstructor]
//    public MultipleExplicitlyCtor()
//    {

//    }

//    [SharpPackConstructor]
//    public MultipleExplicitlyCtor(int x, int y)
//    {

//    }
//}


[SharpPackable]
public partial class ParameterCheck
{
    bool prop1SetCalled;

    string mp;
    public string MyProperty1
    {
        get { return mp; }
        set
        {
            mp = value;
            prop1SetCalled = true;
        }
    }
    public string? MyProperty2;

    public ParameterCheck(string myProperty1)
    {
        this.mp = myProperty1;
    }

    public bool IsProp1SetCalled()
    {
        return prop1SetCalled;
    }
}
