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
    public void ExactSizeFastPath_DoesNotFreezeRetainedBufferConfiguration()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(ExactSizeFastPath_DoesNotFreezeRetainedBufferConfiguration),
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

            configure.Invoke(null, [options]);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void SmallRetainedBuffer_DoesNotShrinkPooledSegmentFloor()
    {
        var writer = new global::SharpPack.Internal.ReusableLinkedArrayBufferWriter(
            useFirstBuffer: true,
            pinned: false,
            firstBufferSize: 64);

        _ = writer.GetSpan(64);
        writer.Advance(64);
        var pooledSegment = writer.GetSpan(1);

        pooledSegment.Length.Should().BeGreaterThanOrEqualTo(4 * 1024);
        writer.Reset();
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

    [Theory]
    [InlineData(1)]
    [InlineData(12_345)]
    [InlineData(128 * 1024)]
    public void CustomRetainedBufferSize_IsNotLimitedToPresets(int bufferSize)
    {
        var loadContext = new AssemblyLoadContext(
            $"{nameof(CustomRetainedBufferSize_IsNotLimitedToPresets)}-{bufferSize}",
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
                .SetValue(options, bufferSize);
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
            var state = constructor.Invoke([true]);

            GetFirstBuffer(state, stateType).Should().HaveCount(bufferSize);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void CustomRetainedBuffer_IsUsedAndReusedByByteArraySerialization()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(CustomRetainedBuffer_IsUsedAndReusedByByteArraySerialization),
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
                .SetValue(options, 3);
            serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [options]);

            var serialize = serializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Serialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(string));
            const string value =
                "a payload that must cross the three-byte retained buffer";
            var expectedPayload = SharpPackSerializer.Serialize(value);

            var firstPayload = (byte[])serialize.Invoke(null, [value])!;
            var stateType = serializerType.GetNestedType(
                "SerializerWriterThreadStaticState",
                BindingFlags.NonPublic)!;
            var state = serializerType.GetField(
                "threadStaticState",
                BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            var firstBuffer = GetFirstBuffer(state, stateType);
            var secondPayload = (byte[])serialize.Invoke(null, [value])!;
            var reusedBuffer = GetFirstBuffer(state, stateType);

            firstBuffer.Should().HaveCount(3);
            reusedBuffer.Should().BeSameAs(firstBuffer);
            firstPayload.Should().Equal(expectedPayload);
            secondPayload.Should().Equal(firstPayload);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ContextByteArrayPath_UsesAndFreezesRetainedBufferConfiguration()
    {
        var loadContext = new AssemblyLoadContext(
            nameof(ContextByteArrayPath_UsesAndFreezesRetainedBufferConfiguration),
            isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(
                typeof(SharpPackSerializer).Assembly.Location);
            var serializerType = assembly.GetType(
                "SharpPack.SharpPackSerializer",
                throwOnError: true)!;
            var contextType = assembly.GetType(
                "SharpPack.SharpPackSerializerContext",
                throwOnError: true)!;
            var optionsType = assembly.GetType(
                "SharpPack.SharpPackSerializerRuntimeOptions",
                throwOnError: true)!;
            var options = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("ThreadBufferSize")!
                .SetValue(options, 17);
            var configure = serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!;
            configure.Invoke(null, [options]);

            var context = Activator.CreateInstance(contextType)!;
            var serialize = serializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == "Serialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters() is
                    [_, { ParameterType: var parameterType }] &&
                    parameterType == contextType)
                .MakeGenericMethod(typeof(string));
            _ = serialize.Invoke(null,
                ["context retained buffer payload", context]);

            var stateType = serializerType.GetNestedType(
                "SerializerWriterThreadStaticState",
                BindingFlags.NonPublic)!;
            var state = serializerType.GetField(
                "threadStaticState",
                BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
            var contextBuffer = GetFirstBuffer(state, stateType);
            contextBuffer.Should().HaveCount(17);

            var defaultSerialize = serializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(static method =>
                    method.Name == "Serialize" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1)
                .MakeGenericMethod(typeof(string));
            _ = defaultSerialize.Invoke(null, ["default retained payload"]);
            GetFirstBuffer(state, stateType).Should().BeSameAs(contextBuffer);

            var secondConfigure = () => configure.Invoke(null, [options]);
            secondConfigure.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void InvalidCustomRetainedBufferSize_IsRejected(int bufferSize)
    {
        // Use an isolated load context so this test
        // cannot observe or freeze the process-wide test runner state.
        var loadContext = new AssemblyLoadContext(
            $"{nameof(InvalidCustomRetainedBufferSize_IsRejected)}-{bufferSize}",
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
            var isolatedOptions = Activator.CreateInstance(optionsType)!;
            optionsType.GetProperty("ThreadBufferSize")!
                .SetValue(isolatedOptions, bufferSize);
            var configure = serializerType.GetMethod(
                "ConfigureRuntime",
                BindingFlags.Public | BindingFlags.Static)!;

            var action = () => configure.Invoke(null, [isolatedOptions]);

            action.Should().Throw<TargetInvocationException>()
                .WithInnerException<ArgumentOutOfRangeException>();
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
