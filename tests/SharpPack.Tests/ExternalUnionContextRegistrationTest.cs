using System;
using SharpPack.ExternalUnionFormatters;
using SharpPack.ExternalUnionModels;

namespace SharpPack.Tests;

public class ExternalUnionContextRegistrationTest
{
    [Fact]
    public void ExternalUnion_InDifferentAssembly_UsesGeneratedRegistration()
    {
        var context = new SharpPackSerializerContextBuilder()
            .RegisterExternalUnionFormatter()
            .Build();
        IExternalUnion value = new ExternalUnionA { Value = 1234 };

        var payload = SharpPackSerializer.Serialize(value, context);
        var decoded = SharpPackSerializer.Deserialize<IExternalUnion>(
            payload,
            context);

        Convert.ToHexString(payload).Should().Be("0B01D2040000");
        decoded.Should().BeOfType<ExternalUnionA>()
            .Which.Value.Should().Be(1234);
    }

    [Fact]
    public void ExternalGenericUnion_UsesClosedGeneratedRegistration()
    {
        var context = new SharpPackSerializerContextBuilder()
            .RegisterExternalGenericUnionFormatter<string?>()
            .Build();
        IExternalGenericUnion<string?> value =
            new ExternalGenericUnionB<string?> { Value = "external" };

        var payload = SharpPackSerializer.Serialize(value, context);
        var decoded = SharpPackSerializer.Deserialize<
            IExternalGenericUnion<string?>>(payload, context);

        decoded.Should().BeOfType<ExternalGenericUnionB<string?>>()
            .Which.Value.Should().Be("external");
    }
}
