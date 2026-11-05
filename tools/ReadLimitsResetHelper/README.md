# Reset reference-cleanup experiment

Only production change from PR #30 56113519: make Reset inlineable and move its
two reference-table clear/drop blocks verbatim into a non-inlined helper, called
when either table is non-null. All context/graph/counter/limit/requiresReset
stores remain in their original order after cleanup. Init and every limit check
remain unchanged. This is a hypothesis, not an accepted performance fix.

Eleven regression cases cover no-table state, dense/sparse 4096/4097 retention
boundaries, mixed thresholds, large-capacity empty tables, limits failure,
lease validity and duplicate return, and nested failure with outer success or
failure followed by reuse. Full dual-architecture CI retains behavior, public
API, generated source, ALC, packages, wire compatibility and NativeAOT gates.

Three runtime variants use exact dev A=6666ada, original PR B=56113519 and this
candidate C. Within each job/suite, all variants load one identical managed
harness IL; only runtime DLLs differ. Eight balanced ABC process triples plus
two B/B pairs use five samples, shared calibrated counts and two-second warmup
through the same non-inlined batch callsite. All process medians, allocation/GC
ranges, environment hashes and order are retained. No outlier exclusions.

The legacy suite preserves the previous all-fixtures setup and ten scenarios,
including circular-reference reads and Int32/context after reference tables
have been allocated in the aggregate group; the surface group need not have
reference-table history. Suites have different fixture shapes, so their timing
difference cannot isolate history alone. Such tables remain non-null after Reset, so the new helper
is not universally cold. The selected suite builds only its selected fixture
and has eight scenarios spanning clean initialization/read paths, 64 KiB
allocating/reused reads and matched pinned copy controls. Normal and pinned
placement remain different GC conditions, and post-timing address remainders
are observations rather than proof of alignment during timing.

JIT probes cover both suites on both architectures with eight-second warmup in
separate processes. They retain bounded method/tier/code-size/opcode/symbolic
call summaries, not raw dumps. Static call-site counts are not dynamic calls
per operation. Zero selected Core methods are accepted only for pure copy
controls; Perf requires Tier1 batch code. Timings do not inherit that separate
probe's tier proof. The two harness suites are not byte-identical to each other.

Intervals bootstrap eight paired log ratios and are exploratory, within-host,
unadjusted for multiple comparisons. A candidate needs repeatable whole-path
gains without functional or important workload regressions; code shrinkage or
an isolated microprobe win is insufficient. Preserve prior failed Init-only
and diagnostic experiments, original PR, and all earlier evidence. No merge,
release, deletion, permissions change or raw-log/artifact upload is part of this
experiment.

## Recorded outcome

Do not promote candidate ff724136. Both architectures passed all 665 tests and
all validation jobs, but legacy primitive/context paths regress without a
reliable compensating whole-path gain. Keep all slow-state observations and
prior candidates. The [reviewed report](../../docs/validation/pr30-reset-helper-20261105.md)
contains the complete comparison tables, JIT-only raw-path follow-up, exact
run links and limitations. The original PR is unchanged.
