# Formatter contexts and collectible AssemblyLoadContext

## Lifetime contract

SharpPack exposes two generic formatter-resolution paths:

| API path | Formatter owner | Collectible ALC guarantee |
| --- | --- | --- |
| `SharpPackSerializer.Serialize(value)` | process-lifetime generic `FormatterSlot<T>` | no |
| `SharpPackSerializer.Serialize(value, context)` | static type slot or supplied override graph, selected by type lifetime | yes, after releasing the context and other plugin references |

The default overloads are the fastest application path. They never use a
`Type -> formatter` dictionary or a mutable global provider, but their closed
generic slots live for the process lifetime.

An explicit context is the lifetime boundary for plugin/RPC graphs. Nested
arrays, collections, tuples, generated objects, unions and custom formatters
receive the same context through `SharpPackWriter` and `SharpPackReader`.
MemoryPack-compatible unmanaged structs remain raw-copy boundaries: an
override for one of their member types does not enter the struct. Register a
formatter for the whole unmanaged type when its representation must change.
Collectible closed types and registered overrides remain context-owned. Empty
or configuration-only contexts use the same type-only static slots for
non-collectible types, avoiding an unnecessary graph lookup.

## Creating a context

Use the constructor when no custom formatter is needed:

```csharp
var context = new SharpPackSerializerContext();
```

Use the startup-only builder for configuration or overrides:

```csharp
var context = new SharpPackSerializerContextBuilder()
    .Configure(SharpPackSerializerConfiguration.Utf16)
    .Register<IPluginMessage>(new PluginMessageFormatter())
    .Build();
```

`Build` freezes registration and can only be called once. The builder is an
ownership-transfer object: once a build attempt starts, its registration
captures are released whether registration/freeze succeeds or throws. A
successfully built context continues to own its registered formatters
independently of the builder. A built context is safe to share between
concurrent RPC calls. Different contexts may register different formatters for
the same closed `T` without affecting each other.

`Stack<T>` serialization copies the element slots, then writes them in push
order. A formatter can replace a slot's value on the wire, but the replacement
is not written back into the caller's stack.

### Generate formatters for external types

Declare a root with `[assembly: SharpPackExternalType(typeof(T))]` when its
type is defined in another assembly and cannot be made `partial`. Add the
generated provider once when you build the context:

```csharp
using SharpPack;
using SharpPack.Generated;
using Vendor;

[assembly: SharpPackExternalType(typeof(Shipment))]

var builder = new SharpPackSerializerContextBuilder();
var context = SharpPackExternalFormatters.Register(builder).Build();
byte[] payload = SharpPackSerializer.Serialize(shipment, context);
Shipment? copy = SharpPackSerializer.Deserialize<Shipment>(payload, context);
```

Declare multiple roots with multiple attributes. The generator visits only
their serialization graphs, includes closed arrays and known collections,
and reuses existing SharpPack and built-in formatters. Explicit context
registrations take precedence over generated ones. The context-free APIs do
not use these formatters.

An ordinary external object uses SharpPack's existing object layout:

- Public readable properties and fields participate, base type first, with
  SharpPack include/ignore attributes applied. An inaccessible included member
  present in the referenced metadata is an error. Each member needs an
  accessible setter (including `init`) or one matching constructor parameter.
- Use a single callable constructor or select one with
  `[SharpPackConstructor]`. A selected constructor present in the referenced
  metadata must be accessible; its parameters must match serialized member
  names and types. Positional records and structs containing managed references
  follow the same rules.
- Metadata preserves the order within its field and property tables, but not
  the relative source order of fields and properties declared together. For
  such a type, all members need contiguous `[SharpPackOrder]` values starting
  at zero. With a vendor type you cannot annotate, use a DTO or an explicit
  formatter instead of guessing the order.
- Version-tolerant objects, reference-tracked objects, unions, callbacks and
  member-specific formatters use their existing authoritative SharpPack
  formatter. Sidecars do not infer those contracts. A recursive *type* graph
  is valid, but an ordinary object layout does not preserve cyclic instances.

