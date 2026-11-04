# Actual-package consumer gate (#18)

Run `python3 tools/PackageConsumers/verify.py --aot` with the repository SDK.
The supported consumer contract is the repository README's **.NET 10 / C# 14**,
not an old compiler inferred from the generator's Roslyn API dependency.

## Required checks

The fixture packs the four production projects at one isolated prerelease version,
then builds consumers outside the repository so Directory.Build.props and project
references cannot supply incidental code, signing, or analyzer references.
It verifies the producer SDK against the validation pin 10.0.114 (within the
repository 10.0.102/latestPatch policy), writes an exact-version,
roll-forward-disabled global.json above all temporary consumers, and verifies and
records the actual consumer SDK. Every consumer explicitly selects C# 14.

Aggregate, split Core+Generator, and Streaming consumers restore from an empty
package cache and a cleared local-only feed. They assert the package asset graph
and execute generated-code roundtrips. Streaming exercises the signed friend
assembly boundary. A separate current Roslyn 5.6 host must load the packaged
generator, compile its generated C# 14 fixture and emit successfully.
NativeAOT consumes the aggregate package; source mapping confines SharpPack
packages to the local feed while allowing runtime/compiler tool packs.

## Historical observations, not support promises

Roslyn 4.3 and 4.4 are isolated historical probes. Their restore/compiler failures
are reported but do not fail the supported .NET 10 / C# 14 consumer gate. Their
generated-source equality is observed when available. Neither a passing probe
nor the generator's Roslyn 4.3 API reference establishes a product compiler
minimum. Production scoped/ref-safety semantics are unchanged.

The initial strict experimental matrix intentionally failed at head 950f7a5,
[run 41931492141](https://github.com/SunSi12138/SharpPack/actions/runs/41931492141):
Roslyn 4.3 loaded and executed the packaged generator but compilation failed with
CS8987 (scoped modifier mismatch). Roslyn 4.4 and 5.6 compiled/emitted this fixture;
all three generated the same source. Aggregate, split, Streaming and packaged
NativeAOT roundtrips and clean-tree verification passed. This history is retained,
not suppressed or reclassified as an old successful run.

## Limits and output policy

Raw tool output remains only in the temporary runner directory. The workflow
uploads no logs or artifacts. Harness failures print bounded summaries rather
than tracebacks. The workflow does not publish packages or modify production
behavior. The production source baseline is dev 6666ada, not the unmerged #30/#31/#16
stack. This lockstep candidate builds on the dual-architecture package harness.

- This independent candidate changes Streaming's package contract to exact Core
  version alignment. Project-reference development remains unchanged. A custom
  nuspec uses public NuGet pack extension points, with metadata/payload parity
  checked against automatic packaging from identical build outputs. Release-equivalent
  IncludeSymbols/snupkg packaging also compares metadata and PDB bytes.
- The gate verifies exact `[PackageVersion]`, same-version selection even with a
  newer Core available, missing-exact-version NU1102, and direct-override NU1608
  promoted to error. Every restore case has a fresh cache and local-only feed.
- NuGet direct-dependency-wins can override the range and users can suppress its
  warning. This is a declared/validated package constraint, not an unconditional
  runtime anti-mixing mechanism. Generator/Runtime ABI diagnostics remain #17.
- ABI mixed-version diagnostics depend on #17 and are not implemented here.
- Deserialize-only and warning-free AOT claims remain #31, not this roundtrip.
- Candidate-stack replay remains separate from this dev baseline.
- CI runs the same required checks on Linux X64 and Arm64. The harness checks
  runner architecture, and every consumer (including AOT) checks actual process
  architecture against its matrix leg before performing a roundtrip.
- The small generated fixture is not exhaustive language or product coverage.
