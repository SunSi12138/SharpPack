using System;
using System.Buffers;
using System.Threading.Tasks;
using SharpPack.Formatters;

namespace SharpPack.Tests;

public class TypeRegistryTest
{
    static SharpPackSerializerContext RegisteredContext()
        => new SharpPackSerializerContextBuilder()
            .EnableTypeSerialization()
            .RegisterType<TypeRegistryTest>("test")
            .Build();

    [Fact]
    public void DefaultAndEmptyContextsRejectTypeIncludingNull()
    {
        SharpPackSerializerConfiguration.Default.TypeSerializationMode.Should().Be(TypeSerializationMode.Disabled);
        foreach (var value in new Type?[] { null, typeof(TypeRegistryTest) })
        {
            Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(value));
            Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(value, new SharpPackSerializerContext()));
        }
        foreach (var payload in new[] { SharpPackSerializer.Serialize("test"), SharpPackSerializer.Serialize<string>(null) })
        {
            Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload));
            Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload, new SharpPackSerializerContext()));
        }
    }

    [Theory]
    [InlineData(SharpPackStringEncoding.Utf8)]
    [InlineData(SharpPackStringEncoding.Utf16)]
    public void RegisteredTypeUsesOnlyStableIdAndNullRoundTrips(SharpPackStringEncoding encoding)
    {
        var context = new SharpPackSerializerContextBuilder()
            .Configure(new SharpPackSerializerConfiguration { StringEncoding = encoding })
            .EnableTypeSerialization()
            .RegisterType(typeof(TypeRegistryTest), "test")
            .Build();
        var payload = SharpPackSerializer.Serialize<Type>(typeof(TypeRegistryTest), context);
        payload.Should().Equal(SharpPackSerializer.Serialize("test", context));
        SharpPackSerializer.Deserialize<Type>(payload, context).Should().Be(typeof(TypeRegistryTest));
        var nullPayload = SharpPackSerializer.Serialize<Type>(null, context);
        SharpPackSerializer.Deserialize<Type>(nullPayload, context).Should().BeNull();
    }

    [Fact]
    public void UnknownTypesAndIdsDoNotExpandRegistryOrPoisonNextOperation()
    {
        var context = RegisteredContext();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(string), context));
        foreach (var id in new[] { "unknown", "TEST", typeof(string).AssemblyQualifiedName! })
        {
            var payload = SharpPackSerializer.Serialize(id);
            Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload, context));
        }
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(string), context));
        var known = SharpPackSerializer.Serialize<Type>(typeof(TypeRegistryTest), context);
        SharpPackSerializer.Deserialize<Type>(known, context).Should().Be(typeof(TypeRegistryTest));
    }

    [Fact]
    public void FormatterAndRootUsageDoNotGrantTypePayloadAuthority()
    {
        var context = new SharpPackSerializerContextBuilder()
            .EnableTypeSerialization()
            .Register(new OffsetFormatter(0))
            .Build();
        _ = SharpPackSerializer.Serialize(new ContextCustomValue(), context);
        _ = context.GetFormatter<TypeRegistryEnvelope>();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(ContextCustomValue), context));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(TypeRegistryEnvelope), context));
        var payload = SharpPackSerializer.Serialize(typeof(ContextCustomValue).AssemblyQualifiedName);
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload, context));
    }

    [Fact]
    public void NestedTypePayloadUsesExplicitRegistryWithoutAuthorizingItsRoot()
    {
        var context = RegisteredContext();
        var value = new TypeRegistryEnvelope { Value = typeof(TypeRegistryTest) };
        var payload = SharpPackSerializer.Serialize(value, context);
        SharpPackSerializer.Deserialize<TypeRegistryEnvelope>(payload, context)!.Value
            .Should().Be(typeof(TypeRegistryTest));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize(value));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<TypeRegistryEnvelope>(payload));
        var emptyRegistry = new SharpPackSerializerContextBuilder().EnableTypeSerialization().Build();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<TypeRegistryEnvelope>(payload, emptyRegistry));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(TypeRegistryEnvelope), context));
    }

    [Fact]
    public void RegistrationIsSeparateFromEnablingAndConfigureOrderIsExplicit()
    {
        var builder = new SharpPackSerializerContextBuilder().RegisterType<string>("string");
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(string), builder.Build()));
        var disabled = new SharpPackSerializerContextBuilder().EnableTypeSerialization()
            .RegisterType<string>("string").Configure(SharpPackSerializerConfiguration.Default).Build();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type>(typeof(string), disabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SharpPackSerializerContext(
            new SharpPackSerializerConfiguration { TypeSerializationMode = (TypeSerializationMode)255 }));
    }

    [Fact]
    public void DuplicateIdsAndTypesFailWithoutPartiallyRegistering()
    {
        var builder = new SharpPackSerializerContextBuilder().EnableTypeSerialization().RegisterType<string>("string");
        Assert.Throws<ArgumentException>(() => builder.RegisterType<int>("string"));
        Assert.Throws<ArgumentException>(() => builder.RegisterType<string>("alias"));
        Assert.Throws<ArgumentException>(() => builder.RegisterType<string>("string"));
        builder.RegisterType<int>("int");
        var context = builder.Build();
        SharpPackSerializer.Deserialize<Type>(SharpPackSerializer.Serialize("int"), context).Should().Be(typeof(int));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(SharpPackSerializer.Serialize("alias"), context));
        Assert.Throws<InvalidOperationException>(() => builder.RegisterType<Guid>("guid"));
        Assert.Throws<InvalidOperationException>(() => builder.EnableTypeSerialization());
    }

    [Fact]
    public void RegistrationArgumentsAreValidated()
    {
        var builder = new SharpPackSerializerContextBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.RegisterType(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => builder.RegisterType<int>(null!));
        Assert.Throws<ArgumentException>(() => builder.RegisterType<int>(""));
        Assert.Throws<ArgumentException>(() => builder.RegisterType<int>(" "));
    }

    [Fact]
    public void UnknownTypeDoesNotWriteToCallerBuffer()
    {
        var buffer = new ArrayBufferWriter<byte>();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Serialize<Type, ArrayBufferWriter<byte>>(
            ref buffer, typeof(string), RegisteredContext()));
        buffer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void ImmutableRegistryCanBeSharedByConcurrentOperations()
    {
        var context = RegisteredContext();
        Parallel.For(0, 100, _ =>
        {
            var bytes = SharpPackSerializer.Serialize<Type>(typeof(TypeRegistryTest), context);
            SharpPackSerializer.Deserialize<Type>(bytes, context).Should().Be(typeof(TypeRegistryTest));
        });
    }

    [Fact]
    public void NonTypePayloadBytesRemainUnchanged()
    {
        var value = new ContextGraph { Name = "registry", Items = [], List = [], Map = [], Pair = (42, "wire"), Optional = 7 };
        var context = RegisteredContext();
        SharpPackSerializer.Serialize(value, context).Should().Equal(SharpPackSerializer.Serialize(value));
        SharpPackSerializer.Deserialize<ContextGraph>(SharpPackSerializer.Serialize(value), context).Should().BeEquivalentTo(value);
    }
}

[SharpPackable]
public partial class TypeRegistryEnvelope
{
    public Type? Value { get; set; }
}
