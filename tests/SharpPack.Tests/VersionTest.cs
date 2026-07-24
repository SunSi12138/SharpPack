using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;


public class VersionTest
{
    [Fact]
    public void V()
    {
        {
            var v = new Version();
            var bin = SharpPackSerializer.Serialize(v);
            var v2 = SharpPackSerializer.Deserialize<Version>(bin);
            v2.Should().Be(v);
        }
        {
            var v = new Version(10, 20);
            var bin = SharpPackSerializer.Serialize(v);
            var v2 = SharpPackSerializer.Deserialize<Version>(bin);
            v2.Should().Be(v);
        }
        {
            var v = new Version(10, 20, 30);
            var bin = SharpPackSerializer.Serialize(v);
            var v2 = SharpPackSerializer.Deserialize<Version>(bin);
            v2.Should().Be(v);
        }
        {
            var v = new Version(10, 20, 30, 40);
            var bin = SharpPackSerializer.Serialize(v);
            var v2 = SharpPackSerializer.Deserialize<Version>(bin);
            v2.Should().Be(v);
        }
    }
}
