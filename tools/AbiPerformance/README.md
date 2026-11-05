# Generated ABI wrapper performance characterization

`python3 tools/AbiPerformance/compare.py` compares the pinned pre-wrapper handshake
commit `d4c15564af7c754e87dd019ca3cc25997097cda3` with the preserved nested-wrapper candidate
`42488401cf4f78ba0b2320c6da2701deef574702` and the current checkout's HEAD.
It builds all three revisions from `git archive`, using the identical checked-in harness
and SDK. It does not publish packages or alter any revision.

The four serialization cases exercise small and large exact-size models, the
version-tolerant temporary buffer, and a circular-reference graph. Before timing,
the harness verifies round trips and reference identity. The driver checks that
the generated code actually names the old/new buffer implementations. Payload
hashes and lengths must match across each pair. Process-median allocated bytes/op
must match within 0.01 B/op.

The workflow runs separately on hosted x64 and ARM64 Linux machines. Within each
machine, all processes are pinned to the same available CPU and use workstation
GC, default tiered compilation/PGO, ReadyToRun disabled and one reported processor.
There are eight independent case-isolated process blocks per case in rotating,
reversed three-way order (96 timed processes total). Each case warms for five seconds, briefly
yields for background tier compilation, calibrates its own batch to at least 100 ms, then measures
nine batches at that count (individual batches can be shorter). Each process contributes its median batch result. The summary gives
the geometric mean candidate/baseline ratio and a deterministic 20,000-resample
95% bootstrap interval over the eight block-paired log ratios. Raw samples are retained.

This is targeted serialization characterization, not a general serializer
benchmark or a hard timing acceptance gate. Hosted CPU contention, JIT layout,
finite warmup and the small number of process pairs limit inference. A confidence
interval overlapping 1 is inconclusive, not proof of equivalence. Deserialization,
other model shapes, runtime versions, operating systems and AOT throughput are not
measured here. Allocations, byte equality and validation failures do fail the run.

Separate no-tier, fully optimized processes capture JIT assembly for
`SerializeExact` and `WriteToAndReset` after timing. Additional default-tiered
processes capture the exact-small method after the same five-second warmup. These logs help inspect code
shape; no-tier logs are not the timing configuration. An empty filtered call-site list is
not evidence of inlining or zero wrapper cost. The workflow artifact preserves SDK
and CPU information, generated source, all process samples, JIT logs and summary.
