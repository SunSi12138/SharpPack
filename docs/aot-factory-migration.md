# NativeAOT and trimming: upgrading formatter factories

SharpPack 2.0 narrows the members that deserialization asks the linker to keep.
Generated models now keep their own formatter factories, so a deserialize-only
program does not depend on an earlier serialization call.

## Rebuild generated models

Upgrade SharpPack.Core and SharpPack.Generator together, or use the matching
aggregate SharpPack package. Rebuild every assembly that contains generated
models, including referenced libraries and union case assemblies. An older model
DLL does not contain the 2.0 factory roots.

## Deserialize an array without an earlier write

With the generator in the calling project, a direct call such as
`SharpPackSerializer.Deserialize<Item[]>(payload)` keeps the array formatter
and the generated `Item` factory in a trimmed build. The generator can do this
when it can name the closed, single-dimensional array's managed element type
from generated code in that project. No startup registration is needed.

For an open generic wrapper or an inaccessible element type, keep the closed
formatter and model factory from reachable code that names the element type:

```csharp
SharpPackSerializer.PreserveFormatterFactory<Item>();
GC.KeepAlive(new SharpPack.Formatters.ArrayFormatter<Item>());
```

If the model also has a context factory and is read through a context, keep
that factory with `PreserveContextFormatterFactory<Item>()` as well.

## Update generic wrappers

If a generic wrapper forwards its type parameter to SharpPack deserialization,
replace its 1.x `NonPublicMethods` annotation with `NonPublicConstructors`:

```csharp
using System.Diagnostics.CodeAnalysis;
using SharpPack;

static T? Read<[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(
    ReadOnlySpan<byte> payload) => SharpPackSerializer.Deserialize<T>(payload);
```

Apply the same change when forwarding to context or async overloads on
SharpPackSerializer. If the wrapper itself reflects on non-public methods, keep that
independent requirement and add the constructor flag. Rebuild the assembly that
defines the wrapper; rebuilding only the model will not update the annotation.

## Preserve handwritten factories

For a model that implements a factory interface by hand and relies on automatic
formatter discovery, add the matching call to reachable application startup code:

```csharp
SharpPackSerializer.PreserveFormatterFactory<MyModel>();
SharpPackSerializer.PreserveContextFormatterFactory<MyModel>();
```

Use the first call for `ISharpPackFormatterFactory<T>` and the second for
`ISharpPackContextFormatterFactory<T>`. Call both only when the model implements
both interfaces. Use each closed model type that the application resolves, for
example `MyModel<int>`. The calls keep the factory methods available to the
published application without invoking them, initializing the model, or
registering a formatter or a context.

If the application already registers a formatter instance or calls
`RegisterFactory<T, TFactory>()` on a context builder, keep that explicit
registration for that context. Test the published app without first serializing
the model; if it relies on context overrides, test with those overrides enabled.
