using System;

namespace SharpPack.Tests;

public class LegacyTypeCompatibilityTest
{
    [Fact]
    public void RenamedLibraryTypePayloadResolvesToSharpPackType()
    {
        const string legacyTypeName =
            "MemoryPack.MemoryPackSerializer, MemoryPack.Core";
        var originalPayload = SharpPackSerializer.Serialize(legacyTypeName);

        SharpPackSerializer.Deserialize<Type>(originalPayload)
            .Should().Be(typeof(SharpPackSerializer));

        var context = new SharpPackSerializerContext();
        SharpPackSerializer.Deserialize<Type>(originalPayload, context)
            .Should().Be(typeof(SharpPackSerializer));
    }
}
