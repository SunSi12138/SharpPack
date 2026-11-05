# Reader optional-state initialization experiment

This branch is an experiment, not a merge recommendation. Its only production
change relative to PR #30 head `56113519` is `AggressiveInlining` on
`SharpPackReaderOptionalState.Init`. No guard, exception order, state reset,
wire behavior or public API is changed.

The method grew from three assignments to include three cached limit values.
A changed inlining decision is a hypothesis, not an established cause of the
observed context-path regressions. Repeated normalization and full configuration
copying at the payload guard were ruled out by source inspection.

## Controls

The validation workflow proves the production diff is exactly the single
attribute and pins A to dev `6666ada`, B to PR `56113519`, and C to frozen validation head `f50b9fd`. It builds one neutral managed harness against A and swaps only Core and
Streaming DLLs. Hashes, settings, affinity, fixture behavior and payload hashes
are checked. It also runs the original Build-Debug suite on both architectures.

JIT probes run separately from timings on x64 and ARM64, for tiered compilation
and PGO both ON and both OFF. Each selected case warms for eight seconds. Core
and harness probes use separate processes. Runtime assembly filtering was not
effective in the first diagnostic run; explicit method-name filtering now
bounds the selected output independently. The first JIT jobs failed their count
guard after retaining FullOpts evidence; this repair does not change candidate
production code or repeat performance measurements. Candidate C remains f50b9fd.
Only structured method identity, tier, code size, opcode and call-target counts
are printed. Raw listings remain temporary runner files. No method's absence
alone proves inlining; inspect caller code/call targets and observed tiers.

Default-runtime performance uses ten one-case processes per round: context
primitive and mixed object, references, small Span/sequence, 64 KiB Span
empty/custom contexts, and raw primitive/writer controls. Eight balanced ABC
triples and two same-binary B/B pairs retain all medians and allocation/GC
observations. Each process warms for two seconds through the same non-inlined
batch method, then takes five samples with identical calibrated counts across
A/B/C. All fixture initialization still occurs before case filtering.

Intervals resample eight paired log ratios, are unadjusted for multiple
comparisons, and are not a validated acceptance budget. Fixed warmup does not
prove tier convergence. Different jobs may run on different physical hosts.
JIT probes can change compilation timing and are not themselves benchmarks.
The earlier broad/focused evidence is retained and must not be overwritten.

This experiment does not execute the unsafe old BitArray formatter, remove any
limit, alter PR #30, merge, release, delete, change permissions or upload logs.
