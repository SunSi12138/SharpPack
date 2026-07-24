#pragma warning disable CS0649

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class MemberKindsAllUnmanaged
{
    public int A; // public field
    [SharpPackInclude]
    private int B; // private field
    public readonly int C; // public readonly field
    [SharpPackInclude]
    private readonly int D; // readonly field

    public int E { get; set; } // public property
    public int F { private get; set; } // private get
    public int G { get; private set; } // private set
    [SharpPackInclude]
    private int H { get; set; } // private property

    int i;
    public int I1 => i; // get only
    public int I2 { set { i = value; } } // set only

    int j;
    [SharpPackInclude]
    private int J1 => j; // private get only
    [SharpPackInclude]
    private int J2 { set { j = value; } } // private set only

    int[] kArray = new int[1];

    public ref int K
    {
        get { return ref kArray[0]; }
    }

    public void SetH(int h)
    {
        this.H = h;
    }

    public int GetH()
    {
        return H;
    }

    public MemberKindsAllUnmanaged(int c, int d)
    {
        this.C = c;
        this.D = d;
    }
}