`SHARPPACK045` identifies the root, the nested path and the reason when no
formatter can be generated. No reflection-based object serializer is tried
at runtime. Existing unmanaged raw-copy bytes, including the padding policy,
are unchanged.

#### Reuse the compiler API in an integration

Reference the shipped `SharpPack.Generator.dll` in the compiler project, not
the runtime application. Call `SharpPackSidecarGenerator.Generate` with your
original Roslyn compilation, the payload roots selected by your own routing,
and a unique provider name. Report the returned diagnostics and add the
returned source using its hint name. Your generated startup code calls
`SharpPack.Generated.<providerName>.Register(builder)` before building the
adapter context.

The result supplies each root's support, reachable sidecar types, and ordered
wire members. It returns Roslyn type symbols rather than C# display names, so
your integration can apply its existing identity rules, such as ignoring tuple
element labels. Pass a type in `registeredFormatterTypes` only when your integration
will provide that formatter. This hands the node to the integration without
analyzing its members. No generated attribute has to be visible to a second
source generator.

### Register an external union formatter

When a formatter declared with `[SharpPackUnionFormatter]` lives in a separate
assembly from its union contract, register it on the context builder with the
generated `Register<FormatterName>` extension, such as `.RegisterMessageFormatter()`.
Import the **union contract's namespace** to bring that extension into scope.
The formatter itself stays in the namespace where it was declared. For an open
generic union, give the formatter the same number of type parameters as the
contract. They bind by position and can have different names.

### Register a Brotli formatter

To compress a type whenever its formatter is selected in a context, wrap its
uncompressed formatter explicitly:

```csharp
var context = new SharpPackSerializerContextBuilder()
    .Register(BrotliFormatter<MyMessage>.Wrap(messageFormatter))
    .Build();
```

`messageFormatter` is the ordinary `SharpPackFormatter<MyMessage>` for the
uncompressed value. Its nested reads and writes share the current context and
the enclosing operation's recursion limit.
Use the member-level Brotli attribute when only a single field needs
compression. A Brotli wrapper cannot use another generic Brotli wrapper as
its uncompressed formatter.

In 2.0, `BrotliFormatter<T>.Default` has the generic formatter type. If code
used the old field for byte arrays, switch it to `BrotliFormatter.Default`.
Use `Wrap` rather than `Default` for context registration.

## System.Type payload capability

`System.Type` values are disabled by default, including null Type values. The
built-in Type formatter requires an explicit context with `RegisteredOnly`
enabled. Formatter registration and root-type use never grant Type-payload
permission, and reading or writing a payload never changes the registry.

```csharp
var context = new SharpPackSerializerContextBuilder()
    .EnableTypeSerialization()
    .RegisterType<MyRequest>("request-v1")
    .RegisterType(typeof(MyResponse), "response-v1")
    .Build();

byte[] payload = SharpPackSerializer.Serialize<Type>(typeof(MyRequest), context);
Type? type = SharpPackSerializer.Deserialize<Type>(payload, context);
```

The Type wire value is the registered ID encoded as an ordinary SharpPack string
using the context's string encoding. Null is supported only in an enabled
context. IDs use ordinal, case-sensitive equality and must be nonempty and not
whitespace. Duplicate IDs or duplicate Types (including repeated identical
registrations) fail during registration; aliases are not supported.
`RegisterType` does not enable serialization. `Configure` replaces all settings,
so call `EnableTypeSerialization` after it or explicitly set
`TypeSerializationMode.RegisteredOnly` in the configuration.

`Build` copies registrations into a private immutable bidirectional registry.
Both successful and failed build attempts clear the builder's Type references
along with its formatter captures. There is no global Type registry, CLR-name
lookup, assembly probing, or compatibility fallback. An unknown Type or ID
fails with `SharpPackSerializationException`; a failed lookup cannot authorize
later operations. Custom formatters remain trusted application code, not a
sandbox controlled by the built-in Type formatter.

