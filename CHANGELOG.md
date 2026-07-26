# Changelog

## 1.0.3

### Performance and resource control

- Bound Context-dependent array, list, tuple and generated-object formatters
  once per Context, so steady-state serialization uses the selected formatter
  directly instead of repeatedly walking the formatter graph.
- Added exact-size `byte[]` serialization for eligible generated object types,
  avoiding the intermediate-buffer-to-result copy while preserving the
  MemoryPack wire format.
- Added configurable retained serializer buffers. The default is an unpinned
  8 KB buffer per active serializer thread, the high-throughput preset is an
  unpinned 80 KB buffer, and applications can choose any size from 1 through
  `Array.MaxLength`.
- Preserved the zero-allocation serializer hot path for pre-sized
  `IBufferWriter<byte>` destinations.
- Added reproducible NuGet-only BenchmarkDotNet comparisons against
  MemoryPack 1.21.4, including latency, throughput and allocation results.

### Fixes for inherited MemoryPack issues

- Fixed generated unmanaged structs ignoring member-level custom formatters.
  MemoryPack's raw-copy path bypassed `[MemoryPackCustomFormatter]`; SharpPack
  now honors `[SharpPackCustomFormatter]` at the root and through arrays,
  lists, nullable values, tuples, key/value pairs, multidimensional arrays,
  closed generic shapes and generated object graphs. Plain unmanaged types
  keep the original raw-copy wire format and performance.
- Fixed truncated unmanaged-list payloads mutating an existing destination
  list before the payload byte count was validated. Both contiguous and
  segmented inputs now fail before changing caller-owned collection state.
- Fixed generated code for nested models losing required containing-type
  modifiers and generic constraints. Nested models inside `static`,
  `readonly`, `ref` and constrained generic partial types now compile with the
  correct containing declarations.

### Correctness and compatibility

- Fixed formatter-factory method-name and return-type collisions, recursive
  Context factory construction, helper-name collisions and conditional generic
  policy classification without falling back to process-wide formatter state.
- Fixed nullable unmanaged `List<T?>` formatter selection and preserved custom
  element formatter composition for explicit array formatters.
- Added validation for custom retained-buffer sizes, exact writer bounds and
  malformed or truncated bulk collection payloads.
- Verified all 203 original-HEAD golden corpus entries in both directions,
  including byte-for-byte equality for deterministic payloads. No wire-format
  change is introduced by this release.

## 1.0.2

- Reduced default-path overhead for small objects while improving large
  serialization throughput.
- Moved generated formatter-override handling into isolated cold paths without
  changing MemoryPack-compatible payloads.
- Reduced optional-state reset work and optimized contiguous reader/writer
  advancement.
- Added regression coverage for context isolation, circular references, pooled
  buffer ownership, generated callbacks, and helper-name collisions.

## 1.0.1

- Published SharpPack as a public project and prepared the first NuGet.org
  packages.
- Added a complete collectible `AssemblyLoadContext` host/plugin sample.
- Added reproducible BenchmarkDotNet comparisons against MemoryPack 1.21.4.
- Improved package metadata, descriptions, tags, and repository provenance.

## 1.0.0

- Renamed the product, assemblies, packages, namespaces, public APIs, source
  generator, streaming APIs, tests, and tooling from MemoryPack to SharpPack.
- Replaced process-wide formatter registration with generic formatter slots and
  context-owned formatter graphs.
- Added collectible `AssemblyLoadContext` isolation and explicit formatter
  contexts.
- Preserved the original MemoryPack binary wire format and golden payload
  compatibility.
- Added complete generic APIs, zero-copy entry points, stream and pipe support,
  DynamicUnion, circular-reference handling, NativeAOT verification, and
  resource ownership hardening.
- Runtime packages target .NET 10; the Roslyn generator remains
  `netstandard2.0`.
