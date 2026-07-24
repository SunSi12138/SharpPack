using System;
using MemoryPack.ExternalUnionFormatters;
using MemoryPack.ExternalUnionModels;

namespace MemoryPack.Tests;

public class ExternalUnionContextRegistrationTest
{
    [Fact]
    public void ExternalUnion_InDifferentAssembly_UsesGeneratedRegistration()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .RegisterExternalUnionFormatter()
            .Build();
        IExternalUnion value = new ExternalUnionA { Value = 1234 };

        var payload = MemoryPackSerializer.Serialize(value, context);
        var decoded = MemoryPackSerializer.Deserialize<IExternalUnion>(
            payload,
            context);

        Convert.ToHexString(payload).Should().Be("0B01D2040000");
        decoded.Should().BeOfType<ExternalUnionA>()
            .Which.Value.Should().Be(1234);
    }

    [Fact]
    public void ExternalGenericUnion_UsesClosedGeneratedRegistration()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .RegisterExternalGenericUnionFormatter<string?>()
            .Build();
        IExternalGenericUnion<string?> value =
            new ExternalGenericUnionB<string?> { Value = "external" };

        var payload = MemoryPackSerializer.Serialize(value, context);
        var decoded = MemoryPackSerializer.Deserialize<
            IExternalGenericUnion<string?>>(payload, context);

        decoded.Should().BeOfType<ExternalGenericUnionB<string?>>()
            .Which.Value.Should().Be("external");
    }
}
