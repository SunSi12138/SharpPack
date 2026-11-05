# PR #30: bounded callback-shape diagnostic (2026-11-05)

## Decision

The diagnostic changes confidence in the **measurement mechanism**, not the integration decision.

- On the ARM64 host, separating the raw callback reduces the rejected Reset-helper candidate's C/B effect from **1.2030 [1.1906, 1.2169]** to **1.0041 [0.9897, 1.0188]**. All four predeclared diagnostic conditions pass.
- On the x64 host, the direction agrees, but two separate-raw B/B controls are **1.1039 and 1.1166**, beyond the predeclared 1.10 limit. The strong-support rule therefore **fails on x64**. There is no strong cross-architecture result and no post-hoc relaxation of the rule.
- The original PR's separate empty-context B/A effect remains **1.1476 [1.1385, 1.1590]** on x64 and **1.1770 [1.1653, 1.1867]** on ARM64. This callback change does not remove the original context signal.
- C remains slower than B in the separate empty-context case: **1.0349 [1.0216, 1.0452]** on x64 and **1.0746 [1.0691, 1.0803]** on ARM64. The Reset-helper candidate remains unselected.
- Same-process JIT output did **not** establish which tier executed during timing. Complete Tier1 listings were visible only in the final file read after process exit, not in either timed-window snapshot. Output buffering and code-version transitions remain unresolved.

Accordingly, #30's performance acceptance remains unresolved. This four-case diagnostic does not establish a library acceptance budget, resolve the 64 KiB observations, or justify a production optimization. It supplies a concrete reason not to interpret the earlier large raw-C slowdown as a universal cost of Reset itself.

## Exact provenance and scope

One dual-architecture run completed successfully:

