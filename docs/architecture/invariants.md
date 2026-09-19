# Architecture invariants

This document is the review checklist for changes to the serializer. An invariant
is not satisfied by documentation alone: every row names the automated evidence
that must remain green. Baselines are change detectors; updating one requires an
explicit explanation in the pull request.

| Owner / boundary | Allowed behavior | Forbidden behavior | Verification |
|---|---|---|---|
| Core wire format | Read and write the established MemoryPack-compatible representation; add behavior that remains byte compatible. | Silently changing headers, member order, null representation, integer encoding, or unmanaged layout. | `WireFormatCompatibilityTest`, `OriginalHeadCompatibilityTest`, and `tools/CompatibilityBaseline/generate-original-head-corpus.sh`. |
| Formatter resolution | Resolve closed generic types and context overrides deterministically. Cache only data whose lifetime is no longer than its owner. | Falling back to an incorrect open generic, allowing a global cache to root a collectible assembly, or bypassing an explicit context formatter. | `ContextFormatterMatrixTest`, `SharpPackSerializerContextTest`, `ExternalUnionContextRegistrationTest`, and `CollectibleAssemblyLoadContextTest`. |
| Writer hot path | Use spans and caller-owned `IBufferWriter<byte>` storage; grow only when required. | Adding an intermediate payload copy, per-value allocation, or reflection to an established zero-allocation path. | `SerializerStructBufferWriterTest`, `ExactSizeSerializationTest`, `ZeroCopyApiTest`, and benchmarks for performance claims. |
| Generated-code ABI | Generated formatters may call the documented CLR-public hidden surface. | Removing or changing such a member merely because it is hidden from IntelliSense. | Representative Stage 1 smoke coverage in `FormatterGenerationTest.FrozenGeneratedCodeAbi_CompilesAgainstCurrentRuntime`; current-generator characterization; see `generated-code-abi.md` for uncovered ABI families and follow-up work. |
| Streaming | Frame and buffer around Core without redefining object encoding or taking ownership of caller resources. | Treating partial input as EOF, consuming bytes beyond a frame, disposing a caller-owned stream/pipe, or maintaining a second wire format. | Streaming tests and the rules in `streaming-boundaries.md`. |
| NativeAOT | Keep supported serialization paths statically discoverable and generator-backed. | Introducing required runtime code generation or unguarded reflection on an AOT path. | Publish and execute `sandbox/NativeAot/NativeAot.csproj`. |

## Change procedure

1. Name the affected invariant in the pull request.
2. Add or update executable evidence before changing behavior.
3. Explain every intentional wire, API, or generated-source baseline diff.
4. Never weaken the wire, collectible-context, or NativeAOT checks merely to make
   an unrelated refactor pass.
