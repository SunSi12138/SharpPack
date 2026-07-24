using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class EnumTest
{
    private T Convert<T>(T value)
    {
        return SharpPackSerializer.Deserialize<T>(SharpPackSerializer.Serialize(value))!;
    }

    [Fact]
    public void EnumTes()
    {
        Convert(BEnum.B).Should().Be(BEnum.B);
        Convert(NormalEnum.A).Should().Be(NormalEnum.A);
        Convert(NotNotEnum.C).Should().Be(NotNotEnum.C);
    }

    public enum BEnum : byte
    {
        A, B, C
    }
    public enum NormalEnum
    {
        A, B, C
    }

    public enum NotNotEnum : long
    {
        A, B, C
    }
}
