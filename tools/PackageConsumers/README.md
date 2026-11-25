# Packaged consumer gate

The **Packaged consumers** workflow builds the four production projects and runs
the same pack command as both release workflows. It exercises a stable version
on Linux x64 and a prerelease version on Linux ARM64; neither job publishes.

The verifier checks the four nupkgs and two snupkgs in the supplied directory,
then uses those files directly. It runs real .NET 10/C# 14 and .NET 11/C# 15
consumers with empty package caches and no project references. Aggregate,
split and Streaming consumers verify selection of the matching Core asset
and run generated round trips on both frameworks. The aggregate also runs
under NativeAOT on both, as does the split consumer on .NET 10. Source mapping
restricts SharpPack packages to the local feed while allowing .NET tool and
runtime packs from NuGet.org.

The .NET 11 consumers exercise the new numeric scalars, nullable values,
arrays, and generated dictionary keys. Artifact generation runs on both
frameworks; the .NET 10 tooling executable can roll forward to .NET 11
when .NET 10 is absent.

On x64, `--compatibility` adds independent Roslyn 5.0 and 5.6 processes that load
the packaged generator, emit identical source, and report the same ABI error
against the prior Core. Roslyn 5.0 is the supported C# 14 floor; the project's
older Roslyn API reference is not a compiler support claim. The mixed-version
package checks build Core and Generator from the `v1.1.0` tag, not a public feed.
The previous Generator is tested only for an untrimmed round trip.

Streaming calls Core internals, so its nuspec declares the exact Core version.
The x64 gate checks that restore selects the matching Core even when a newer
one exists, fails when the exact version is missing, and flags a direct
override as NU1608. Consumers can explicitly override the range under NuGet's
direct-dependency-wins rules; keep Core and Streaming on the same version.

Both release workflows run the verifier after packing and before publication:

```sh
python3 tools/PackageConsumers/verify.py --feed ./publish --version 2.0.0 --architecture X64
```

The release check reads the original files without repacking. Compatibility
tests that produce additional fixture packages use a separate temporary feed.