### Migration from the 1.x Type-name contract

This development change intentionally breaks previous `System.Type` payloads.
Choose stable protocol IDs on both endpoints and register each allowed Type
explicitly. Do not use assembly-qualified names as protocol IDs. Legacy CLR-name
payloads are not automatically resolved or converted, including the old
MemoryPack-to-SharpPack name substitution. Convert stored legacy data with a
separate trusted migration application using the old version; do not enable
runtime fallback in the new reader. Keep endpoints on compatible contracts
until migration is complete. Non-Type wire payloads remain unchanged.

The pinned original 203-entry compatibility corpus remains unchanged. Its two Type-containing entries
(`System.Type` and `System.Type[]`) are explicitly tested for rejection instead
of legacy wire roundtrip. The reverse-compatible current-writer corpus retains
all 190 non-Type runtime entries (previously 192 including those two Type
entries). The 11 original fixture-local entries retain their separate explicit
checks. Classification traverses runtime element types and generic arguments;
no other unresolved runtime types or extra Type exclusions may be skipped.
Actual counts are asserted by the compatibility tool, not inferred from CI badges.

For collectible plugins, load and discover the permitted Types, build that
plugin's immutable registry, then publish its context. Independent contexts may
map the same ID to different Types, even with identical CLR names in different
ALCs. Stop plugin requests and release its context and other plugin references
before unloading. To change an active registry, publish a new context snapshot.

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
path is a typed `SharpPackFormatter<T>`.

Exceptions raised while resolving an explicit-context formatter are not added
to the weak graph cache.

## RPC and zero-copy I/O

Write directly to a reusable struct writer:

```csharp
var output = new ArrayBufferWriter<byte>();
int payloadLength = SharpPackSerializer.Serialize(ref output, value, context);
```

Class-based `IBufferWriter<byte>` implementations also have a by-value
convenience overload. Deserialization from `ReadOnlySpan<byte>` or
`ReadOnlySequence<byte>` returns the consumed byte count through the `ref`
overloads.

For a length-prefixed RPC transport, `SharpPack.Streaming` can write and read
one frame without an intermediate payload array:

```csharp
int length = await SharpPackStreamingSerializer.SerializeFrameAsync(
    pipe.Writer, value, context, cancellationToken);

MyMessage? message =
    await SharpPackStreamingSerializer.DeserializeFrameAsync<MyMessage>(
        pipe.Reader, length, context, cancellationToken);
```

The frame length belongs to the RPC transport. It is not embedded in or added
to SharpPack's wire format.

## Wire compatibility

The formatter architecture does not change SharpPack's payload contract:
object and collection headers, member order, union tags, string encodings and
version-tolerant layouts remain unchanged. Existing SharpPack payloads for
supported types can be deserialized by either the default or explicit-context
path.

The test suite contains fixed payloads produced by the original format for
primitives, arrays, UTF-8/UTF-16 strings, generated objects and unions.

## Unload requirements

`AssemblyLoadContext.Unload()` starts unloading; it cannot override unrelated
strong references. The host must release:

- the `SharpPackSerializerContext`;
- plugin values and application-owned formatter instances;
- plugin `Type`, `Assembly`, reflection objects, delegates and exceptions;
- streams, buffers, tasks or callbacks that capture plugin state.

The regression suite compiles and loads collectible plugins, exercises nested
graphs, unions, custom formatters, buffer writers, sequences and async streams,
then verifies that the load context, assembly and types are collected.

The executable
[`sandbox/CollectibleAlcSample`](../sandbox/CollectibleAlcSample) demonstrates
the complete host/plugin lifecycle: load a plugin into a collectible context,
pass an explicit serializer context into plugin-owned generic code, release all
strong references, call `Unload`, and verify collection.


## Deserialization limits

Aggregate read limits and payload-size boundaries are documented in
[Deserialization read limits](read-limits.md).
