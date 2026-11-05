# PR #30 performance decision and bounded next steps

## Decision

Stop production micro-tuning for now. The evidence does not justify a third
production candidate after the Init-inlining and Reset-helper experiments.
Preserve original PR #30, both unsuccessful candidates and all functional and
performance baselines. **Functional acceptance is established for the tested
heads; performance acceptance remains unresolved.**

This is not a finding of no regression, and it is not a recommendation to merge.
A testable measurement hypothesis exists, but it does not yet supply a
production mechanism with a credible benefit sufficient to justify additional
state or branching complexity. No new experiment is launched by this decision
record.

## Sources and requirements

- [Issue #15](https://github.com/SunSi12138/SharpPack/issues/15) requires direct
  integer state/comparisons at aggregate boundaries, no new policy dispatch or
  per-primitive accounting, flattened reader fields and sentinel values rather
  than an extra enabled/disabled branch in each guard. It also requires
  SerializerSurfaceBenchmark and focused collection/depth measurements to show
  no meaningful default-hot-path regression.
- [PR #30](https://github.com/SunSi12138/SharpPack/pull/30), exact B
  `56113519bb9771fe33ec39daed59181024918228`, remains unchanged.
- Current dev A is `6666ada1d878da64b2f4d2c1107fbea30fad074f`; its production
  baseline is the shared ancestor `8efea5af`. Exact heads were rechecked
  on 2026-11-05.
- [Broad/focused performance evidence](https://github.com/SunSi12138/SharpPack/blob/7c58350edba61d5715ff5e7fc25e0a661d333d98/docs/validation/pr30-performance-20261105.md)
- [Init-only experiment](https://github.com/SunSi12138/SharpPack/blob/8442512985cbc17e0f45e30fde6a52eaef7e8822/docs/validation/pr30-init-inline-20261105.md)
- [State/copy decomposition](https://github.com/SunSi12138/SharpPack/blob/a08b2fe9a04f9acb99ba019cb976186cd09756a0/docs/validation/pr30-state-copy-20261105.md)
- [Reset-helper experiment, complete results and raw JIT follow-up](https://github.com/SunSi12138/SharpPack/blob/0e71a4a0a6377e6b28759a8c13460feb377bf9ee/docs/validation/pr30-reset-helper-20261105.md)

Issue #15 and the current PR description give a qualitative "no meaningful
regression" requirement, not a numerical latency/throughput budget for a
representative application. Do not silently replace that requirement with
"the overhead is only a few nanoseconds" or demand zero overhead indefinitely.

## What the evidence actually isolates

### 1. Small context reads have additional work, but its cost is not decomposed

Manual initialized Int32 reads, which omit public payload checking and
serializer state acquisition, repeatedly show roughly 2.2 ns of extra work
versus dev on both architectures. This rules out the payload guard as the
complete explanation. It does not distinguish initialization, reader snapshot
loads, configurable depth comparison, reset stores or changed generated code.

Source inspection gives a coherent group of mechanisms:

- Limits are normalized once in the immutable context constructor, not on each
  read. Repeated normalization is not a supported target.
- The payload guard reads a cached maxPayloadBytes integer; it does not copy
  the full configuration.
- Init copies resolved limits into operation state, Reset restores compatible
  defaults, and the reader captures depth/collection values in direct fields.
- The existing depth comparison changes from a constant threshold to a
  configured field. Collection/reference checks remain at their boundaries.

These are genuine added state/check operations, but component timing is
non-additive. On ARM64, Init-only's delta exceeded Init-plus-reset's delta;
constructor controls drifted; an x64 exact-limit guard probe was faster than
its noop control. Summing or subtracting those rows cannot identify a specific
store or guard to remove.

Relevant source:
[context initialization](https://github.com/SunSi12138/SharpPack/blob/56113519bb9771fe33ec39daed59181024918228/src/SharpPack.Core/SharpPackSerializerContext.cs),
[operation state](https://github.com/SunSi12138/SharpPack/blob/56113519bb9771fe33ec39daed59181024918228/src/SharpPack.Core/SharpPackReaderOptionalState.cs),
[reader fields and checks](https://github.com/SunSi12138/SharpPack/blob/56113519bb9771fe33ec39daed59181024918228/src/SharpPack.Core/SharpPackReader.cs).

### 2. Inlining is not an established remedy

The Init attribute changed a standalone wrapper but not the warmed batch in
the important cases, and did not provide stable overall gains. The Reset helper
made some batches smaller but increased ARM64 legacy context Int32 latency
about 9%, with no dependable offsetting benefit. Its two-architecture functional
suite passed all 665 tests each; that demonstrates preserved tested behavior,
not successful performance acceptance.

A third annotation, default-limits flag or speculative cleanup branch has no
stronger causal support. Code size and static call counts are insufficient
selection criteria.

### 3. A shared callback can change code generation for an unexecuted branch

The raw unmanaged production entry bypasses Reset. Nevertheless, the failed
Reset candidate made legacy raw Int32 slower in all eight paired processes on
both architectures. Its shared harness ReadInt closure contains both raw and
context branches. The raw JIT probe retained context-side code, and candidate
B/C batch size changed from 991 to 1059 bytes on x64 and 1328 to 1364 on ARM64.

This is a concrete code-generation confound worth isolating. It is not proof
that the unused context branch caused the original timing change, nor a basis
for discarding the measurements. Separate eight-second JIT processes do not
establish the exact code/tier used by two-second timing samples. The x64 raw
same-binary control also had a large excursion; ARM64 controls were steadier.

### 4. The large-array mechanism remains unresolved

The earlier x64 64 KiB allocating-read regressions remain real observations,
not withdrawn findings. They were not consistently reproduced after changing
fixture setup and hosts. Other runs exhibit slow process states in A or B,
sometimes absent from C. Pure-copy controls and writer controls also shift.

Post-timing normal-array alignment can differ by runtime; matching pinned
placement does not consistently resolve the signal and changes GC conditions.
The source body begins four bytes after the aligned wire buffer. None of these
facts proves that alignment, payload checking, Reset or allocation alone is
the cause.

Do not add small-operation deltas to predict a 64 KiB improvement. Do not label
every slow state noise or selectively remove it. Whether a version changes
the probability of those slow states remains open.

## Cost and benefit assessment

- The observed small-read differences are a few nanoseconds in the measured
  shapes. That is a scale reference, **not a predicted recoverable saving or
  mathematical upper bound**. Application throughput impact depends on the
  actual workload and whether serialization is a bottleneck.
- There is no credible recoverable-benefit estimate for mixed objects,
  reference graphs or 64 KiB arrays from the component probes.
- A default-limits flag, skipped state stores or an added specialized path
  increases branch/state maintenance and requires new proofs across default
  and limited contexts, custom formatters, failures, reentrancy, leases and ALC
  lifetime. The present evidence does not justify that cost.
- Replacing reader snapshots with repeated reads from mutable OptionalState is
  not a free layout optimization. Equivalence would need to be proved even
  when exposed optional state is reset or otherwise reused; it must not relax
  an active reader's effective limits. It also risks violating the intended
  flattened hot-path design.
- Reverting or delaying one guard changes the security/feature scope. This
  decision does not authorize such a change or weaken structural validation.

The appropriate current trade-off is to retain the original implementation and
its unresolved performance status, not to integrate an unproven optimization.

## One bounded validation phase, if performance work resumes

The next useful hypothesis is about measurement shape, not production code:

> Separating raw and context callbacks while holding fixture setup and runtime
> revisions fixed will distinguish shared-wrapper code-generation effects from
> a persistent context-path version difference.

A useful follow-up would have these limits:

1. Use original dev A and PR B as the production comparison. The already-failed
   Reset C may be retained solely as a diagnostic contrast, not a new candidate.
   Make no production changes.
2. Compare the existing shared conditional callback with separate raw/context
   callback types or methods in the same fixture history. Do not simultaneously
   change warmup policy, setup allocations and transport shape.
3. Start only with Int32 raw and empty context, retain same-binary controls and
   every sample, and predeclare the effect size/repeatability required to make
   a decision. Use a stable, recorded environment and paired process order.
4. Associate tier/code-version observations with the actual measured process
   where practical; keep tracing/profiling overhead separate from timing
   claims. Another detached JIT dump alone is insufficient.
5. Add at most one 64 KiB representative with a pure-copy control only after
   the small-path protocol can distinguish version effects from control drift.
   Do not immediately rerun the whole transport/type matrix.

Existing BenchmarkDotNet classes remain relevant for acceptance, but changing
framework alone does not solve fixture design. In particular,
[SerializerSurfaceBenchmark](https://github.com/SunSi12138/SharpPack/blob/56113519bb9771fe33ec39daed59181024918228/sandbox/Benchmark/Benchmarks/SerializerSurfaceBenchmark.cs)
still selects raw/context paths through GetContext in its source. Inspect its
generated path rather than assuming the branch disappears.
[ReadLimitsBenchmark](https://github.com/SunSi12138/SharpPack/blob/56113519bb9771fe33ec39daed59181024918228/sandbox/Benchmark/Benchmarks/ReadLimitsBenchmark.cs)
has separate default/limited collection and depth methods, but its within-build
ratio is not by itself a dev-versus-PR regression measurement. Proper matched
revision comparison remains necessary.

### Stopping and reopening conditions

- If callback separation removes the raw anomaly while a context difference
  remains, narrow investigation to that context path. Do not automatically
  assign the residual to one field or guard.
- If control drift still prevents separating version effects, stop and preserve
  an inconclusive result rather than launching more annotation experiments.
- If the remaining difference is below an agreed practical threshold, record
  the permitted overhead and scope explicitly. This is an acceptance decision,
  not a claim of zero regression.
- Reopen a production candidate only after a repeatable mechanism, a
  semantics-preserving change and worthwhile expected benefit are identified.
- Do not keep repeating broad matrices merely because the root cause is
  unresolved. The two failed candidates remain rejected regardless of whether
  every slow state is explained.

## Open decisions

1. What absolute/relative latency or throughput change is meaningful for the
   project's representative default/context workloads?
2. Which portion of the small context signal persists with independent entry
   callbacks and measured-process tier evidence?
3. Are the 64 KiB slow states reproducibly version-dependent under a controlled
   fixture, or dominated by environmental/setup variation?

Independent evidence review supports this bounded pause in production tuning.
No new production candidate, CI run, original-PR change, merge, release,
deletion or permission change is made by this record.