- [Run 42019395403](https://github.com/SunSi12138/SharpPack/actions/runs/42019395403)
- [x64 job 127218069275](https://github.com/SunSi12138/SharpPack/actions/runs/42019395403/job/127218069275)
- [ARM64 job 127218069629](https://github.com/SunSi12138/SharpPack/actions/runs/42019395403/job/127218069629)
- [Frozen diagnostic source e57ddcef](https://github.com/SunSi12138/SharpPack/tree/e57ddcef8b1017a330c48075d67cd065f0449485/tools/ReadLimitsCallbacks)
- [Predeclared protocol and stopping rule](https://github.com/SunSi12138/SharpPack/blob/e57ddcef8b1017a330c48075d67cd065f0449485/tools/ReadLimitsCallbacks/README.md)
- [Exact workflow](https://github.com/SunSi12138/SharpPack/blob/e57ddcef8b1017a330c48075d67cd065f0449485/.github/workflows/read-limits-callbacks.yaml)

At 11:44 UTC on 2026-11-05, dev and PR #30 still pointed to the A and B revisions below; PR #30 was open and unmerged. The runtime comparisons are frozen, not whatever a moving branch contains later:

| Side | Revision | Purpose |
|---|---|---|
| A | 6666ada1d878da64b2f4d2c1107fbea30fad074f | dev baseline |
| B | 56113519bb9771fe33ec39daed59181024918228 | original PR #30 |
| C | ff724136dafdd0c1b9a4c98b1ddf115ca81b860a | previously rejected Reset helper, diagnostic contrast only |

The diagnostic commit adds only its harness, tests, protocol and workflow. The workflow verifies immutable production heads, unchanged production/settings relative to the appropriate baseline, identical generator trees, exact SDK 10.0.114, loaded assembly hashes, one harness IL per architecture, CPU affinity, payload/count identity, and clean final source checkouts. Runtime metadata reports .NET 10.0.14, tiered compilation and tiered PGO enabled. The same harness IL is copied between A/B/C; only the exact runtime assemblies change.

No new production source, original PR update, broad performance matrix, merge, release, deletion or permission change was made. The prior exact-head functional baseline is retained, not rerun or replaced: [original PR functional run](https://github.com/SunSi12138/SharpPack/actions/runs/41588954630) and [Reset candidate functional run](https://github.com/SunSi12138/SharpPack/actions/runs/42012806804). The latter passed 665 tests per architecture and its existing API, generated-code, ALC, package, wire and AOT gates. These callback jobs are diagnostics, not another full functional acceptance run.

## What changed in the measurement

The shared callback retains the original conditional body: choose the raw or context serializer entry inside the callback. The separate callbacks directly call one entry each; callback selection happens before timing.

All four callbacks and the earlier aggregate fixtures, including reference-graph setup history, initialize in every process before selecting one case. This preserves setup context while deliberately changing callback shape. Adding methods necessarily changes the managed harness and compiler-generated layout. The experiment therefore does not isolate one machine branch, one instruction, or one cache effect.

The four cases are interleaved within eight blocks. Case order is mirrored/rotated, and A/B/C order is pairwise balanced. Each case has eight paired A/B/C triples, two B/B and two C/C pairs, five samples per process, a two-second warmup through the same non-inlined RunBatch, and one calibrated iteration count shared across A/B/C. Each architecture runs 140 processes including calibration, of which 128 are timed. All samples, slow states and JIT observations remain in the main statistics.

The intervals bootstrap eight paired log ratios; the reported effects are geometric means. These are exploratory, within-host intervals, unadjusted for multiple comparisons. They are not cross-host or production-population guarantees. The two control pairs per binary and shape are useful diagnostics but do not characterize all noise.

## Predeclared rule and its outcome

Strong support for reduction of the earlier greater-than-20% raw-C signal requires all four conditions per architecture:

1. Shared raw C/B lower 95% interval > 1.10.
2. The block-paired shared/separate C/B ratio-of-ratios lower interval > 1.
3. Separate raw C/B upper interval < 1.10.
4. Every B/B and C/C ratio for both raw shapes lies within 0.9–1.1.

| Condition | x64 | ARM64 |
|---|---|---|
| Shared raw lower bound > 1.10 | Pass: 1.1058 | Pass: 1.1906 |
| Interaction lower bound > 1 | Pass: 1.1229 | Pass: 1.1714 |
| Separate raw upper bound < 1.10 | Pass: 0.9935 | Pass: 1.0188 |
| All relevant controls within 0.9–1.1 | **Fail: B/B 1.1039, 1.1166** | Pass |
| Overall | **No strong support by the rule** | **Strong support on this host/run** |

The 10% boundary was committed before the results. It is a diagnostic size criterion for the previously large signal, not an acceptable library regression, proof of equivalence, or permission to ignore smaller effects. No threshold or run count changed after seeing results.

## Timing results

All thread-allocation medians are 0 B/op. No measured GC events occurred in the reported main-series samples. Full process medians, control times, allocation ranges, environment hashes and execution order are preserved in the linked numeric summaries.

### x64

| Case | B/A [95%] | C/A [95%] | C/B [95%] | B/B controls | C/C controls |
|---|---:|---:|---:|---:|---:|
| Shared raw | 1.0442 [1.0373,1.0504] | 1.1619 [1.1599,1.1637] | 1.1127 [1.1058,1.1212] | 1.0158 / 0.9821 | 1.0003 / 0.9967 |
| Separate raw | 1.0270 [1.0046,1.0530] | 0.9927 [0.9637,1.0140] | 0.9666 [0.9380,0.9935] | **1.1039 / 1.1166** | 0.9918 / 1.0828 |
| Shared empty context | 1.1194 [1.1016,1.1383] | 1.1519 [1.1396,1.1631] | 1.0290 [1.0141,1.0417] | 0.9463 / 0.9900 | 0.9833 / 1.0016 |
| Separate empty context | 1.1476 [1.1385,1.1590] | 1.1876 [1.1829,1.1921] | 1.0349 [1.0216,1.0452] | 1.0000 / 0.9883 | 1.0306 / 0.9923 |

Shared/separate raw C/B interaction: **1.1512 [1.1229,1.1818]**.
Shared/separate original B/A context interaction: **0.9754 [0.9578,0.9934]**.

Separate-raw B controls include approximately 1.524 -> 1.683 ns and 1.490 -> 1.664 ns. The main separate-raw A/B/C distributions also span several timing levels. Do not label the apparent C/B improvement as a stable production win.

### ARM64

| Case | B/A [95%] | C/A [95%] | C/B [95%] | B/B controls | C/C controls |
|---|---:|---:|---:|---:|---:|
| Shared raw | 0.9984 [0.9911,1.0064] | 1.2011 [1.1914,1.2126] | 1.2030 [1.1906,1.2169] | 0.9994 / 0.9992 | 0.9868 / 0.9969 |
| Separate raw | 0.9951 [0.9805,1.0099] | 0.9992 [0.9987,0.9997] | 1.0041 [0.9897,1.0188] | 0.9999 / 0.9996 | 0.9991 / 1.0398 |
| Shared empty context | 1.1581 [1.1491,1.1686] | 1.2526 [1.2431,1.2624] | 1.0816 [1.0759,1.0873] | 0.9985 / 0.9924 | 1.0035 / 0.9928 |
| Separate empty context | 1.1770 [1.1653,1.1867] | 1.2648 [1.2561,1.2727] | 1.0746 [1.0691,1.0803] | 1.0122 / 0.9996 | 1.0055 / 1.0009 |

Shared/separate raw C/B interaction: **1.1981 [1.1714,1.2295]**.
Shared/separate original B/A context interaction: **0.9839 [0.9703,0.9983]**.

For scale only, arithmetic means of the eight paired differences, computed from process medians rounded to 0.001 ns, are:

| Architecture / case | Approx. B - A, ns/op | Approx. C - B, ns/op |
|---|---:|---:|
| x64 shared raw | +0.077 | +0.206 |
| x64 separate raw | +0.042 | -0.054 |
| x64 shared context | +1.676 | +0.453 |
| x64 separate context | +1.962 | +0.531 |
| ARM64 shared raw | -0.004 | +0.412 |
| ARM64 separate raw | -0.008 | +0.007 |
| ARM64 shared context | +3.198 | +1.911 |
| ARM64 separate context | +3.540 | +1.756 |

These descriptive absolute deltas are not confidence intervals or an additive decomposition of state, field, guard and callback costs. JIT observation is enabled here, so absolute times should not be directly compared with earlier observation-disabled jobs.

## Same-process JIT observation: useful negative result

Every timing process enabled only the RunBatch disassembly filter and used a unique local output file. Exactly two file-length snapshots were taken outside timing: after warmup and after all five samples. There was no output polling, extra forced GC, waiting for a listing, or warmup extension.

The parser uses byte prefixes and requires a complete matching method listing including the entire footer line. It records method identity, tier, OSR status, multiple versions and bounded opcode/symbolic-call summaries. The summary identifiers hash those bounded summaries, not machine code.

On **each architecture**, all 128 timed processes had:

- Equal before/after byte lengths, at 4 KiB boundaries.
- Two complete Instrumented Tier0 listings before timing.
- One incomplete listing at each snapshot.
- No newly complete listing between the two snapshots.
- One complete non-OSR Tier1 listing available only in the final file after process exit.
- No incomplete listing in that final file.

Thus zero main processes had a complete non-OSR Tier1 listing visible before timing. The numeric tables report zero parse-status “unknown” because all final files parsed, **not** because the executing tier is known. The two snapshots establish only that neither contained a complete Tier1 listing; completeness was confirmed in the file read after exit. Buffered output is consistent with these observations, but they do not identify when compilation or flushing occurred. It is invalid to infer either “timing ran Tier0” or “timing definitely ran the final Tier1.”

The final Tier1 summaries show a shape-sensitive pattern:

| Architecture / final RunBatch shape | A code bytes | B code bytes | C code bytes |
|---|---:|---:|---:|
| x64 shared raw | 897 / 901 | 978 / 982 | 1046 / 1050 |
| x64 separate raw | 359 / 360 / 375 | 359 / 360 / 375 | 359 / 360 / 375 |
| ARM64 shared raw | 1200 / 1204 | 1328 / 1332 | 1364 / 1368 |
| ARM64 separate raw | 492 / 500 | 492 / 500 | 492 / 500 |

Across the eight main processes per side, the separate-raw bounded-summary variant sets are shared by A/B/C on each architecture. This is compatible with a callback-shape mechanism. Equal summaries are not proof of identical complete machine code, and the final listings are not proof of what executed in a timed sample. Branch layout, delegate profiling, inlining, code placement, tier transitions and unobserved callees are not separately identified.

## Evidence, review and stopping point

Persisted outputs contain only numeric metadata and bounded symbolic summaries:

- [x64 timing, controls, allocations, visibility counts and process order](pr30-callbacks-20261105/x64.md)
- [ARM64 timing, controls, allocations, visibility counts and process order](pr30-callbacks-20261105/arm64.md)
- [x64 per-process visibility and bounded JIT summaries](pr30-callbacks-20261105/jit-x64.json)
- [ARM64 per-process visibility and bounded JIT summaries](pr30-callbacks-20261105/jit-arm64.json)

Raw subprocess logs and instruction dumps stayed in temporary runner storage. No raw logs or CI artifacts were uploaded. Five samples from every process were included in its median; published tables retain every timed-process median rather than all raw samples.

The protocol, source, driver, workflow and visibility-parser fix were independently reviewed before execution. The final source has 29 local tests, all passing, and those same tests passed in both hosted jobs. **Independent result/report review is complete.** It confirmed the ARM64-only strong-support result, the x64 control failure, the continuing context signal, and the unresolved association between JIT output and timed execution.

The earlier [performance decision](https://github.com/SunSi12138/SharpPack/blob/52b610af5b34f589ced18c2b9ee4ccdcc37d3a3b/docs/validation/pr30-performance-decision-20261105.md), [Reset-helper rejection](https://github.com/SunSi12138/SharpPack/blob/0e71a4a0a6377e6b28759a8c13460feb377bf9ee/docs/validation/pr30-reset-helper-20261105.md), and [state/copy limitations](https://github.com/SunSi12138/SharpPack/blob/a08b2fe9a04f9acb99ba019cb976186cd09756a0/docs/validation/pr30-state-copy-20261105.md) remain preserved.

Stop after this single pass and its independent review. No additional timing or production candidate is triggered by the x64 control failure or the unresolved executing tier. Any future acceptance effort needs an explicitly scoped decision about the remaining context cost and representative workloads; this diagnostic does not silently set that budget.

