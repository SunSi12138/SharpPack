using System;

namespace SharpPack.Tests;

public class LegacyTypeCompatibilityTest
{
    [Theory]
    [InlineData("MemoryPack.MemoryPackSerializer, MemoryPack.Core")]
    [InlineData("System.String, System.Private.CoreLib")]
    public void LegacyClrNamesAreRejectedWithoutFallback(string legacyTypeName)
    {
        var payload = SharpPackSerializer.Serialize(legacyTypeName);
        var context = new SharpPackSerializerContextBuilder().EnableTypeSerialization()
            .RegisterType<string>("string").RegisterType<SharpPackSerializerContext>("context").Build();
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload));
        Assert.Throws<SharpPackSerializationException>(() => SharpPackSerializer.Deserialize<Type>(payload, context));
    }
}
