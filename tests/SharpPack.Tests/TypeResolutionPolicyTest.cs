using System;

namespace SharpPack.Tests;

public class TypeResolutionPolicyTest
{
    static SharpPackSerializerConfiguration CatalogOnlyConfiguration =>
        SharpPackSerializerConfiguration.Default with
        {
            TypeResolutionMode = TypeResolutionMode.ContextCatalogOnly,
        };

    static SharpPackSerializerConfiguration DisabledConfiguration =>
        SharpPackSerializerConfiguration.Default with
        {
            TypeResolutionMode = TypeResolutionMode.Disabled,
        };

    [Fact]
    public void DefaultMode_PreservesCurrentTypeRoundTrip()
    {
        var payload = SharpPackSerializer.Serialize<Type>(
            typeof(TypeResolutionPolicyTest));
        var context = new SharpPackSerializerContext();

        context.Configuration.TypeResolutionMode
            .Should().Be(TypeResolutionMode.GlobalCompatibility);
        SharpPackSerializer.Deserialize<Type>(payload, context)
            .Should().Be(typeof(TypeResolutionPolicyTest));
    }

    [Fact]
    public void ContextCatalogOnly_DoesNotGlobalFallback()
    {
        var payload = SharpPackSerializer.Serialize<Type>(
            typeof(TypeResolutionPolicyTest));
        var context = new SharpPackSerializerContext(CatalogOnlyConfiguration);

        var deserialize = () =>
            SharpPackSerializer.Deserialize<Type>(payload, context);

        deserialize.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void ContextCatalogOnly_SerializingTypeDoesNotExpandCatalog()
    {
        var context = new SharpPackSerializerContext(CatalogOnlyConfiguration);
        var payload = SharpPackSerializer.Serialize<Type>(
            typeof(TypeResolutionPolicyTest),
            context);

        var deserialize = () =>
            SharpPackSerializer.Deserialize<Type>(payload, context);

        deserialize.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void ContextCatalogOnly_ResolvesRegisteredType()
    {
        var payload = SharpPackSerializer.Serialize<Type>(
            typeof(ContextCustomValue));
        var context = new SharpPackSerializerContextBuilder()
            .Configure(CatalogOnlyConfiguration)
            .Register(new OffsetFormatter(0))
            .Build();

        SharpPackSerializer.Deserialize<Type>(payload, context)
            .Should().Be(typeof(ContextCustomValue));
    }

    [Fact]
    public void ContextCatalogOnly_ResolvesRootAssemblyType()
    {
        var payload = SharpPackSerializer.Serialize(
            new TypeResolutionEnvelope
            {
                RuntimeType = typeof(TypeResolutionPolicyTest),
            });
        var context = new SharpPackSerializerContext(CatalogOnlyConfiguration);

        var restored = SharpPackSerializer.Deserialize<TypeResolutionEnvelope>(
            payload,
            context);

        restored!.RuntimeType.Should().Be(typeof(TypeResolutionPolicyTest));
    }

    [Fact]
    public void Disabled_RejectsTypePayload()
    {
        var context = new SharpPackSerializerContext(DisabledConfiguration);
        var payload = SharpPackSerializer.Serialize<Type>(
            typeof(TypeResolutionPolicyTest));

        var serialize = () =>
            SharpPackSerializer.Serialize<Type>(
                typeof(TypeResolutionPolicyTest),
                context);
        var deserialize = () =>
            SharpPackSerializer.Deserialize<Type>(payload, context);

        serialize.Should().Throw<SharpPackSerializationException>();
        deserialize.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void ContextCatalogOnly_PreservesNonTypeWireBytes()
    {
        var value = new ContextGraph
        {
            Name = "strict",
            Items = [],
            List = [],
            Map = [],
            Pair = (42, "wire"),
            Optional = 7,
        };
        var context = new SharpPackSerializerContext(CatalogOnlyConfiguration);

        var strictPayload = SharpPackSerializer.Serialize(value, context);
        var defaultPayload = SharpPackSerializer.Serialize(value);

        strictPayload.Should().Equal(defaultPayload);
        SharpPackSerializer.Deserialize<ContextGraph>(strictPayload, context)
            .Should().BeEquivalentTo(value);
    }
}

[SharpPackable]
public partial class TypeResolutionEnvelope
{
    public Type? RuntimeType { get; set; }
}
