using MemoryPack.Formatters;
using MemoryPack.Tests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

public class UnionTest
{
    [Fact]
    public void Foo()
    {
        {
            var one = new AForOne { BaseValue = 10, MyProperty = 99 };
            var two = new AForTwo { BaseValue = 99, MyProperty = 10000 };

            var bin1 = MemoryPackSerializer.Serialize((IForExternalUnion)one);
            var bin2 = MemoryPackSerializer.Serialize((IForExternalUnion)two);

            var one2 = MemoryPackSerializer.Deserialize<IForExternalUnion>(bin1);
            var two2 = MemoryPackSerializer.Deserialize<IForExternalUnion>(bin2);

            one2.Should().BeAssignableTo<AForOne>().Subject.Should().BeEquivalentTo(one);
            two2.Should().BeAssignableTo<AForTwo>().Subject.Should().BeEquivalentTo(two);
        }
        {
            var one = new BForOne<DateTime> { NoValue = DateTime.Now, MyProperty = 99 };
            var two = new BForTwo<string> { NoValue = "aaaa", MyProperty = 10000 };

            var bin1 = MemoryPackSerializer.Serialize((IGenericsUnion<DateTime>)one);
            var bin2 = MemoryPackSerializer.Serialize((IGenericsUnion<string>)two);

            var one2 = MemoryPackSerializer.Deserialize<IGenericsUnion<DateTime>>(bin1);
            var two2 = MemoryPackSerializer.Deserialize<IGenericsUnion<string>>(bin2);

            one2.Should().BeAssignableTo<BForOne<DateTime>>().Subject.Should().BeEquivalentTo(one);
            two2.Should().BeAssignableTo<BForTwo<string>>().Subject.Should().BeEquivalentTo(two);
        }
    }

    [Fact]
    public void DynamicUnion_IsGenericContextOwnedAndWireCompatible()
    {
        var formatter = new DynamicUnionFormatterBuilder<IDynamicBase>()
            .Add<Gen1>(7)
            .Add<Gen2>(42)
            .Build();
        var context = new MemoryPackSerializerContextBuilder()
            .Register(formatter)
            .Build();

        IDynamicBase first = new Gen1 { MyProperty = 5678 };
        IDynamicBase second = new Gen2 { MyProperty = "dynamic" };

        var firstPayload = MemoryPackSerializer.Serialize(first, context);
        var secondPayload = MemoryPackSerializer.Serialize(second, context);

        Convert.ToHexString(firstPayload).Should().Be("07012E160000");
        Convert.ToHexString(secondPayload).Should()
            .Be("2A01F8FFFFFF0700000064796E616D6963");
        MemoryPackSerializer.Deserialize<IDynamicBase>(firstPayload, context)
            .Should().BeOfType<Gen1>().Which.MyProperty.Should().Be(5678);
        MemoryPackSerializer.Deserialize<IDynamicBase>(secondPayload, context)
            .Should().BeOfType<Gen2>().Which.MyProperty.Should().Be("dynamic");
    }

    [Fact]
    public void DynamicUnion_RejectsDuplicateTypesAndTags()
    {
        var duplicateType = new DynamicUnionFormatterBuilder<IDynamicBase>()
            .Add<Gen1>(1);
        Assert.Throws<ArgumentException>(() => duplicateType.Add<Gen1>(2));

        var duplicateTag = new DynamicUnionFormatterBuilder<IDynamicBase>()
            .Add<Gen1>(1);
        Assert.Throws<ArgumentException>(() => duplicateTag.Add<Gen2>(1));
    }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IDynamicBase
{
}

[MemoryPackable]
public partial class Gen1 : IDynamicBase
{
    public int MyProperty { get; set; }
}

[MemoryPackable]
public partial class Gen2 : IDynamicBase
{
    public string? MyProperty { get; set; }
}
