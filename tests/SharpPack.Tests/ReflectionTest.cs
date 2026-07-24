using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class ReflectionTest
{
    [Fact]
    public void InvokeExplicitInterface()
    {
        var type = typeof(ReflecCheck);

        var m = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(".CreateFormatter", StringComparison.Ordinal));
        m.Should().NotBeNull();
        m.Invoke(null, null).Should().BeAssignableTo<SharpPackFormatter<ReflecCheck>>();

        var p = type.GetProperty("global::SharpPack.IFixedSizeSharpPackable.Size", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        p.Should().NotBeNull();
    }

    [Fact]
    public void PublicSurface_IsGenericOnlyAndHasNoLegacyRegistry()
    {
        var assembly = typeof(SharpPackSerializer).Assembly;

        assembly.GetType("SharpPack.SharpPackFormatterProvider").Should().BeNull();
        assembly.GetType("SharpPack.SharpPackSerializerOptions").Should().BeNull();
        assembly.GetType("SharpPack.DefaultSharpPackSerializerContext").Should().BeNull();
        assembly.GetType("SharpPack.ISharpPackFormatter").Should().BeNull();
        assembly.GetType("SharpPack.ISharpPackFormatterRegister").Should().BeNull();
        assembly.GetType("SharpPack.FormatterResolver`1")!
            .MakeGenericType(typeof(ReflecCheck))
            .GetMethod(
                "TryCreateExternalGeneratedFormatter",
                BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull();
        typeof(SharpPackSerializerContext)
            .GetProperty(
                "Default",
                BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();

        typeof(SharpPackSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method =>
                method.Name is "Serialize" or "SerializeAsync" or
                "Deserialize" or "DeserializeAsync")
            .Should().OnlyContain(static method => method.IsGenericMethodDefinition);
    }
}

[SharpPackable]
public partial class ReflecCheck
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
}
