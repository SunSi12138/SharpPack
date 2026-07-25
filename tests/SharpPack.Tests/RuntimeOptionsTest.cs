using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

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
            optionsType.GetProperty("PinThreadBuffer")!
                .SetValue(options, true);

            var configure = serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!;
            configure.Invoke(null, [options]);

            var stateType = serializerType.GetNestedType(
                "SerializerWriterThreadStaticState",
                BindingFlags.NonPublic)!;
            var stateConstructor = stateType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                [typeof(bool)],
                modifiers: null)!;
            var temporaryState = stateConstructor.Invoke([false]);
            GetFirstBuffer(temporaryState, stateType).Should().BeEmpty();

            // Creating a non-retained reentrant state must not freeze the
            // configured state used by subsequent long-lived thread states.
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
            var firstBuffer = GetFirstBuffer(state, state.GetType());
            firstBuffer.Length.Should().Be(80 * 1024);
            GC.GetGeneration(firstBuffer).Should().Be(2);

            var secondConfigure = () => configure.Invoke(null, [options]);
            secondConfigure.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ReentrantState_DoesNotRetainAFirstBuffer()
    {
        var serializerType = typeof(SharpPackSerializer);
        var stateType = serializerType.GetNestedType(
            "SerializerWriterThreadStaticState",
            BindingFlags.NonPublic)!;
        var constructor = stateType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            [typeof(bool)],
            modifiers: null)!;

        var temporaryState = constructor.Invoke([false]);
        GetFirstBuffer(temporaryState, stateType).Should().BeEmpty();
    }

    [Fact]
    public void ExactSizeFirstUse_FreezesRuntimeConfiguration()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(ExactSizeFirstUse_FreezesRuntimeConfiguration),
            isCollectible: true);
        try
        {
            var coreAssembly = loadContext.LoadFromAssemblyPath(
                typeof(SharpPackSerializer).Assembly.Location);
            var testAssembly = loadContext.LoadFromAssemblyPath(
                typeof(ExactSizeSerializationTest).Assembly.Location);
            var serializerType = coreAssembly.GetType(
                "SharpPack.SharpPackSerializer",
                throwOnError: true)!;
            var modelType = testAssembly.GetType(
                "SharpPack.Tests.ExactSizeModel",
                throwOnError: true)!;
            var model = Activator.CreateInstance(modelType)!;
            modelType.GetProperty("Text")!.SetValue(model, "freeze");

            serializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Serialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1)
                .MakeGenericMethod(modelType)
                .Invoke(null, [model]);

            var optionsType = coreAssembly.GetType(
                "SharpPack.SharpPackSerializerRuntimeOptions",
                throwOnError: true)!;
            var options = Activator.CreateInstance(optionsType)!;
            var configure = serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!;

            var secondConfigure = () => configure.Invoke(null, [options]);
            secondConfigure.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public async Task ConcurrentFirstUse_UsesOneFrozenConfiguration()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(ConcurrentFirstUse_UsesOneFrozenConfiguration),
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
                .SetValue(options, 12_345);
            serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [options]);

            var stateType = serializerType.GetNestedType(
                "SerializerWriterThreadStaticState",
                BindingFlags.NonPublic)!;
            var constructor = stateType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                [typeof(bool)],
                modifiers: null)!;

            var lengths = await Task.WhenAll(
                Enumerable.Range(0, Environment.ProcessorCount * 4)
                    .Select(_ => Task.Run(() =>
                    {
                        var state = constructor.Invoke([true]);
                        return GetFirstBuffer(state, stateType).Length;
                    })));

            lengths.Should().OnlyContain(static length => length == 12_345);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    static byte[] GetFirstBuffer(object state, Type stateType)
    {
        var bufferWriter = stateType.GetField(
            "BufferWriter",
            BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(state)!;
        return (byte[])bufferWriter.GetType().GetMethod(
            "DangerousGetFirstBuffer",
            BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(bufferWriter, null)!;
    }
}
