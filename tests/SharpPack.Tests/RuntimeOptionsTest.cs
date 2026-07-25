using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace SharpPack.Tests;

public class RuntimeOptionsTest
{
    [Fact]
    public void Presets_ExposeExpectedRetainedBufferTradeoffs()
    {
        SharpPackSerializerRuntimeOptions.Default.ThreadBufferSize
            .Should().Be(8 * 1024);
        SharpPackSerializerRuntimeOptions.Default.PinThreadBuffer
            .Should().BeFalse();
        SharpPackSerializerRuntimeOptions.HighThroughput.ThreadBufferSize
            .Should().Be(80 * 1024);
        SharpPackSerializerRuntimeOptions.HighThroughput.PinThreadBuffer
            .Should().BeFalse();
    }

    [Fact]
    public void RuntimeConfiguration_IsAppliedOnceAndThenFrozen()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(RuntimeConfiguration_IsAppliedOnceAndThenFrozen),
            isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(
                typeof(SharpPackSerializer).Assembly.Location);
            var serializerType = assembly.GetType(
                "SharpPack.SharpPackSerializer",
                throwOnError: true)!;
            var optionsType = assembly.GetType(
                "SharpPack.SharpPackSerializerRuntimeOptions",
                throwOnError: true)!;
            var options = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("ThreadBufferSize")!
                .SetValue(options, 80 * 1024);

            var configure = serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!;
            configure.Invoke(null, [options]);

            var serialize = serializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Serialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(string));
            serialize.Invoke(null, ["runtime-options"]);

            var state = serializerType.GetField(
                "threadStaticState",
                BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            var bufferWriter = state.GetType().GetField(
                "BufferWriter",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(state)!;
            var firstBuffer = (byte[])bufferWriter.GetType().GetMethod(
                "DangerousGetFirstBuffer",
                BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(bufferWriter, null)!;
            firstBuffer.Length.Should().Be(80 * 1024);

            var secondConfigure = () => configure.Invoke(null, [options]);
            secondConfigure.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
