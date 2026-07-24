# Formatter contexts and collectible AssemblyLoadContext

## Lifetime contract

MemoryPack exposes two generic formatter-resolution paths:

| API path | Formatter owner | Collectible ALC guarantee |
| --- | --- | --- |
| `MemoryPackSerializer.Serialize(value)` | process-lifetime generic `FormatterSlot<T>` | no |
| `MemoryPackSerializer.Serialize(value, context)` | static type slot or supplied override graph, selected by type lifetime | yes, after releasing the context and other plugin references |

The default overloads are the fastest application path. They never use a
`Type -> formatter` dictionary or a mutable global provider, but their closed
generic slots live for the process lifetime.

An explicit context is the lifetime boundary for plugin/RPC graphs. Nested
arrays, collections, tuples, generated objects, unions and custom formatters
receive the same context through `MemoryPackWriter` and `MemoryPackReader`.
Collectible closed types and registered overrides remain context-owned. Empty
or configuration-only contexts use the same type-only static slots for
non-collectible types, avoiding an unnecessary graph lookup.

## Creating a context

Use the constructor when no custom formatter is needed:

```csharp
var context = new MemoryPackSerializerContext();
```

Use the startup-only builder for configuration or overrides:

```csharp
var context = new MemoryPackSerializerContextBuilder()
    .Configure(MemoryPackSerializerConfiguration.Utf16)
    .Register<IPluginMessage>(new PluginMessageFormatter())
    .Build();
```

`Build` freezes registration and can only be called once. A built context is
safe to share between concurrent RPC calls. Different contexts may register
different formatters for the same closed `T` without affecting each other.

## Resolution model

The default path resolves a type once:

```text
Serialize<T>
  -> FormatterSlot<T>
     -> generated type-owned formatter factory
     -> built-in/generic-shape formatter
     -> error formatter
```

The explicit override or collectible-type path is weakly keyed by its
formatter graph:

```text
Serialize<T>(value, context)
  -> ContextFormatterSlot<T>
     -> context registration
     -> generated type-owned formatter factory
     -> context-owned built-in/generic-shape formatter
```

There is no public formatter provider, `IsRegistered`, open-generic mutable
registry, non-generic serializer overload or runtime `Type/object` serializer
path. Reflection is limited to cold formatter-shape/factory discovery; the hot
path is a typed `MemoryPackFormatter<T>`.

Exceptions raised while resolving an explicit-context formatter are not added
to the weak graph cache.

## RPC and zero-copy I/O

Write directly to a reusable struct writer:

```csharp
var output = new ArrayBufferWriter<byte>();
int payloadLength = MemoryPackSerializer.Serialize(ref output, value, context);
```

Class-based `IBufferWriter<byte>` implementations also have a by-value
convenience overload. Deserialization from `ReadOnlySpan<byte>` or
`ReadOnlySequence<byte>` returns the consumed byte count through the `ref`
overloads.

For a length-prefixed RPC transport, `MemoryPack.Streaming` can write and read
one frame without an intermediate payload array:

```csharp
int length = await MemoryPackStreamingSerializer.SerializeFrameAsync(
    pipe.Writer, value, context, cancellationToken);

MyMessage? message =
    await MemoryPackStreamingSerializer.DeserializeFrameAsync<MyMessage>(
        pipe.Reader, length, context, cancellationToken);
```

The frame length belongs to the RPC transport. It is not embedded in or added
to MemoryPack's wire format.

## Wire compatibility

The formatter architecture does not change MemoryPack's payload contract:
object and collection headers, member order, union tags, string encodings and
version-tolerant layouts remain unchanged. Existing MemoryPack payloads for
supported types can be deserialized by either the default or explicit-context
path.

The test suite contains fixed payloads produced by the original format for
primitives, arrays, UTF-8/UTF-16 strings, generated objects and unions.

## Unload requirements

`AssemblyLoadContext.Unload()` starts unloading; it cannot override unrelated
strong references. The host must release:

- the `MemoryPackSerializerContext`;
- plugin values and application-owned formatter instances;
- plugin `Type`, `Assembly`, reflection objects, delegates and exceptions;
- streams, buffers, tasks or callbacks that capture plugin state.

The regression suite compiles and loads collectible plugins, exercises nested
graphs, unions, custom formatters, buffer writers, sequences and async streams,
then verifies that the load context, assembly and types are collected.
