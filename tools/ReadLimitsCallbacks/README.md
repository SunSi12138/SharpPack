# Bounded callback-shape diagnosis

This is one four-case diagnostic, not a new production candidate or a complete
performance acceptance matrix. It follows the recorded decision to stop
speculative production tuning. All production source remains unchanged.

## Frozen comparisons

- A: dev6666ada1d878da64b2f4d2c1107fbea30fad074f.
- B: originalPR56113519bb9771fe33ec39daed59181024918228.
- C: rejectedResetff724136dafdd0c1b9a4c98b1ddf115ca81b860a, retained only as a
  diagnostic contrast. This study cannot rehabilitate C for integration.

One neutral managed harness IL is copied across these runtime DLLs on each
architecture. Source heads, unchanged harness production tree, baseline
ancestor, generator identity, SDK10.0.114, loaded hashes and CPU affinity are
checked. The previous exact-head behavior gates remain the functional baseline;
this diagnostic does not rerun or replace them.

## Four cases, one pass

The shared Int32.raw and Int32.empty callbacks keep the previous conditional
ReadInt body. Int32Separate.raw and Int32Separate.empty use separate methods
that call the raw or context entry directly, without a timed mode branch.
Selection happens during setup. All four callbacks and the previous aggregate
fixtures initialize in every process, including reference-graph history, then
exactly one case is timed. The new callback additions necessarily change the
managed harness and compiler-generated method layout from the old experiment.
This compares callback shapes, not a proof that only one machine branch changed.

Four cases are interleaved inside eight blocks. Case order is mirrored/rotated;
ABC runtime order is pairwise balanced. Every case has shared calibrated counts,
five samples, eight ABC triples and two B/B plus two C/C control pairs. Existing
two-second warmup and per-sample GC protocol are retained. Each architecture
runs 140 processes including calibration, of which128 are timed. All samples,
slow states and unknown JIT observations remain in the main statistics.

No large-array, transport or extra type-shape timing is added. There is no
automatic repeat or broader matrix if results are noisy or inconclusive.

## Predeclared questions and diagnostic rule

Primary: does the previously observed greater-than20% rejected-C raw signal
depend materially on the shared callback shape?

Report per-architecture C/B for shared/separate raw plus their block-paired
ratio-of-ratios. Call the result strong support for removal of that large
shape-dependent signal only when all of these hold:

1. Shared raw C/B lower95% interval is greater than1.10.
2. The shared/separate C/B interaction lower95% interval is greater than1.
3. Separate raw C/B upper95% interval is below1.10.
4. All B/B and C/C ratios for both relevant raw shapes lie within0.9–1.1.

This10% boundary is a diagnostic size criterion for the earlier greater-than20%
signal, not an allowed library regression, an equivalence claim or an application
acceptance budget. Strong cross-architecture support requires both architectures
to meet the rule. Otherwise report the full numbers and narrower/inconclusive
evidence without retuning the rule or automatically rerunning.

Secondary: report original B/A empty-context effects in both shapes and their
block-paired interaction. Do not infer a precise guard/field cost from residual
differences. No outcome from these four rows alone completes #30's broader
performance acceptance or resolves the64KiB observations.

Intervals bootstrap eight paired log ratios, are exploratory/within-host and
unadjusted for multiple comparisons. The controls and raw distributions remain
part of interpretation even when an interval excludes1.

## Same-process JIT output visibility

All processes enable only the RunBatch disassembly filter and write to a unique
local file. The C# harness takes exactly two file-length snapshots outside the
timed samples: after warmup and after all five samples. It never waits for output,
polls, forces extra GC or extends warmup to obtain a listing.

After normal process exit, the driver parses raw byte prefixes. A version is
visible at a snapshot only if the specified Program RunBatch listing and its
entire end-marker line fit inside that byte prefix. Method identity, tier label,
OSR status, multiple versions and bounded code-size/opcode/symbolic-call summaries
are retained. Records distinguish versions visible before timing, newly visible
between snapshots and only visible after the final snapshot at process exit.
Missing/unparseable output is unknown, and no associated timing is discarded.

These are output-visibility observations, NOT proof of the executing tier:

- Buffered output can become visible long after compilation; no file growth
  does not prove no compilation or code-version switch occurred.
- A complete Tier1 listing before timing does not establish that a callsite or
  active frame has switched to it. OSR and callsite updates remain uncertain.
- RunBatch output does not cover every non-inlined callback or library callee.
- JIT output and metadata reads perturb this diagnostic. Do not compare its
  absolute times directly with previous observation-disabled jobs.
- Summary identifiers hash bounded summaries, not complete machine code.

Only numeric/symbolic summaries are printed. Raw subprocess logs and instruction
dumps remain in temporary runner storage; no raw logs or artifacts are uploaded.

## Boundaries and stopping condition

Stop after this one dual-architecture pass and independent review. Unknown
association, control drift, multimodal results or failure to reproduce the old
signal do not trigger additional runs. Preserve all outcomes and explain whether
the evidence changes confidence in the measurement mechanism. The original PR's
performance status and C's rejection do not change automatically. No original PR
update, production edit, merge, release, deletion or permission change is included.

## Recorded outcome

Run 42019395403 completed both architectures. ARM64 met the predeclared raw-shape
diagnostic rule; x64 failed its B/B control condition, so cross-architecture
strong support was not established. The original PR context signal remains,
and the Reset-helper candidate remains unselected. Independent result review
is complete. No follow-on timing run was added. See the
[full evidence report](../../docs/validation/pr30-callbacks-20261105.md).
