# Changelog

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
