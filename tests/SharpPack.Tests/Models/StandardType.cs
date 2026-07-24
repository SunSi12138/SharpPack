using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// namespaced
namespace SharpPack.Tests.Models
{
    // SHARPPACK001 must be partial
    // [SharpPackable]
    //public class Ng
    //{

    //}

    [SharpPackable]
    public partial class StandardTypeZero
    {
    }

    [SharpPackable]
    public partial class StandardTypeOne
    {
        public int One { get; set; }
    }


    [SharpPackable]
    public partial class StandardTypeTwo
    {
        public int One { get; set; }
        public int Two { get; set; }

        public StandardTypeTwo()
        {
            // _ = new StandardTypeTwoFormatter();
        }

        // SHARPPACK002 nested is not allowed
        //[SharpPackable]
        //public partial class Nested
        //{
        //    public int One { get; set; }
        //}
    }

    [SharpPackable]
    public partial struct StandardUnmanagedStruct
    {
        public int MyProperty { get; set; }
    }

    [SharpPackable]
    public partial struct StandardStruct
    {
        public string MyProperty { get; set; }

        public StandardStruct()
        {
            MyProperty = default!;
        }
    }

    public partial class NestedContainer
    {
        [SharpPackable]
        public partial class StandardTypeNested
        {
            public int One { get; set; }
        }
    }

    public partial class DoublyNestedContainer
    {
        public partial class DoublyNestedContainerInner
        {
            [SharpPackable]
            public partial class StandardTypeDoublyNested
            {
                public int One { get; set; }
            }
        }
    }


    [SharpPackable]
    public partial class WithArray
    {
        public StandardTypeOne[]? One { get; set; }
    }

}

// another namespace, same type name
namespace SharpPack.Tests.Models.More
{

    [SharpPackable]
    public partial class StandardTypeTwo
    {
        public string? One { get; set; }
        public string? Two { get; set; }

        public StandardTypeTwo()
        {
            // new StandardTypeTwoFormatter();
        }
    }
}

[SharpPackable]
public partial class GlobalNamespaceType
{
    public int MyProperty { get; set; }

    public GlobalNamespaceType()
    {
        // _ = new GlobalNamespaceTypeFormatter();
    }
}
