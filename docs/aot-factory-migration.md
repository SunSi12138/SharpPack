# NativeAOT and trimming: formatter factory migration

The development branch replaces broad preservation of non-public model methods
with precise formatter-factory roots. Deserialize-only programs no longer depend
on an earlier serialization call to keep generated factories reachable.

This changes the trimming contract. It is not a promise that previously built
model assemblies will work unchanged with the new runtime.

## Generated models

Upgrade `SharpPack.Core` and `SharpPack.Generator` together, including indirect
references through the aggregate `SharpPack` package. Rebuild every generated
model assembly, including referenced libraries and union case assemblies, before
publishing a trimmed or NativeAOT application. Do not combine an old generated
model DLL with the new runtime and rely on broad method preservation.

Current generated models preserve their own default and context factory methods.
No application startup registration is required for those generated factories.
For models without user static initialization, the preservation field is read on
the factory creation path, not on every deserialization. Models with a user
static-constructor hook, initialized static fields/properties, or custom formatter
fields retain strict initialization semantics.

## Handwritten factory implementations

Implementing `ISharpPackFormatterFactory<T>` or
`ISharpPackContextFormatterFactory<T>` alone no longer guarantees automatic
discovery after trimming. This includes explicit interface implementations.
For each concrete model type used through automatic resolution, preserve the
implemented factory during application startup, before its first resolution:

```csharp
SharpPackSerializer.PreserveFormatterFactory<MyModel>();
SharpPackSerializer.PreserveContextFormatterFactory<MyModel>();

// Later: normal default or context-aware serialization/deserialization.
var value = SharpPackSerializer.Deserialize<MyModel>(payload, context);
```

Call only the API whose interface the type implements. A type implementing both
interfaces should call both APIs. For a generic model, preserve every closed type
the application will resolve, for example
`PreserveFormatterFactory<MyModel<int>>()`. Preserve a context factory for each
model type, not for each context instance.

These APIs establish linker roots through static-interface delegates. They do
not call the factory, force the model's static constructor, register a formatter,
retain a context, or keep a global registry of model types. Actual resolution
still creates and caches formatters according to the existing context rules.
Do not hide the startup calls in unreachable code and expect them to survive
trimming.

Alternatively, use the existing context builder's
`RegisterFactory<T, TFactory>()` for explicit default-factory registration, or
register an appropriate formatter instance. Explicit registration defines the
formatter used by that context; it is not a substitute for preserving the
automatic context-aware factory when those semantics are required.

Untrimmed JIT behavior is not evidence that a handwritten factory was preserved.
Test the published NativeAOT/trimmed executable with a deserialize-only entry
point and non-empty contexts when context overrides matter.

## Validation and performance boundaries

The generated field-root candidate was tested on x64 and ARM64 with isolated
deserialize-only cases, collection/generic/union/context paths, unused private
method symbol controls, and trimmed JIT. Handwritten startup roots were tested
independently with default/context factories, closed generic models, negative
unrooted controls, factory/static-constructor counters and collectible assembly
unloading. The repository workflows retain these checks.

A fixed-CPU x64 paired experiment compared the generated candidate against the
original baseline using two clean builds per version, 12 alternating paired
blocks and four same-binary control pairs. The candidate/baseline geometric mean
was 0.97386, with a within-run bootstrap 95% interval of [0.92998, 1.00129];
the same-binary control was 0.99948. Allocation stayed at 48 bytes per nested
read. All observations were retained, including a baseline process at 45.26 ns
versus the usual approximately 27–29 ns. These are noisy single-run results,
not a speedup claim or proof of general performance equivalence.

Normalized native instructions for the tested nested model/item deserializers
and caller matched the original baseline. Factory initialization checks moved
to the cold creation path. This removes the identified hot-path mechanism in
that corpus; applications should still benchmark their own workloads.

Reproduction and raw measurements:
[paired experiment 41880746565](https://github.com/SunSi12138/SharpPack/actions/runs/41880746565)
at validation commit `6ca5bb20f7e0aef7297b73da2ac680e67969d479`, comparing
`2c61f93dfda9da0eba9bfb0b6af2e93b48cd739e` with
`6666ada1d878da64b2f4d2c1107fbea30fad074f`. These measurements preceded combining
the separately tested handwritten startup APIs with the generated candidate.
