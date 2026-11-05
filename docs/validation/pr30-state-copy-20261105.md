# PR #30 state, reader and 64 KiB cost investigation

## Outcome

No production fix is established by this phase. Keep PR #30 and every prior
candidate unchanged. The previously unsuccessful Init-inlining experiment
remains an experiment, not an accepted optimization.

The added checks have a repeatable small-operation cost in this harness: manual
initialized Int32 reads are about 2.2 ns slower on both architectures and the
public context entry is about 2.0 ns slower on x64 and 3.3 ns slower on ARM64.
Component probes do not support adding separate initialization, reset,
constructor and guard deltas to explain that total.

The earlier x64 64 KiB allocating-read regression is **not reproduced in this
new experiment**, rather than fixed or disproven. ReadAlloc.empty B/A is
0.9817 [0.9351, 1.0362] on x64 and 0.9957 [0.9800, 1.0097] on ARM64. The new
selected-fixture harness changes the call shape and setup/heap history from the
previous all-fixtures harness. Hosts also differ. These measurements do not
isolate which difference changed the old signal.

## Revisions and evidence

- A, current dev: `6666ada1d878da64b2f4d2c1107fbea30fad074f`.
- B, original [PR #30](https://github.com/SunSi12138/SharpPack/pull/30):
  `56113519bb9771fe33ec39daed59181024918228`.
- Both heads were rechecked on 2026-11-05 at approximately 10:28 UTC and remain
  unchanged. PR #30 still targets its existing branch; this experiment does not
  rebase or update it.
- Component/copy harness:
  [326bf6725fab3b4e4deee5e1d0048a2556887fe0](https://github.com/SunSi12138/SharpPack/commit/326bf6725fab3b4e4deee5e1d0048a2556887fe0),
  branch `validation/pr30-costs-20261105`.
- Read/copy JIT instrumentation:
  [3f98a94b4c77b166325a2d53d689ffe5318dcebe](https://github.com/SunSi12138/SharpPack/commit/3f98a94b4c77b166325a2d53d689ffe5318dcebe).
- Pure-copy JIT instrumentation repair:
  [651907db8c3f251de257412a2c2b049dbe29d167](https://github.com/SunSi12138/SharpPack/commit/651907db8c3f251de257412a2c2b049dbe29d167).
- [Earlier broad/focused acceptance evidence](https://github.com/SunSi12138/SharpPack/blob/7c58350edba61d5715ff5e7fc25e0a661d333d98/docs/validation/pr30-performance-20261105.md).
- [Unsuccessful Init-only experiment and its validation](https://github.com/SunSi12138/SharpPack/blob/8442512985cbc17e0f45e30fde6a52eaef7e8822/docs/validation/pr30-init-inline-20261105.md).

These commits add diagnostic tools/workflows only. Production source is
unchanged from dev in the diagnostic branch, and measured B is the immutable
original PR. CI checks the exact revisions, unchanged source/build settings,
ancestor baseline and identical generator tree. Functional acceptance remains
the existing exact-PR evidence; these new timing jobs are not a substitute for
the complete behavior, limits, AOT and package gates.

## Execution and protocol

[Measurement run 42007813212](https://github.com/SunSi12138/SharpPack/actions/runs/42007813212)
passed all four jobs:

- [x64 components](https://github.com/SunSi12138/SharpPack/actions/runs/42007813212/job/127178909766)
- [ARM64 components](https://github.com/SunSi12138/SharpPack/actions/runs/42007813212/job/127178910368)
- [x64 allocation/copy](https://github.com/SunSi12138/SharpPack/actions/runs/42007813212/job/127178910215)
- [ARM64 allocation/copy](https://github.com/SunSi12138/SharpPack/actions/runs/42007813212/job/127178910092)

SDK 10.0.114 builds one neutral managed harness against A. Only runtime Core and
Streaming DLLs are replaced for B; harness hashes must match within the job.
Each process initializes only the selected fixture, runs on one CPU, and warms
the same non-inlined batch function for two seconds with tiering/PGO enabled.
Eight alternating A/B or B/A process pairs, five samples per process, shared
calibrated counts and two B/B pairs retain all process medians, allocation
ranges and GC counts. No outlier is removed.

Ratios are geometric means of paired ratios with bootstrap intervals from
eight pairs. Median paired nanosecond differences summarize a different
quantity and can have a different sign when distributions are skewed. Intervals
are within-host, exploratory, unadjusted for multiple comparisons, and do not
prove equivalence. There is no predeclared universal performance budget here.
Warmup duration does not prove every timed sample reached the same JIT tier.

## Component findings

All component summary median thread allocations are 0 B/op.

| Probe | x64 B/A [95% interval]; median paired delta | ARM64 B/A [95% interval]; median paired delta |
|---|---|---|
| Repeated Init only | 0.9237 [0.8258,1.0338]; -0.164 ns | 1.3185 [1.2879,1.3523]; +1.168 ns |
| Init plus dirty ResetAndExit | 1.0344 [0.9946,1.0857]; +0.179 ns | 1.0948 [1.0882,1.1020]; +0.594 ns |
| Repeated already-clear Reset | 1.1142 [1.1077,1.1214]; +0.191 ns | 1.3331 [1.3329,1.3332]; +0.295 ns |
| Reader constructor, 64 KiB span | 1.0358 [0.9754,1.0948]; +0.377 ns | 1.0482 [1.0147,1.0821]; +0.325 ns |
| Manual initialized Int32 read | 1.1413 [1.1267,1.1539]; +2.175 ns | 1.1478 [1.1441,1.1515]; +2.264 ns |
| Public context Int32 deserialize | 1.1648 [1.1246,1.2148]; +1.972 ns | 1.1663 [1.1616,1.1709]; +3.290 ns |

The manual path performs Init, reader construction, ReadValue<int>, Dispose and
ResetAndExit. It omits public serializer payload checking and state acquisition.
Its regression shows that extra cost exists in that shorter combined path; it
does not assign the cost exclusively to reader fields, state stores or reset.

Init and ResetAndExit delegates are bound by reflection during setup, never by
reflection inside timing. The Init-only probe does not reset between operations.
The ResetClean probe repeatedly resets an already-cleared state and is not
isolated dirty-reset cost. The smaller ARM64 Init+Reset delta than Init-only
illustrates why these probe deltas cannot be added or subtracted as a causal
decomposition.

Constructor probes use a non-inlined by-ref consumer and Dispose. They avoid an
extra reader struct copy, but still include a different call/optimizer shape
from deserialization. Same-binary constructor ratios as low as 0.6577 (ARM64
64-byte span) and 0.8411 (x64 64-byte span), plus 64 KiB controls around
0.8968/0.9378, prevent a precise claim about the cost of two additional reader
fields.

Payload-guard diagnostics use B on both sides: a same-signature noop versus the
accepted real guard through prebound delegates. Default guard median differences
are +0.286 ns on x64 and +0.220 ns on ARM64. Exact-limit differences are
-0.313 ns on x64 and +0.202 ns on ARM64. The negative x64 difference is evidence
of call-shape/optimization limitations, not a negative production check cost.
Exact-limit acceptance and length-plus-one PayloadBytes rejection are validated
outside timing. No production guard is removed.

## 64 KiB follow-up

All allocating rows report identical A/B median thread allocation:
length + 24 bytes. Reuse rows report 0 B/op and verify that the destination
instance is unchanged and contents match.

| 65536-byte case | x64 B/A [95% interval] | ARM64 B/A [95% interval] |
|---|---|---|
| ReadAlloc.empty | 0.9817 [0.9351,1.0362] | 0.9957 [0.9800,1.0097] |
| ReadAlloc.raw | 0.9758 [0.9383,1.0109] | 1.0100 [0.9961,1.0285] |
| ReadAlloc.custom | 0.9967 [0.9322,1.0742] | 1.1363 [0.9980,1.4571] |
| ReadReuse.empty.normal | 0.9929 [0.9713,1.0043] | 1.0092 [1.0049,1.0131] |
| ReadReuse.raw.normal | 1.0106 [1.0079,1.0139] | 1.0202 [1.0153,1.0250] |
| ReadReuse.custom.normal | 1.0026 [1.0017,1.0042] | 0.9885 [0.9853,0.9915] |
| CopyAlloc.raw | 0.9918 [0.9578,1.0240] | 0.9842 [0.9707,0.9979] |
| CopyReuse.raw.normal | 1.0062 [1.0038,1.0088] | 0.9992 [0.9979,1.0005] |

The ARM64 custom allocating ratio is driven by a process median of 5924 ns
while the other seven B medians are near 2.1–2.2 microseconds; median paired
difference is only +9.839 ns. The 65504-byte empty allocating row has a similar
5916 ns process median and ratio 1.1298, but a median paired difference of
+15.484 ns. Keep those processes; do not call either a stable 13% regression.
ARM64 ReadAlloc.empty.65568 has a same-binary B/B ratio of 2.8842. The x64 pinned
reuse controls also have material dispersion.

Ordinary raw reuse is slower by about 1.06% on x64 and 2.02% on ARM64 in this
run (+10.951/+20.754 ns). Empty/custom/pinned cases do not show a consistent
direction. Pure-copy controls also exhibit small A/B shifts despite matching
copy logic. These observations prevent assigning every small difference to
read-limit checks.

Both neighboring sizes, 65504 and 65568, remain in the full tables below. The
prior x64 64 KiB empty/custom +13.55%/+17.99% evidence remains linked above.
No successful production change sits between that evidence and this run.

### Alignment and allocation observations

Each copy job retains 400 post-timing observation records. All input buffers
are modulo 64 = 0 and actual array-body sources modulo 64 = 4, because the
wire format has a four-byte length header. At 65536 bytes, source page offset
is 68 in these records. Each record collects 64 output remainders, not addresses.

Normal reused destinations are not identically placed across runtimes. For
65536-byte empty reads, post-timing A/B output offsets modulo 64 are 56/0 on
x64 and 0/8 on ARM64; pure normal reuse controls are 24/0 and 32/8 respectively.
The allocating-read A/B observations cover all eight 8-byte-aligned modulo-64
buckets with similar distributions, rather than one obvious candidate-only bad
bucket. This identifies an uncontrolled condition, not its performance effect. Normal
arrays can move during forced collections before timing; post-timing snapshots
do not establish their alignment throughout the samples.

Pinned reuse independently fixes output modulo 64 to 0 or 32, verified in all
records, with exactly 64 setup candidates on both sides and only the selected
array retained. Pinning also changes heap/GC behavior. The pinned results do
not establish a consistent limit-check regression or prove alignment caused the
old allocating-read signal.

Pure-copy controls match GC.AllocateUninitializedArray and
Unsafe.CopyBlockUnaligned, but do not reproduce the complete serializer
operation, context setup, root graph or JIT code. Do not subtract their timing
from serializer timing and call the difference an isolated check cost.

## JIT findings and retained diagnostic failure

[Read/copy JIT run 42008452501](https://github.com/SunSi12138/SharpPack/actions/runs/42008452501)
failed both jobs after completing the two read scenarios. Its original parser
incorrectly required nonempty selected reader-method listings for pure-copy
probes, where no reader method is expected. This was an instrumentation failure,
not a production test failure. Completed read summaries are retained:

- [x64 partial read JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42008452501/job/127181064177)
- [ARM64 partial read JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42008452501/job/127181063738)

[Pure-copy repair run 42009234875](https://github.com/SunSi12138/SharpPack/actions/runs/42009234875)
passed both jobs and only reran the missing pure-copy scenarios:

- [x64 copy JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42009234875/job/127183696869)
- [ARM64 copy JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42009234875/job/127183696331)

The repair permits zero selected Core methods only for pure-copy cases, still
requires a nonempty full dump and Tier1 batch evidence from Perf, and passes
seven parser controls. Both original and repaired histories remain intact.

| Tier1 batch | x64 A/B code bytes | ARM64 A/B code bytes |
|---|---:|---:|
| ReadAlloc.empty.65536 | 1955 / 2262 | 2348 / 2552 |
| ReadReuse.empty.normal.65536 | 2029 / 2210 | 2448 / 2580 |
| CopyAlloc.raw.65536 | 574 / 582 | 692 / 692 |
| CopyReuse.raw.normal.65536 | 488 / 488 | 580 / 580 |

On x64, read batches in A inline reference cleanup while B contains named
Reset calls. Both retain CORINFO_HELP_MEMCPY. The pure-copy A/B batches have
matching instruction/opcode/call counts within their jobs, although x64
CopyAlloc code size differs. Matching summaries do not prove byte-identical
machine code.

Static call-site counts include loop unrolling, fallback and exception paths.
Two Reset or memcpy sites in a listing do not mean two calls per operation.
ARM64 indirect calls lack reliable symbolic targets, so absence of a named
Reset call is not absence of reset work.

JIT probes use separate eight-second warmup processes, versus two seconds for
timings. They do not prove the exact tier or machine code used by every timed
sample. Read and repaired copy probes run on separate hosts. Compare A/B
within a job; do not make cross-job absolute timing or code-layout causal claims.

## Verifiable next directions

1. Reproduce the old 64 KiB signal with a controlled setup change before
   altering the copy implementation. Keep the earlier harness call shape and
   compare all-fixture versus selected-fixture initialization in balanced
   processes on the same host. Hold source and output placement constant where
   feasible, retain normal and pinned controls, allocations/GC, and A/A or B/B
   controls. If changing only setup history changes both runtime versions
   similarly, that argues against a check-specific 64 KiB fix. If the B/A gap
   survives these controls, investigate that exact entry's JIT and allocation
   path rather than assuming the new negative result closes it.

2. Test reset code layout as an independent small-operation hypothesis. A
   conservative candidate can keep all current Reset stores and reference
   clearing/retention behavior while moving reference-container cleanup into a
   separate helper, leaving a small inlineable reset body. A simpler
   attribute-only Reset candidate is also an isolated first test, but could
   increase code size. The observed outlined Reset provides a concrete JIT
   prediction, not a promise of speedup. Verify actual timed-shape JIT, then
   compare original B versus candidate on Int32/context, 64-byte/64-KiB spans,
   custom formatters and reference-heavy reads, on both architectures. Reject
   a candidate without repeatable gains or with regressions.

Any reset experiment must preserve context/formatter release, default limit
restoration, reference counters, retained-capacity thresholds, pooled leases,
reentrancy and exceptions. Rerun exact/over-limit tests, successive contexts,
custom formatter and circular/sparse reference cases, ALC unload, public API,
generated source, wire compatibility, packages and NativeAOT. Do not retain
context state across operations, omit checks or add an unchecked fast path.

The current component probes do not justify changing reader field storage or
the payload guard independently. Those require a focused candidate with all
checks preserved and whole-path validation, rather than arithmetic attribution.

## Review and boundaries

Independent review covered the fixture setup, pinned-allocation fairness,
identical copy sinks, pairing/count logic and 13 local statistics/design
controls. The final independent pass also approved the complete result interpretation,
alignment limitations, retained instrumentation failure and conservative
reset-helper hypothesis; seven repaired JIT parser controls passed.

No original PR update, merge, release, deletion, permission change, new PR or
raw-log/artifact upload was performed. Raw subprocess logs and instruction
dumps remain in temporary runner storage; structured numeric and symbolic
summaries are retained in job output.

## Complete comparison tables

### x64 components

| Case | right/left [95%] | median paired delta ns | left ns range | right ns range | left/right thread B/op | right/right controls |
|---|---:|---:|---:|---:|---:|---:|
| InitOnly | 0.9237 [0.8258,1.0338] | -0.164 | 6.545–8.976 | 6.543–8.402 | 0.000/0.000 | 0.9841/1.0018 |
| InitResetExit | 1.0344 [0.9946,1.0857] | 0.179 | 8.442–10.102 | 9.359–10.358 | 0.000/0.000 | 1.0267/0.9756 |
| ResetClean | 1.1142 [1.1077,1.1214] | 0.191 | 1.670–1.701 | 1.867–1.928 | 0.000/0.000 | 0.9992/1.0040 |
| CtorSpan64 | 1.0706 [0.9406,1.2156] | -0.006 | 5.049–6.502 | 5.666–6.970 | 0.000/0.000 | 0.9098/0.8411 |
| CtorSpan65536 | 1.0358 [0.9754,1.0948] | 0.377 | 5.017–6.503 | 5.627–7.022 | 0.000/0.000 | 0.9927/0.8968 |
| ManualReadInt | 1.1413 [1.1267,1.1539] | 2.175 | 14.978–15.315 | 16.870–17.549 | 0.000/0.000 | 1.0187/1.0168 |
| DeserializeInt | 1.1648 [1.1246,1.2148] | 1.972 | 15.807–16.092 | 17.742–20.428 | 0.000/0.000 | 0.9949/1.0090 |
| GuardDiagnosticDefault | 1.1158 [1.0829,1.1359] | 0.286 | 2.202–2.468 | 2.491–2.537 | 0.000/0.000 | 1.0008/1.0036 |
| GuardDiagnosticExact | 0.8745 [0.8739,0.8750] | -0.313 | 2.492–2.502 | 2.179–2.188 | 0.000/0.000 | 0.9999/0.9977 |

### ARM64 components

| Case | right/left [95%] | median paired delta ns | left ns range | right ns range | left/right thread B/op | right/right controls |
|---|---:|---:|---:|---:|---:|---:|
| InitOnly | 1.3185 [1.2879,1.3523] | 1.168 | 3.687–3.795 | 4.739–5.237 | 0.000/0.000 | 0.9386/1.0408 |
| InitResetExit | 1.0948 [1.0882,1.1020] | 0.594 | 6.350–6.417 | 6.917–7.070 | 0.000/0.000 | 1.0029/0.9834 |
| ResetClean | 1.3331 [1.3329,1.3332] | 0.295 | 0.885–0.886 | 1.180–1.180 | 0.000/0.000 | 0.9999/1.0001 |
| CtorSpan64 | 0.9854 [0.8508,1.1010] | 0.531 | 6.849–10.734 | 7.190–7.873 | 0.000/0.000 | 0.6577/0.9020 |
| CtorSpan65536 | 1.0482 [1.0147,1.0821] | 0.325 | 6.806–7.470 | 7.117–8.076 | 0.000/0.000 | 0.9563/0.9378 |
| ManualReadInt | 1.1478 [1.1441,1.1515] | 2.264 | 15.198–15.475 | 17.475–17.732 | 0.000/0.000 | 0.9972/0.9336 |
| DeserializeInt | 1.1663 [1.1616,1.1709] | 3.290 | 19.699–20.012 | 23.027–23.398 | 0.000/0.000 | 1.0113/1.0031 |
| GuardDiagnosticDefault | 1.1193 [1.1138,1.1251] | 0.220 | 1.830–1.867 | 2.069–2.078 | 0.000/0.000 | 0.9976/1.0021 |
| GuardDiagnosticExact | 1.1116 [1.1026,1.1207] | 0.202 | 1.844–1.900 | 2.081–2.086 | 0.000/0.000 | 0.9974/1.0026 |

### x64 allocation/copy

| Case | right/left [95%] | median paired delta ns | left ns range | right ns range | left/right thread B/op | right/right controls |
|---|---:|---:|---:|---:|---:|---:|
| ReadAlloc.empty.65504 | 0.9778 [0.9528,1.0032] | -63.716 | 2646.011–3135.685 | 2607.931–3181.170 | 65528.000/65528.000 | 0.9702/0.9618 |
| ReadReuse.empty.normal.65504 | 1.0037 [1.0026,1.0046] | 5.180 | 1285.402–1288.243 | 1288.390–1294.657 | 0.000/0.000 | 1.0013/0.9983 |
| CopyAlloc.raw.65504 | 0.9881 [0.9651,1.0097] | -21.056 | 2601.699–2833.196 | 2590.376–2780.088 | 65528.000/65528.000 | 1.0022/0.9455 |
| CopyReuse.raw.normal.65504 | 0.9992 [0.9986,1.0000] | -0.812 | 1280.688–1284.309 | 1280.516–1282.829 | 0.000/0.000 | 0.9793/1.0002 |
| ReadAlloc.empty.65536 | 0.9817 [0.9351,1.0362] | -90.766 | 2186.631–2610.297 | 2187.530–2533.197 | 65560.000/65560.000 | 1.0412/0.9792 |
| ReadReuse.empty.normal.65536 | 0.9929 [0.9713,1.0043] | 4.287 | 1286.373–1406.281 | 1291.301–1293.942 | 0.000/0.000 | 0.9999/1.0016 |
| CopyAlloc.raw.65536 | 0.9918 [0.9578,1.0240] | -18.989 | 2101.436–2392.470 | 2152.161–2306.176 | 65560.000/65560.000 | 1.0179/0.9925 |
| CopyReuse.raw.normal.65536 | 1.0062 [1.0038,1.0088] | 8.453 | 1276.642–1282.192 | 1283.469–1293.530 | 0.000/0.000 | 1.0007/1.0013 |
| ReadAlloc.empty.65568 | 0.9764 [0.9297,1.0296] | -68.520 | 2251.567–2704.161 | 2205.163–2534.433 | 65592.000/65592.000 | 0.9850/0.9119 |
| ReadReuse.empty.normal.65568 | 1.0042 [1.0035,1.0047] | 5.338 | 1284.862–1288.773 | 1290.371–1293.045 | 0.000/0.000 | 0.9989/0.9986 |
| CopyAlloc.raw.65568 | 0.9803 [0.9021,1.0369] | 16.863 | 1902.801–2659.280 | 2001.129–2168.947 | 65592.000/65592.000 | 0.8346/1.0482 |
| CopyReuse.raw.normal.65568 | 1.0005 [0.9986,1.0027] | -0.910 | 1279.100–1282.180 | 1278.138–1286.890 | 0.000/0.000 | 1.0002/0.9990 |
| ReadAlloc.raw.65536 | 0.9758 [0.9383,1.0109] | -22.787 | 2206.110–2467.802 | 2198.193–2416.381 | 65560.000/65560.000 | 0.9175/1.0753 |
| ReadReuse.raw.normal.65536 | 1.0106 [1.0079,1.0139] | 10.951 | 1294.274–1296.393 | 1305.150–1318.940 | 0.000/0.000 | 0.9993/0.9978 |
| ReadAlloc.custom.65536 | 0.9967 [0.9322,1.0742] | -86.308 | 2168.837–2583.659 | 2096.148–2747.437 | 65560.000/65560.000 | 1.0821/0.9794 |
| ReadReuse.custom.normal.65536 | 1.0026 [1.0017,1.0042] | 2.688 | 1302.263–1305.637 | 1304.807–1313.221 | 0.000/0.000 | 0.9994/0.9973 |
| ReadReuse.empty.pinned0.65536 | 1.0372 [0.9593,1.1692] | 6.210 | 1284.263–1551.587 | 1292.797–1983.022 | 0.000/0.000 | 1.1042/1.0001 |
| CopyReuse.raw.pinned0.65536 | 0.9925 [0.9247,1.0746] | 1.177 | 1268.932–1809.829 | 1270.364–1679.966 | 0.000/0.000 | 1.0000/1.2294 |
| ReadReuse.empty.pinned32.65536 | 1.0233 [0.9834,1.0662] | 31.766 | 1284.327–1388.685 | 1293.012–1461.435 | 0.000/0.000 | 1.0406/1.0699 |
| CopyReuse.raw.pinned32.65536 | 1.0130 [0.9783,1.0432] | 46.593 | 1266.619–1383.081 | 1269.168–1373.524 | 0.000/0.000 | 0.9627/0.9637 |

### ARM64 allocation/copy

| Case | right/left [95%] | median paired delta ns | left ns range | right ns range | left/right thread B/op | right/right controls |
|---|---:|---:|---:|---:|---:|---:|
| ReadAlloc.empty.65504 | 1.1298 [1.0010,1.4288] | 15.484 | 2275.075–2374.906 | 2278.380–5916.095 | 65528.000/65528.000 | 1.0100/0.9733 |
| ReadReuse.empty.normal.65504 | 1.0048 [0.9988,1.0099] | 7.371 | 1031.699–1037.477 | 1020.244–1049.123 | 0.000/0.000 | 0.9767/1.0016 |
| CopyAlloc.raw.65504 | 0.9977 [0.9854,1.0106] | -12.826 | 2156.489–2227.208 | 2142.694–2213.518 | 65528.000/65528.000 | 0.9788/1.0118 |
| CopyReuse.raw.normal.65504 | 0.9923 [0.9872,0.9971] | -6.684 | 1065.215–1077.756 | 1056.420–1071.490 | 0.000/0.000 | 1.0007/1.0075 |
| ReadAlloc.empty.65536 | 0.9957 [0.9800,1.0097] | 3.313 | 2267.304–2373.100 | 2271.187–2374.824 | 65560.000/65560.000 | 0.9954/1.0091 |
| ReadReuse.empty.normal.65536 | 1.0092 [1.0049,1.0131] | 9.715 | 1031.405–1044.272 | 1041.485–1052.135 | 0.000/0.000 | 0.9976/1.0016 |
| CopyAlloc.raw.65536 | 0.9842 [0.9707,0.9979] | -24.834 | 1898.492–2132.053 | 1894.990–2042.319 | 65560.000/65560.000 | 0.9681/1.0035 |
| CopyReuse.raw.normal.65536 | 0.9992 [0.9979,1.0005] | -1.405 | 948.671–952.256 | 947.485–951.512 | 0.000/0.000 | 1.0010/0.9999 |
| ReadAlloc.empty.65568 | 1.0087 [0.9995,1.0215] | 5.785 | 1948.657–2001.368 | 1965.078–2042.571 | 65592.000/65592.000 | 0.9936/2.8842 |
| ReadReuse.empty.normal.65568 | 1.0129 [1.0011,1.0316] | 2.536 | 1034.857–1051.168 | 1041.945–1115.817 | 0.000/0.000 | 0.9936/1.0165 |
| CopyAlloc.raw.65568 | 0.9953 [0.9404,1.0448] | 8.391 | 1886.258–2227.535 | 1879.876–2116.438 | 65592.000/65592.000 | 1.0058/1.0475 |
| CopyReuse.raw.normal.65568 | 0.9913 [0.9839,0.9956] | -5.534 | 1075.051–1079.785 | 1038.626–1073.779 | 0.000/0.000 | 0.9753/0.9983 |
| ReadAlloc.raw.65536 | 1.0100 [0.9961,1.0285] | 12.137 | 1961.771–2069.746 | 1986.154–2115.039 | 65560.000/65560.000 | 0.9679/1.0048 |
| ReadReuse.raw.normal.65536 | 1.0202 [1.0153,1.0250] | 20.754 | 1017.142–1031.592 | 1041.355–1050.030 | 0.000/0.000 | 1.0031/0.9871 |
| ReadAlloc.custom.65536 | 1.1363 [0.9980,1.4571] | 9.839 | 2130.415–2201.975 | 2130.312–5924.421 | 65560.000/65560.000 | 0.9664/0.9512 |
| ReadReuse.custom.normal.65536 | 0.9885 [0.9853,0.9915] | -12.283 | 1084.999–1092.228 | 1071.559–1079.068 | 0.000/0.000 | 1.0055/0.9406 |
| ReadReuse.empty.pinned0.65536 | 0.9993 [0.9951,1.0026] | 0.353 | 1034.412–1046.757 | 1033.560–1044.500 | 0.000/0.000 | 0.9993/0.9934 |
| CopyReuse.raw.pinned0.65536 | 1.0006 [0.9998,1.0013] | 0.943 | 949.603–952.079 | 950.362–951.859 | 0.000/0.000 | 0.9990/1.0023 |
| ReadReuse.empty.pinned32.65536 | 0.9737 [0.9676,0.9817] | -31.630 | 1058.738–1068.262 | 1025.899–1065.794 | 0.000/0.000 | 0.9920/0.9528 |
| CopyReuse.raw.pinned32.65536 | 1.0055 [1.0002,1.0154] | 0.753 | 971.918–1010.210 | 1009.373–1010.841 | 0.000/0.000 | 1.0018/1.0009 |
