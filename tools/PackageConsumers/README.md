# Actual-package consumer gate (#18)

Run `python3 tools/PackageConsumers/verify.py --aot` with the repository SDK.
The fixture packs the four production projects at one unique prerelease version,
then builds consumers outside the repository so Directory.Build.props and project
references cannot supply incidental code, signing, or analyzer references.

Aggregate, split Core+Generator, and Streaming consumers restore from an empty
package cache and a cleared local-only feed. They assert the package asset graph
and execute generated-code roundtrips. Streaming exercises the signed friend
assembly boundary. The packaged generator runs in separate executable processes
with Roslyn 4.3.0, 4.4.0 and 5.6.0; generated output must agree for the common fixture,
compile, and emit under each. This is a smoke contract, not exhaustive language
coverage. NativeAOT consumes the aggregate package; source mapping confines
SharpPack packages to the local feed while allowing runtime/compiler tool packs.

Raw tool output remains only in the temporary runner directory. The workflow
uploads no logs or artifacts. It does not publish packages or modify production
behavior. The branch is based on dev 6666ada, not the unmerged #30/#31/#16 stack.

## Explicitly open

- The current Streaming Core dependency is a lower-bound range. The check verifies
  the packed version is referenced, but does not claim an exact lockstep contract
  or compatibility with arbitrary newer Core versions. Policy tightening remains.
- ABI mixed-version diagnostics depend on #17 and are not implemented here.
- Deserialize-only and warning-free AOT claims remain #31, not this roundtrip.
- Candidate-stack replay and ARM64 coverage remain separate from this x64 baseline.

## Baseline evidence

At test head c39837f (dev production sources 6666ada), run 41931191710
passed aggregate, split and Streaming consumers, Roslyn 5.6 compilation/emit,
minimum/current generated-source equality and packaged NativeAOT execution.
Roslyn 4.3 loaded and executed the packaged generator but compilation failed with
CS8987 (scoped modifier mismatch). The overall gate intentionally remains red;
loading a generator is not a claim that the old compiler accepts its output
against modern net10 Core metadata. Roslyn 4.4 is an additional boundary probe,
not an assumed supported minimum. A compatibility decision or production fix
needs its own justification; this harness does not suppress compiler errors.
