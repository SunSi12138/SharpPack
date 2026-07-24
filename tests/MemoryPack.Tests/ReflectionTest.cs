using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

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
        m.Invoke(null, null).Should().BeAssignableTo<MemoryPackFormatter<ReflecCheck>>();

        var p = type.GetProperty("global::MemoryPack.IFixedSizeMemoryPackable.Size", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        p.Should().NotBeNull();
    }

    [Fact]
    public void PublicSurface_IsGenericOnlyAndHasNoLegacyRegistry()
    {
        var assembly = typeof(MemoryPackSerializer).Assembly;

        assembly.GetType("MemoryPack.MemoryPackFormatterProvider").Should().BeNull();
        assembly.GetType("MemoryPack.MemoryPackSerializerOptions").Should().BeNull();
        assembly.GetType("MemoryPack.DefaultMemoryPackSerializerContext").Should().BeNull();
        assembly.GetType("MemoryPack.IMemoryPackFormatter").Should().BeNull();
        assembly.GetType("MemoryPack.IMemoryPackFormatterRegister").Should().BeNull();
        assembly.GetType("MemoryPack.FormatterResolver`1")!
            .MakeGenericType(typeof(ReflecCheck))
            .GetMethod(
                "TryCreateExternalGeneratedFormatter",
                BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull();
        typeof(MemoryPackSerializerContext)
            .GetProperty(
                "Default",
                BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();

        typeof(MemoryPackSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method =>
                method.Name is "Serialize" or "SerializeAsync" or
                "Deserialize" or "DeserializeAsync")
            .Should().OnlyContain(static method => method.IsGenericMethodDefinition);
    }
}

[MemoryPackable]
public partial class ReflecCheck
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
}
