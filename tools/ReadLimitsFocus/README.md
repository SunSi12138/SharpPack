# Focused read-limit JIT diagnosis

The first context/platform matrix (run41995910039, head9ce22f0) showed equal
A/B allocation medians in aggregate and non-1 MiB surface rows, but small
allocation-count differences in 1 MiB rows. Timings were multimodal with large
same-binary variation in some cases. Those results are retained. Synchronized A/B modes can still yield
useful paired ratios; the broad limited-after-common comparison has additional
order bias and is not evidence of a speedup.

This follow-up is exploratory, selecting eight signals: raw/context Int32,
context depth32, raw/context circular references32, actual Utf16 mixed object,
64-byte two-segment raw reads and64-byte context Span reads. Every process
measures one case through the same non-inlined batch function used for its
two-second warmup, calibration and timed samples. Other fixtures still initialize
before filtering; initialization is not isolated to one shape.

A/B use current dev6666ada and exact PR3056113519 with identical managed harness
IL. Four empty-context cases also measure C with configured limits (exact
boundaries where applicable; Int32 uses the general configured limits). ABC
ordering balances pair direction4/4; per-case calibration/counts/payload hashes
match. Eight process pairs/triples and two same-binary controls retain all
paired medians plus control ratios and absolute medians, GC counts and allocation
ranges. No process exclusions or arbitrary raw-log uploads occur.

Tiered compilation/PGO ON and OFF run as separate jobs on x64 and ARM64. ON/OFF
jobs may use different physical hosts; do not use their absolute times to claim
causal attribution to tiering. The changed batch/filter/warmup protocol also
means this is not a controlled single-factor explanation of the first run.
The goals are to test whether candidate-versus-baseline signals remain in
cleaner measurements and to separate default runtime and fixed optimized-JIT
observations. Per-case bootstrap intervals are exploratory/unadjusted and do
not establish general no-regression or complete performance acceptance.

The first matrix persists all ordinary paired process medians and same-binary
ratios; its control absolute medians were only in temporary structured data.
This follow-up retains those absolute controls in the summary as well.
No production sources, PR metadata, merge, release or permission changes.
