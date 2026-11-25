# Packaged consumer gate

The **Packaged consumers** workflow builds the four production projects and runs
the same pack command as both release workflows. It exercises a stable version
on Linux x64 and a prerelease version on Linux ARM64; neither job publishes.

The verifier checks the four nupkgs and two snupkgs in the supplied directory,
then uses those files directly. Temporary consumers use the producer's .NET 10
SDK and C# 14 with an empty package cache and no project references. Aggregate,
split and Streaming consumers restore from the local feed and run a generated
round trip. The aggregate and split consumers also publish and run under
NativeAOT; source mapping restricts SharpPack packages to the local feed while
allowing .NET tool and runtime packs from NuGet.org.

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
