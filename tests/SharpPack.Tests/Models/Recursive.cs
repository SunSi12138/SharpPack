using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;

[SharpPackable]
public partial class Recursive
{
    public int MyProperty { get; set; }
    public Recursive? Rec { get; set; }
}
