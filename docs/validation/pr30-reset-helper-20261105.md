# PR #30 Reset helper experiment

## Decision

**Do not promote this candidate.** Keep it as an unsuccessful performance
experiment. The behavior checks pass, and the JIT shows the intended code
reorganization, but paired whole-path measurements show regressions without a
reliable compensating benefit. PR #30 and previous candidates remain unchanged.

The clearest adverse signal is legacy Int32: raw reads are slower in all eight
candidate-versus-PR pairs on both architectures, and ARM64 context reads slow
about 9%. The raw unmanaged production path bypasses Reset, so that signal
cannot simply be explained as executing the new helper. Its precise cause
remains a separate diagnostic question, not a reason to promote the candidate.

## Candidate and source invariants

- A, dev: `6666ada1d878da64b2f4d2c1107fbea30fad074f`.
- B, original [PR #30](https://github.com/SunSi12138/SharpPack/pull/30):
  `56113519bb9771fe33ec39daed59181024918228`.
- C: [ff724136dafdd0c1b9a4c98b1ddf115ca81b860a](https://github.com/SunSi12138/SharpPack/commit/ff724136dafdd0c1b9a4c98b1ddf115ca81b860a),
  branch `validation/pr30-reset-helper-20261105`, directly based on B.

The only production change is in SharpPackReaderOptionalState:

1. Mark public Reset with AggressiveInlining.
2. Call a private NoInlining ResetReferences helper when either reference
   container is non-null.
3. Move the two existing container clear/drop blocks verbatim into that helper.
4. Preserve every original context, formatter graph, reference counter, limit
   and requiresReset store, in the same order after reference cleanup.

Init, limit checks, exception rules, lease identity/generation and isInUse are
unchanged. The non-null condition is intentional: an allocated but empty
container still follows the original cleanup path. Retention continues to use
Count > 4096, not capacity or >= 4096. No context state is retained across
operations and no unchecked fast path is introduced.

A workflow verifies the exact transformation from B and rejects any additional
production/build-setting change. Generator source remains identical. The
candidate production file and new test file were read back and verified after
publication.

## Functional validation

[Build-Debug 42012806804](https://github.com/SunSi12138/SharpPack/actions/runs/42012806804)
passed both architectures:

- [x64](https://github.com/SunSi12138/SharpPack/actions/runs/42012806804/job/127195805189):
  665 passed, 0 failed, 0 skipped.
- [ARM64](https://github.com/SunSi12138/SharpPack/actions/runs/42012806804/job/127195805526):
  665 passed, 0 failed, 0 skipped.

Eleven new regression cases cover no-reference-table state with context/graph
and limits; dense/sparse counts 4096 and 4097; mixed retention boundaries;
allocated empty tables with large capacity; reset/reuse after a reference-limit
failure; active lease validity and duplicate return; and nested limit failures
with both outer success and failure followed by another successful read.

Public API and generated-source baselines, collectible ALC, package builds,
190 reverse-compatible current-writer payloads read by the original reader,
NativeAOT publish/run and clean-tree checks also passed. The existing two legacy
Type-containing rejections and eleven fixture-local compatibility cases retain
their separate treatment.

Existing MessagePack 3.1.8 sandbox NU1902 and generic-Type IL2111 warnings remain.
Functional green does not imply warning-free acceptance or a performance pass.

## Measurement design

[Run 42012807073](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073)
passed all six performance/JIT jobs:

- [x64 legacy timings](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195806130)
- [ARM64 legacy timings](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195806238)
- [x64 selected-fixture timings](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195806164)
- [ARM64 selected-fixture timings](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195806328)
- [x64 JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195805781)
- [ARM64 JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42012807073/job/127195806235)

Both suites reuse their previous C# fixtures byte-for-byte. SDK 10.0.114 builds
one harness IL per suite against A; B and C replace only the Core/Streaming
runtime DLLs. Hashes, runtime flags, one-CPU affinity, payload identity and
identical per-case calibrated iteration counts are checked.

Each case has eight balanced ABC process triples, two B/B pairs, five samples
per process and two seconds of warmup through the same non-inlined batch
callsite. All process medians, allocation ranges, GC counts and execution order
are retained. No outlier is removed.

Legacy has ten cases and initializes all fixtures in its chosen group before
filtering. Its aggregate group reads reference graphs before timing Int32,
while its surface group need not have reference-table history. Selected has
eight cases and initializes only its chosen fixture. These suites have different
callback/setup shapes, so their timing difference does not isolate history
alone. Allocated empty tables remain non-null, making the helper potentially
hot after reference use.

Ratios use geometric means of eight paired ratios with bootstrap intervals.
These are exploratory within-host intervals, unadjusted for multiple comparisons,
not a universal performance budget or equivalence proof. Fixed warmup does not
prove tier convergence. The JIT probes below are separate processes.

## Timing results

All reported summary median thread B/op values match across A/B/C. This does
not mean every total-allocation sample is identical; some total-counter samples
have small fractional fluctuations.

### Legacy cases

| Case | x64 C/B [95% interval] | ARM64 C/B [95% interval] |
|---|---|---|
| Int32.raw | 1.3713 [1.3650,1.3770] | 1.2135 [1.2088,1.2185] |
| Int32.empty | 0.9587 [0.8211,1.0825] | 1.0902 [1.0866,1.0939] |
| Mixed.utf16 | 0.9478 [0.8676,1.0109] | 1.0134 [1.0078,1.0191] |
| References32.raw | 1.0004 [0.9884,1.0135] | 0.9832 [0.9607,1.0076] |
| References32.empty | 1.0070 [0.9874,1.0255] | 1.0008 [0.9944,1.0079] |
| BytesSpan64.empty | 1.0078 [0.9865,1.0264] | 1.0095 [1.0022,1.0176] |
| BytesSpan65536.empty | 1.0155 [0.9065,1.1115] | 1.0431 [1.0005,1.1150] |
| BytesSpan65536.custom | 1.0120 [0.9122,1.0995] | 0.9961 [0.9865,1.0046] |

The raw Int32 slowdown is small in absolute time but consistent in the eight
main paired processes: x64 B is 1.468–1.508 ns, C 2.009–2.040 ns; ARM64 B is
2.022–2.036 ns, C 2.446–2.480 ns. x64 also has a B/B control ratio of 2.5935,
which must not be hidden. ARM64 raw controls are much steadier at 1.0041 and
0.9986.

ARM64 empty-context Int32 moves from B 23.400–23.550 ns to C 25.361–25.768 ns.
x64 context and mixed-object rows have much wider distributions; their favorable
point estimates do not demonstrate a stable gain. For example, x64 context B
includes medians of 27.844 and 22.699 ns, with other B values near 16–17 ns.

Reference-heavy cases do not show a reliable benefit. The small-span ARM64
regression, absent dependable improvements elsewhere, adds to the decision not
to promote. Large-span and two-segment results retain substantial variability.
The writer control changes in opposite directions across architectures, despite
no writer source change; that is a further limitation on causal interpretation.

The earlier x64 64 KiB issue remains unresolved. This run's original-PR custom
Span B/A is 1.1224 [1.0306,1.2238], while candidate C/A is
1.1359 [1.0279,1.2433]. The helper is not an established fix.

### Selected-fixture cases and slow states

- Clean public Int32: x64 C/B 1.0215 [0.9894,1.0567]; ARM64
  0.9932 [0.9876,0.9983]. The approximately 0.68% ARM64 improvement still leaves
  C about 17.3% slower than dev in this shape and does not offset legacy risks.
- Manual initialized Int32: x64 C/B 1.0458 [0.9724,1.1975], ARM64
  1.0026 [0.9952,1.0091]. Seven of eight x64 C processes are faster than B, but
  one C median is 28.618 ns versus the other C values near 16.9–17.2 ns. Keep
  all eight; neither a stable improvement nor universal slowdown is established.
- ARM64 allocating custom 64 KiB has a C/B point estimate of 0.6615, but slow
  process medians appear once in A, three times in B and zero times in C.
  Those slow medians are about 5.8–6.4 microseconds versus typical values near
  2.1–2.3 microseconds. Whether the candidate changes the probability of that
  state is not established. Do not label it a reliable 34% gain or dismiss it
  automatically as pure noise.
- ARM64 empty allocating 64 KiB includes a B median of 7827 ns among typical
  values near 2.1 microseconds. Its C/B interval crosses 1.
- x64 pinned pure-copy control shows C/B 0.9148 [0.8779,0.9529], even though no
  serializer read is timed, with B/B controls 1.3413 and 1.4498. x64 pinned
  reader reuse has a B/B ratio of 1.5721. These controls reveal substantial
  variation in that host's large-array measurements.

The selected timing jobs retain 224 post-timing observation records each.
B/C ordinary reused output placement matches in their records, and pinned
destinations are modulo 64 = 0 and modulo 4096 = 192. These are snapshots after
timing, not a record of normal-array alignment during forced collections.
Matching snapshot placement does not explain or rule out the observed slow
states. Pinning also changes heap/GC behavior.

## JIT observations

Each architecture records 42 probe configurations. Each uses eight seconds of
warmup with tiering/PGO on, separately from the two-second timing processes.
Selected Perf listings must contain a Tier1 batch; zero selected Core listings
are allowed only for pure-copy controls. Raw dumps remain local to the runner;
only structured method/tier/size/opcode/symbolic-call records are retained.

| Tier1 batch | x64 B/C bytes | ARM64 B/C bytes |
|---|---:|---:|
| Legacy Int32.empty | 1391 / 1298 | 1656 / 1536 |
| Legacy References32.empty | 2583 / 2728 | 3244 / 3356 |
| Legacy BytesSpan65536.empty | 2129 / 2129 | 2480 / 2480 |
| Legacy BytesSpan65536.custom | 2050 / 2050 | 2316 / 2316 |
| Selected DeserializeInt | 1268 / 1175 | 1492 / 1372 |
| Selected ReadAlloc.empty.65536 | 2244 / 2244 | 2552 / 2552 |

Int32 batch code shrinks, but the timing results do not demonstrate a general
benefit. Reference batch code grows. x64 Int32 C listings include a
ResetReferences call site, but that does not prove the helper runs every
operation: the clean selected probe has that static site without a helper
Tier1 listing.

For 64 KiB reads, B/C caller size/instruction/opcode/call summaries match, while
the standalone Reset callee changes. In selected allocating reads, x64 Reset
shrinks from B 170 bytes to C 85, and ARM64 from 216 to 100. Therefore equal
caller summaries do not establish equal work or latency.

Nor does a smaller Reset body prove the total cleanup code shrinks: in x64
References32, B Reset is 360 bytes; C Reset 85 plus helper 277 totals 362.
Indirect ARM64 call targets lack reliable symbols. Static call-site counts
include fallback, exception and unrolled paths and are not calls per operation.

The pure-copy control's ARM64 B/C batch has the same 580 bytes and 145
instructions but different opcode distributions. Matching sizes or bounded
summaries are not proof of identical machine code. Separate JIT probes do not
establish the exact tier/code that every timed sample used.

## Raw Int32 follow-up

A separate [JIT-only follow-up 42015291959](https://github.com/SunSi12138/SharpPack/actions/runs/42015291959)
passed both architectures, six configurations each:

- [x64](https://github.com/SunSi12138/SharpPack/actions/runs/42015291959/job/127204244034)
- [ARM64](https://github.com/SunSi12138/SharpPack/actions/runs/42015291959/job/127204243594)

Instrumentation commit
[3fbe76f1687e97163ca660679a904df1652baaae](https://github.com/SunSi12138/SharpPack/commit/3fbe76f1687e97163ca660679a904df1652baaae)
checks out C explicitly at ff724136 and uses its unchanged legacy harness.
It adds no production change and reruns no timing matrix.

The raw production unmanaged path bypasses reader state and Reset. However,
the harness ReadInt closure contains both null-context and non-null-context
branches. The Tier1 raw probe retains code and static calls for the context
branch, so changing Reset can change the shared wrapper even when the selected
value has no context.

| Raw Int32 Tier1 method | x64 B/C bytes | ARM64 B/C bytes |
|---|---:|---:|
| ReadInt wrapper | 847 / 852 | 1232 / 1208 |
| RunBatch | 991 / 1059 | 1328 / 1364 |

On x64, C's wrapper/batch contains ResetReferences and a local code-label call
site, while B contains Reset call sites. The summaries do not identify every
exception-region relationship, so no specific local-label role is asserted.
These are code-generation observations, not evidence that raw deserialization
executes the helper per operation.

The follow-up supports a concrete code-layout/inlining hypothesis for this
shared wrapper. It does not establish the dynamic executed path, exact original
timing tier, or a causal explanation for the slowdown. New probes are separate
processes and hosts; no cross-job absolute timing comparison is made. The
decision not to promote does not depend on resolving this remaining cause.

## Review, retained evidence and boundaries

Independent review passed the production transformation, eleven regression
tests, both unchanged fixture programs, workflow/driver logic and twenty Python
statistics/JIT/design controls. Result review independently supports preserving
this as an unsuccessful experiment rather than promoting it, including all
slow-state and causality limitations above.

Earlier evidence remains available:

- [State/copy decomposition](https://github.com/SunSi12138/SharpPack/blob/a08b2fe9a04f9acb99ba019cb976186cd09756a0/docs/validation/pr30-state-copy-20261105.md)
- [Unsuccessful Init-only candidate](https://github.com/SunSi12138/SharpPack/blob/8442512985cbc17e0f45e30fde6a52eaef7e8822/docs/validation/pr30-init-inline-20261105.md)
- [Initial performance acceptance investigation](https://github.com/SunSi12138/SharpPack/blob/7c58350edba61d5715ff5e7fc25e0a661d333d98/docs/validation/pr30-performance-20261105.md)

No original PR update, merge, release, deletion, permission change or raw-log
artifact upload was performed. No unsupported performance fix is proposed for
integration. Further optimization should start from original B, preserve all
limit/reset semantics, and use whole-path measurements rather than assuming
smaller reset code must be faster.

## Complete comparison tables


### x64 legacy

| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls |
|---|---:|---:|---:|---:|---:|
| Int32.raw | 0.9638 [0.8874,1.0081] | 1.3216 [1.2188,1.3796] | 1.3713 [1.3650,1.3770] | 0.000/0.000/0.000 | 1.0069/2.5935 |
| Int32.empty | 1.1588 [1.0294,1.3339] | 1.1109 [1.0782,1.1411] | 0.9587 [0.8211,1.0825] | 0.000/0.000/0.000 | 0.9522/0.7971 |
| Mixed.utf16 | 1.2344 [1.1525,1.3488] | 1.1699 [1.1574,1.1825] | 0.9478 [0.8676,1.0109] | 368.000/368.000/368.000 | 0.9963/0.9625 |
| References32.raw | 1.0213 [1.0094,1.0322] | 1.0217 [1.0088,1.0339] | 1.0004 [0.9884,1.0135] | 768.000/768.000/768.000 | 1.0320/1.0351 |
| References32.empty | 1.0272 [1.0004,1.0536] | 1.0343 [1.0127,1.0539] | 1.0070 [0.9874,1.0255] | 768.000/768.000/768.000 | 0.9802/0.9719 |
| BytesSpan64.empty | 1.0626 [1.0467,1.0791] | 1.0709 [1.0604,1.0817] | 1.0078 [0.9865,1.0264] | 88.000/88.000/88.000 | 1.0224/1.0108 |
| BytesSequence2x64.empty | 1.0515 [1.0119,1.0936] | 1.1142 [1.0657,1.1638] | 1.0596 [0.9912,1.1336] | 120.000/120.000/120.000 | 1.0933/0.9961 |
| BytesSpan65536.empty | 1.0916 [0.9352,1.2094] | 1.1085 [0.9645,1.2442] | 1.0155 [0.9065,1.1115] | 65560.000/65560.000/65560.000 | 0.8695/0.7189 |
| BytesSpan65536.custom | 1.1224 [1.0306,1.2238] | 1.1359 [1.0279,1.2433] | 1.0120 [0.9122,1.0995] | 65560.000/65560.000/65560.000 | 0.9879/1.1119 |
| WriteBuffer64.empty | 1.0444 [1.0293,1.0575] | 1.0698 [1.0472,1.0932] | 1.0243 [1.0022,1.0412] | 0.000/0.000/0.000 | 0.9390/0.9990 |

### ARM64 legacy

| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls |
|---|---:|---:|---:|---:|---:|
| Int32.raw | 1.0024 [1.0006,1.0045] | 1.2164 [1.2126,1.2206] | 1.2135 [1.2088,1.2185] | 0.000/0.000/0.000 | 1.0041/0.9986 |
| Int32.empty | 1.1697 [1.1667,1.1732] | 1.2752 [1.2708,1.2807] | 1.0902 [1.0866,1.0939] | 0.000/0.000/0.000 | 1.0277/0.9976 |
| Mixed.utf16 | 1.0735 [1.0560,1.0892] | 1.0879 [1.0685,1.1050] | 1.0134 [1.0078,1.0191] | 368.000/368.000/368.000 | 1.0009/1.0094 |
| References32.raw | 1.0515 [1.0346,1.0696] | 1.0339 [1.0206,1.0512] | 0.9832 [0.9607,1.0076] | 768.000/768.000/768.000 | 0.9971/1.0423 |
| References32.empty | 1.0303 [1.0210,1.0388] | 1.0311 [1.0266,1.0357] | 1.0008 [0.9944,1.0079] | 768.000/768.000/768.000 | 1.0050/0.9950 |
| BytesSpan64.empty | 1.0243 [1.0166,1.0326] | 1.0340 [1.0222,1.0457] | 1.0095 [1.0022,1.0176] | 88.000/88.000/88.000 | 1.0003/1.0026 |
| BytesSequence2x64.empty | 1.1335 [1.0746,1.1941] | 1.1847 [1.0954,1.2689] | 1.0451 [0.9739,1.1134] | 120.000/120.000/120.000 | 1.0101/1.1430 |
| BytesSpan65536.empty | 0.9960 [0.9792,1.0122] | 1.0389 [0.9986,1.1077] | 1.0431 [1.0005,1.1150] | 65560.000/65560.000/65560.000 | 1.0090/0.9964 |
| BytesSpan65536.custom | 1.0033 [0.9983,1.0092] | 0.9993 [0.9923,1.0051] | 0.9961 [0.9865,1.0046] | 65560.000/65560.000/65560.000 | 0.9906/1.0132 |
| WriteBuffer64.empty | 1.0280 [1.0158,1.0388] | 1.0152 [0.9989,1.0268] | 0.9875 [0.9801,0.9942] | 0.000/0.000/0.000 | 0.9779/1.0020 |

### x64 selected

| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls |
|---|---:|---:|---:|---:|---:|
| InitResetExit | 1.0604 [0.9993,1.1334] | 1.0250 [0.9540,1.1150] | 0.9666 [0.9332,1.0000] | 0.000/0.000/0.000 | 0.9394/0.9671 |
| ManualReadInt | 1.1438 [1.1278,1.1595] | 1.1962 [1.1148,1.3616] | 1.0458 [0.9724,1.1975] | 0.000/0.000/0.000 | 0.9995/1.0002 |
| DeserializeInt | 1.1097 [1.0764,1.1400] | 1.1335 [1.1103,1.1649] | 1.0215 [0.9894,1.0567] | 0.000/0.000/0.000 | 1.0164/1.0030 |
| ReadAlloc.empty.65536 | 0.9983 [0.9506,1.0494] | 1.0317 [0.9893,1.0794] | 1.0334 [0.9761,1.0897] | 65560.000/65560.000/65560.000 | 1.1565/0.9406 |
| ReadReuse.empty.normal.65536 | 1.0029 [1.0025,1.0032] | 1.0057 [1.0031,1.0097] | 1.0028 [1.0004,1.0067] | 0.000/0.000/0.000 | 0.9966/0.9992 |
| ReadAlloc.custom.65536 | 1.0080 [0.9771,1.0392] | 0.9924 [0.9570,1.0272] | 0.9845 [0.9351,1.0365] | 65560.000/65560.000/65560.000 | 1.0953/1.0469 |
| ReadReuse.empty.pinned0.65536 | 0.9532 [0.8942,1.0171] | 1.0200 [0.9647,1.0712] | 1.0700 [1.0043,1.1451] | 0.000/0.000/0.000 | 1.5721/1.0030 |
| CopyReuse.raw.pinned0.65536 | 1.0261 [0.9235,1.1391] | 0.9387 [0.8693,1.0168] | 0.9148 [0.8779,0.9529] | 0.000/0.000/0.000 | 1.3413/1.4498 |

### ARM64 selected

| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls |
|---|---:|---:|---:|---:|---:|
| InitResetExit | 1.1009 [1.0950,1.1065] | 1.1059 [1.0989,1.1134] | 1.0045 [0.9998,1.0091] | 0.000/0.000/0.000 | 0.9936/0.9729 |
| ManualReadInt | 1.1414 [1.1343,1.1475] | 1.1444 [1.1368,1.1516] | 1.0026 [0.9952,1.0091] | 0.000/0.000/0.000 | 1.0082/0.9954 |
| DeserializeInt | 1.1813 [1.1745,1.1879] | 1.1733 [1.1652,1.1826] | 0.9932 [0.9876,0.9983] | 0.000/0.000/0.000 | 1.0050/0.9826 |
| ReadAlloc.empty.65536 | 1.1822 [0.9913,1.6429] | 0.9940 [0.9654,1.0276] | 0.8408 [0.5993,1.0117] | 65560.000/65560.000/65560.000 | 1.0236/1.0526 |
| ReadReuse.empty.normal.65536 | 1.0086 [1.0006,1.0167] | 1.0203 [1.0036,1.0379] | 1.0115 [0.9989,1.0258] | 0.000/0.000/0.000 | 1.0038/0.9965 |
| ReadAlloc.custom.65536 | 1.2999 [0.7788,1.9946] | 0.8599 [0.6571,0.9944] | 0.6615 [0.4527,0.9539] | 65560.000/65560.000/65560.000 | 0.9792/1.1416 |
| ReadReuse.empty.pinned0.65536 | 1.0008 [0.9985,1.0040] | 1.0038 [1.0015,1.0060] | 1.0030 [0.9990,1.0067] | 0.000/0.000/0.000 | 0.9925/1.0071 |
| CopyReuse.raw.pinned0.65536 | 1.0006 [0.9999,1.0013] | 1.0010 [0.9999,1.0021] | 1.0004 [0.9997,1.0011] | 0.000/0.000/0.000 | 1.0009/0.9999 |
