# Ownership and lifetime

## Serializer calls

**Owner / boundary:** the caller owns input spans, streams, pipes,
`IBufferWriter<byte>` instances, and serializer contexts. SharpPack owns only
temporary state it creates for the duration of the operation.

**Allowed:** borrow caller storage synchronously, retain reusable buffers inside
an explicitly long-lived serializer/context object, and return newly allocated
payloads from allocation-returning APIs.

**Forbidden:** dispose caller resources, retain spans or borrowed buffers after
the call, return pooled memory to a pool while it is observable, or expose stale
state after an exception.

**Verification:** `ResourceOwnershipTest`, `WriterOptionalStateTest`,
`ReaderTest`, `ReentrancyTest`, and streaming tests.

## Formatter contexts and caches

**Owner / boundary:** a `SharpPackSerializerContext` owns its registrations and
context-local resolution results. Process-wide caches may contain only types and
formatters that cannot lengthen collectible `AssemblyLoadContext` lifetimes.

**Allowed:** immutable registration tables, context-local closed-generic
factories, weak associations for collectible types, and release of context state
when the context becomes unreachable.

**Forbidden:** a static strong reference from Core to a collectible type,
assembly, formatter, delegate, or constructed generic graph; mutating a built
context; substituting a user factory for the generated factory of another type.

**Verification:** `CollectibleAssemblyLoadContextTest`,
`SharpPackSerializerContextTest`, `ContextFormatterMatrixTest`, and execution of
`sandbox/CollectibleAlcSample` in CI.

## Generated formatters

**Owner / boundary:** the consumer compilation owns generated formatter types;
Core owns the runtime contracts they invoke; the Generator owns emitted source.

**Allowed:** generated static metadata and delegates whose lifetime follows the
consumer assembly, and context-provided dependencies passed through runtime
state.

**Forbidden:** Generator-to-Core mutable global state, runtime dependence on the
Generator assembly, or generated code that captures a transient context in a
process-wide cache.

**Verification:** generator characterization, collectible-context tests,
package inspection, and NativeAOT execution.
