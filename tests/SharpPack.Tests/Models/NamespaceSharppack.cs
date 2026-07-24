using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.SharpPack
{

    [SharpPackable]
    public partial class MemModel1
    {
        public int MyProperty { get; set; }
    }

}

namespace SharpPack.Tests.SharpPack
{

    [SharpPackable]
    public partial class MemModel2
    {
        public int MyProperty { get; set; }
    }

}


namespace SharpPack.Tests.Models.SharpPack
{

    [SharpPackable]
    public partial class MemModel3
    {
        public int MyProperty { get; set; }
    }

}
