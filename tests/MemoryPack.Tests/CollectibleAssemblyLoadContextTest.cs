using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace MemoryPack.Tests;

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IHostPluginUnion
{
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CollectibleAssemblyLoadContextCollection
{
    public const string Name = "MemoryPack collectible AssemblyLoadContext";
}

[Collection(CollectibleAssemblyLoadContextCollection.Name)]
public class CollectibleAssemblyLoadContextTest
{
    const string PluginSource = """
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using MemoryPack.Formatters;
using MemoryPack.Tests;

namespace CollectiblePlugin;

[MemoryPackable]
public partial class PluginItem
{
    public string? Name { get; set; }
}

[MemoryPackable]
public partial class PluginDto
{
    public int Id { get; set; }
    public Type? RuntimeType { get; set; }
    public PluginItem? Child { get; set; }
    public PluginItem?[]? Children { get; set; }
    public List<PluginItem?>? ChildList { get; set; }
    public IReadOnlyList<PluginItem?>? ReadOnlyChildren { get; set; }
    public Dictionary<string, PluginItem?>? ChildMap { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(0, typeof(PluginUnionValue))]
public partial interface IPluginUnion
{
}

[MemoryPackable]
public partial class PluginUnionValue : IPluginUnion
{
    public int Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IExternalPluginUnion
{
}

[MemoryPackUnionFormatter(typeof(IExternalPluginUnion))]
[MemoryPackUnion(0, typeof(ExternalPluginUnionValue))]
public partial class ExternalPluginUnionFormatter
{
}

[MemoryPackable]
public partial class ExternalPluginUnionValue : IExternalPluginUnion
{
    public int Value { get; set; }
}

public sealed class HostPluginUnionFormatter : MemoryPackFormatter<IHostPluginUnion>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref IHostPluginUnion? value)
    {
        if (value is not HostPluginUnionValue item)
        {
            writer.WriteNullUnionHeader();
            return;
        }

        writer.WriteUnionHeader(0);
        writer.WritePackable(item);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref IHostPluginUnion? value)
    {
        if (!reader.TryReadUnionHeader(out var tag))
        {
            value = null;
            return;
        }

        value = tag == 0
            ? reader.ReadPackable<HostPluginUnionValue>()
            : throw new InvalidOperationException();
    }
}

[MemoryPackable]
public partial class HostPluginUnionValue : IHostPluginUnion
{
    public int Value { get; set; }
}

public sealed class PlusOneFormatter : MemoryPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref int value)
    {
        writer.WriteUnmanaged(value + 1);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref int value)
    {
        reader.ReadUnmanaged(out int encoded);
        value = encoded - 1;
    }
}

public sealed class PlusOneAttribute : MemoryPackCustomFormatterAttribute<PlusOneFormatter, int>
{
    public override PlusOneFormatter GetFormatter() => new();
}

[MemoryPackable]
public partial class PluginCustomDto
{
    [PlusOne]
    public int Value { get; set; }
}

public sealed class UnsupportedPluginType
{
    public int Value { get; set; }
}

public sealed class UnsupportedPluginFormatter : MemoryPackFormatter<UnsupportedPluginType>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref UnsupportedPluginType? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }
        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(value.Value);
    }

    public override void Deserialize(ref MemoryPackReader reader, scoped ref UnsupportedPluginType? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }
        reader.ReadUnmanaged(out int item);
        value = new UnsupportedPluginType { Value = item };
    }
}

public sealed class ThrowingPluginFormatter : MemoryPackFormatter<UnsupportedPluginType>
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref UnsupportedPluginType? value)
        => throw new InvalidOperationException("expected formatter failure");

    public override void Deserialize(ref MemoryPackReader reader, scoped ref UnsupportedPluginType? value)
        => throw new InvalidOperationException("expected formatter failure");
}

public static class PluginEntry
{
    public static bool RunAll()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register<IHostPluginUnion>(new HostPluginUnionFormatter())
            .Register(new UnsupportedPluginFormatter())
            .Build();
        var value = new PluginDto
        {
            Id = 42,
            RuntimeType = typeof(PluginItem),
            Child = new PluginItem { Name = "nested" },
            Children = new[] { new PluginItem { Name = "array" } },
            ChildList = new List<PluginItem?> { new PluginItem { Name = "list" } },
            ReadOnlyChildren = new List<PluginItem?> { new PluginItem { Name = "read-only-list" } },
            ChildMap = new Dictionary<string, PluginItem?>
            {
                ["key"] = new PluginItem { Name = "dictionary" }
            }
        };

        var bytes = MemoryPackSerializer.Serialize(value, context);
        var result = MemoryPackSerializer.Deserialize<PluginDto>(bytes, context);
        if (result?.Id != 42 ||
            result.RuntimeType != typeof(PluginItem) ||
            result.Child?.Name != "nested" ||
            result.Children?[0]?.Name != "array" ||
            result.ChildList?[0]?.Name != "list" ||
            result.ReadOnlyChildren?[0]?.Name != "read-only-list" ||
            result.ChildMap?["key"]?.Name != "dictionary")
        {
            return false;
        }

        var array = new[] { new PluginDto { Id = 1 } };
        var arrayBytes = MemoryPackSerializer.Serialize(array, context);
        if (MemoryPackSerializer.Deserialize<PluginDto[]>(arrayBytes, context)?[0].Id != 1)
        {
            return false;
        }

        var list = new List<PluginDto> { new() { Id = 2 } };
        var listBytes = MemoryPackSerializer.Serialize(list, context);
        if (MemoryPackSerializer.Deserialize<List<PluginDto>>(listBytes, context)?[0].Id != 2)
        {
            return false;
        }

        var map = new Dictionary<string, PluginDto> { ["dto"] = new() { Id = 3 } };
        var mapBytes = MemoryPackSerializer.Serialize(map, context);
        if (MemoryPackSerializer.Deserialize<Dictionary<string, PluginDto>>(mapBytes, context)?["dto"].Id != 3)
        {
            return false;
        }

        IPluginUnion union = new PluginUnionValue { Value = 4 };
        var unionBytes = MemoryPackSerializer.Serialize<IPluginUnion>(union, context);
        if (MemoryPackSerializer.Deserialize<IPluginUnion>(unionBytes, context) is not PluginUnionValue { Value: 4 })
        {
            return false;
        }

        IExternalPluginUnion externalUnion = new ExternalPluginUnionValue { Value = 40 };
        var externalUnionBytes = MemoryPackSerializer.Serialize(externalUnion, context);
        if (MemoryPackSerializer.Deserialize<IExternalPluginUnion>(externalUnionBytes, context)
            is not ExternalPluginUnionValue { Value: 40 })
        {
            return false;
        }

        IHostPluginUnion hostUnion = new HostPluginUnionValue { Value = 41 };
        var hostUnionBytes = MemoryPackSerializer.Serialize(hostUnion, context);
        if (MemoryPackSerializer.Deserialize<IHostPluginUnion>(hostUnionBytes, context)
            is not HostPluginUnionValue { Value: 41 })
        {
            return false;
        }

        var customBytes = MemoryPackSerializer.Serialize(new PluginCustomDto { Value = 5 }, context);
        if (MemoryPackSerializer.Deserialize<PluginCustomDto>(customBytes, context)?.Value != 5)
        {
            return false;
        }

        var registeredBytes = MemoryPackSerializer.Serialize(new UnsupportedPluginType { Value = 6 }, context);
        if (MemoryPackSerializer.Deserialize<UnsupportedPluginType>(registeredBytes, context)?.Value != 6)
        {
            return false;
        }

        var bufferWriter = new ArrayBufferWriter<byte>();
        MemoryPackSerializer.Serialize(ref bufferWriter, value, context);
        var sequence = new ReadOnlySequence<byte>(bufferWriter.WrittenMemory);
        if (MemoryPackSerializer.Deserialize<PluginDto>(sequence, context)?.Id != 42)
        {
            return false;
        }

        using var stream = new MemoryStream();
        MemoryPackSerializer.SerializeAsync(stream, value, context).AsTask().GetAwaiter().GetResult();
        stream.Position = 0;
        return MemoryPackSerializer.DeserializeAsync<PluginDto>(stream, context).AsTask().GetAwaiter().GetResult()?.Id == 42;
    }

    public static bool RunFailedLookup()
    {
        var context = new MemoryPackSerializerContext();
        try
        {
            _ = MemoryPackSerializer.Serialize(new UnsupportedPluginType(), context);
            return false;
        }
        catch (MemoryPackSerializationException)
        {
            return true;
        }
    }

    public static bool RunFormatterFailure()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register<UnsupportedPluginType>(new ThrowingPluginFormatter())
            .Build();
        try
        {
            _ = MemoryPackSerializer.Serialize(new UnsupportedPluginType(), context);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message == "expected formatter failure";
        }
    }
}
""";

    [Fact]
    public void ExplicitContextGraph_UnloadsCollectibleAssembly()
    {
        var references = LoadInvokeAndUnload(CompilePlugin(), "single", "RunAll");

        ForceUnload(references);

        references.LoadContext.IsAlive.Should().BeFalse();
        references.Assembly.IsAlive.Should().BeFalse();
        references.PluginType.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void SameFullNameInTwoLoadContexts_IsIsolatedAndUnloadsIndependently()
    {
        var image = CompilePlugin();
        var first = LoadedPlugin.Load(image, "same-name-one");
        var second = LoadedPlugin.Load(image, "same-name-two");

        first.InvokeBoolean("RunAll").Should().BeTrue();
        second.InvokeBoolean("RunAll").Should().BeTrue();
        GlobalFormatterCachesExist().Should().BeFalse();

        var firstReferences = first.Unload();
        first = null!;
        ForceUnload(firstReferences);
        firstReferences.LoadContext.IsAlive.Should().BeFalse();
        firstReferences.Assembly.IsAlive.Should().BeFalse();
        firstReferences.PluginType.IsAlive.Should().BeFalse();

        second.InvokeBoolean("RunAll").Should().BeTrue();
        var secondReferences = second.Unload();
        second = null!;
        ForceUnload(secondReferences);
        secondReferences.LoadContext.IsAlive.Should().BeFalse();
        secondReferences.Assembly.IsAlive.Should().BeFalse();
        secondReferences.PluginType.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void FailedFormatterLookup_DoesNotRootCollectibleAssembly()
    {
        var references = LoadInvokeAndUnload(CompilePlugin(), "failed-lookup", "RunFailedLookup");

        ForceUnload(references);

        references.LoadContext.IsAlive.Should().BeFalse();
        references.Assembly.IsAlive.Should().BeFalse();
        references.PluginType.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void FormatterException_DoesNotLeaveThreadStaticContextRoot()
    {
        var references = LoadInvokeAndUnload(CompilePlugin(), "formatter-failure", "RunFormatterFailure");

        ForceUnload(references);

        references.LoadContext.IsAlive.Should().BeFalse();
        references.Assembly.IsAlive.Should().BeFalse();
        references.PluginType.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void EmptyContextTracksRuntimeTypeInsideNonCollectibleHost()
    {
        var references = RoundTripHostRuntimeTypeAndUnload(CompilePlugin());

        ForceUnload(references);

        references.LoadContext.IsAlive.Should().BeFalse();
        references.Assembly.IsAlive.Should().BeFalse();
        references.PluginType.IsAlive.Should().BeFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static UnloadReferences RoundTripHostRuntimeTypeAndUnload(byte[] image)
    {
        var loaded = LoadedPlugin.Load(image, "host-runtime-type");
        var context = new MemoryPackSerializerContext();
        var value = new HostRuntimeTypeEnvelope
        {
            RuntimeType = loaded.PluginType
        };

        var payload = MemoryPackSerializer.Serialize(value, context);
        var restored = MemoryPackSerializer.Deserialize<HostRuntimeTypeEnvelope>(
            payload,
            context);
        restored!.RuntimeType.Should().BeSameAs(loaded.PluginType);

        var references = loaded.Unload();
        restored = null;
        value = null!;
        context = null!;
        loaded = null!;
        return references;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static UnloadReferences LoadInvokeAndUnload(byte[] image, string name, string methodName)
    {
        var loaded = LoadedPlugin.Load(image, name);
        loaded.InvokeBoolean(methodName).Should().BeTrue();
        GlobalFormatterCachesExist().Should().BeFalse(
            "the runtime must not contain a process-global formatter provider or Type-keyed formatter graph");
        var references = loaded.Unload();
        loaded = null!;
        return references;
    }

    static byte[] CompilePlugin()
    {
        var (compilation, generatorDiagnostics) = CSharpGeneratorRunner.RunGenerator(
            PluginSource,
            preprocessorSymbols:
            [
                "NET7_0_OR_GREATER",
                "NET8_0_OR_GREATER",
                "NET9_0_OR_GREATER",
                "NET10_0_OR_GREATER",
            ]);
        generatorDiagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        result.Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.Success.Should().BeTrue();
        return stream.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool GlobalFormatterCachesExist()
    {
        var assembly = typeof(MemoryPackSerializer).Assembly;
        return assembly.GetType("MemoryPack.MemoryPackFormatterProvider") is not null ||
               assembly.GetType("MemoryPack.DefaultMemoryPackSerializerContext") is not null;
    }

    static bool RefersToAssembly(object? value, Assembly assembly)
    {
        return value switch
        {
            null => false,
            Type type => IsFromAssembly(type, assembly),
            Delegate callback => IsFromAssembly(callback.GetType(), assembly) || callback.Method.DeclaringType?.Assembly == assembly,
            _ => IsFromAssembly(value.GetType(), assembly),
        };
    }

    static bool IsFromAssembly(Type type, Assembly assembly)
    {
        if (type.Assembly == assembly)
        {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is { } element && IsFromAssembly(element, assembly))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => IsFromAssembly(argument, assembly));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ForceUnload(UnloadReferences references)
    {
        for (var i = 0; i < 12 &&
             (references.LoadContext.IsAlive || references.Assembly.IsAlive || references.PluginType.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext(string name)
            : base(name, isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(MemoryPackableAttribute).Assembly.GetName().Name)
            {
                return typeof(MemoryPackableAttribute).Assembly;
            }

            return null;
        }
    }

    sealed class LoadedPlugin
    {
        PluginLoadContext? loadContext;
        Assembly? assembly;
        Type? entryType;
        Type? pluginType;

        public Assembly Assembly => assembly!;
        public Type PluginType => pluginType!;

        LoadedPlugin(PluginLoadContext loadContext, Assembly assembly, Type entryType, Type pluginType)
        {
            this.loadContext = loadContext;
            this.assembly = assembly;
            this.entryType = entryType;
            this.pluginType = pluginType;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LoadedPlugin Load(byte[] image, string name)
        {
            var loadContext = new PluginLoadContext(name);
            using var stream = new MemoryStream(image, writable: false);
            var assembly = loadContext.LoadFromStream(stream);
            var entryType = assembly.GetType("CollectiblePlugin.PluginEntry", throwOnError: true)!;
            var pluginType = assembly.GetType("CollectiblePlugin.PluginDto", throwOnError: true)!;
            return new LoadedPlugin(loadContext, assembly, entryType, pluginType);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool InvokeBoolean(string methodName)
        {
            var method = entryType!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
            var result = (bool)method.Invoke(null, null)!;
            method = null!;
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public UnloadReferences Unload()
        {
            var loadContextReference = new WeakReference(loadContext!);
            var assemblyReference = new WeakReference(assembly!);
            var typeReference = new WeakReference(pluginType!);
            var context = loadContext!;

            entryType = null;
            pluginType = null;
            assembly = null;
            loadContext = null;
            context.Unload();
            context = null!;
            return new UnloadReferences(loadContextReference, assemblyReference, typeReference);
        }
    }

    sealed record UnloadReferences(WeakReference LoadContext, WeakReference Assembly, WeakReference PluginType);
}

[MemoryPackable]
public partial class HostRuntimeTypeEnvelope
{
    public Type? RuntimeType { get; set; }
}
