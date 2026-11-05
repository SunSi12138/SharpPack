# PR #30 performance acceptance evidence

Status: both experiments completed successfully on x64 and ARM64. Performance acceptance remains open because the default runtime measurements retain regressions. A successful workflow is not a performance pass.

## Pinned relationship and prior evidence

- PR: https://github.com/SunSi12138/SharpPack/pull/30
- Candidate: `56113519bb9771fe33ec39daed59181024918228`.
- Current dev: `6666ada1d878da64b2f4d2c1107fbea30fad074f`, rechecked at 09:09 UTC on 2026-11-05.
- Common ancestor: `8efea5afc79eaefc5023806a2588c4ad2f09a9c6`. The PR is 5 commits ahead and 3 behind dev. The three dev commits change CI and sandbox MessagePack dependencies, not production source, shared build properties or SDK policy. Both workflows assert those paths are unchanged and assert identical generator trees.
- Existing exact-head functional evidence: https://github.com/SunSi12138/SharpPack/actions/runs/41588954630 . The PR records 654 passing tests on each architecture, API/generated/wire/ALC/package/NativeAOT checks. This work does not replace that evidence or claim it tested a combined future head.
- Prior limited x64 performance evidence: https://github.com/SunSi12138/SharpPack/actions/runs/41589130306 . That older SDK/runtime ABCCBA experiment is separate, including the warmed reusable BitArray writer's scoped zero-allocation result. Its reader-constructor-inlining candidate `974e7186` is not in PR #30. The unsafe original BitArray formatter was not executed again.

## Broad paired matrix

Harness commit: https://github.com/SunSi12138/SharpPack/commit/9ce22f061f1d063ce507e94966c5fbdd80f008e9

Run: https://github.com/SunSi12138/SharpPack/actions/runs/41995910039 . All four jobs succeeded; each job includes fixture checks, exact-hash checks, measurements and clean-checkout checks.

- x64 aggregate: https://github.com/SunSi12138/SharpPack/actions/runs/41995910039/job/127138884320
- ARM64 aggregate: https://github.com/SunSi12138/SharpPack/actions/runs/41995910039/job/127138884367
- x64 surface: https://github.com/SunSi12138/SharpPack/actions/runs/41995910039/job/127138885153
- ARM64 surface: https://github.com/SunSi12138/SharpPack/actions/runs/41995910039/job/127138883982

Coverage is documented in `README.md`: no-context, empty context, configured Utf16 and custom formatter contexts; 64 B through 1 MiB Span/sequence reads; reusable and allocating writes; primitive/generated/collection/depth/circular graphs; synchronous synthetic stream/Pipe paths; accepted limited payloads and separate untimed rejection controls. There are 53 common surface rows and 27 common aggregate rows, each including one harness-control row, plus 5 and 12 candidate-only limited rows respectively.

The same managed harness IL is used for both runtime revisions. SDK 10.0.114 is pinned. Every job runs eight alternating A/B process pairs and two A/A control pairs, one CPU affinity, identical per-case iteration counts, one-second warmup and five samples. Intervals resample eight paired log ratios; they are exploratory within-host intervals with no multiple-comparison adjustment. There is no validated equivalence threshold.

### Findings to retain

These are candidate/baseline time ratios, not percentages of overall application throughput.

| Case | x64 B/A | ARM64 B/A | Interpretation |
|---|---:|---:|---|
| Int32, empty context | 1.1066 [1.0704, 1.1434] | 1.1407 [1.0936, 1.1780] | All 8 pairs slower on each host, but small absolute operation and multimodal times. Focused confirmation follows. |
| 32-node circular graph, no context | 1.0462 [1.0363, 1.0545] | 1.0172 [1.0035, 1.0279] | Retain the paired signal; focused confirmation follows. |
| 64 B, two-segment sequence, no context | 1.0469 [1.0241, 1.0706] | 1.0406 [1.0140, 1.0744] | Both hosts slower; same-case A/A controls are near 1. |
| 64 B, two-segment sequence, empty context | 1.0783 [1.0395, 1.1187] | 1.0754 [1.0418, 1.1109] | Same direction on both hosts, 7/8 pairs slower each. |
| 64 B, 31-byte segmentation, empty context | 1.0921 [1.0714, 1.1142] | 1.0974 [1.0793, 1.1207] | All 8 pairs slower each; useful signal despite some shared modes. |
| 64 KiB Span, empty context | 1.1549 [1.1348, 1.1775] | 1.0197 [1.0069, 1.0328] | Clear x64-host signal, all 8 pairs slower; not an architecture-independent 15% claim. |
| 64 KiB Span, custom formatter | 1.1567 [1.1335, 1.1766] | 1.0279 [1.0051, 1.0570] | Retain the x64-host signal rather than averaging it away. |

Independent review agrees these signals should be retained, not dismissed wholesale because other cases are noisy. Synchronized A/B timing modes can still contain useful paired evidence.

### Allocation observations and limits

All 27 aggregate report rows and all non-1 MiB surface rows have identical A/B summary median B/op at the report's precision. This includes harness-control rows. Reusable buffer/destination writes report 0 B/op under these warmed conditions. It is not a universal zero-allocation claim.

The 1 MiB Span rows have equal thread-allocation medians but floating total-allocation counts; 1 MiB sequence rows also show small thread-allocation differences. Their cause has not been established. Do not silently classify all differences as LOH or measurement noise, and do not say every tested allocation count is identical.

### What the first matrix cannot establish

- Many absolute timings are multimodal. x64 Int32 raw A/A reaches 4.6243; some other controls and 1 MiB timings vary materially. A single fixed warmup does not prove tier convergence.
- Candidate limited cases always run after common cases. Their apparently large speedups are confounded by position and cannot establish that enabling limits improves speed.
- Synthetic Pipe timing includes Pipe construction, copy and cleanup. Synthetic async operations are required to complete synchronously; real asynchronous transport latency is not measured.
- Rejection controls check behavior and destination/read preservation, not a quantitative memory-allocation budget.
- Input buffers are aligned; output allocation layout and host effects remain. The harness is not BenchmarkDotNet and does not cover every original serializer benchmark or every production workload.

## Focused confirmation

Harness commit: https://github.com/SunSi12138/SharpPack/commit/fa115eb7386921aa506c5a492b04dab9967e2950

Run: https://github.com/SunSi12138/SharpPack/actions/runs/41999545245 . All four jobs succeeded.

- x64 tier ON: https://github.com/SunSi12138/SharpPack/actions/runs/41999545245/job/127151005031
- ARM64 tier ON: https://github.com/SunSi12138/SharpPack/actions/runs/41999545245/job/127151005018
- x64 tier OFF: https://github.com/SunSi12138/SharpPack/actions/runs/41999545245/job/127151004840
- ARM64 tier OFF: https://github.com/SunSi12138/SharpPack/actions/runs/41999545245/job/127151004635

Eight selected cases run one measured case per process through the same non-inlined batch function for two-second warmup, calibration and measurement. Matched candidate-limit variants use balanced A/B/C order. All eight paired medians and absolute A/A controls are retained. Tiered compilation/PGO ON and OFF have separate jobs on each architecture; those jobs may use different physical hosts. Their absolute times cannot isolate tiering causality. All fixtures initialize before case filtering, and the changed protocol is not a single-factor explanation of the broad run.

### Focused results

Default runtime mode (tiered compilation and PGO ON):

| Case | x64 B/A | ARM64 B/A |
|---|---:|---:|
| Int32, empty context | 1.1144 [1.0629, 1.1647] | 1.1626 [1.1544, 1.1701] |
| Mixed generated object, Utf16 | 1.0806 [1.0621, 1.1010] | 1.0803 [1.0643, 1.0940] |
| 32-node circular graph, no context | 1.0314 [1.0135, 1.0508] | 1.0345 [1.0212, 1.0514] |
| 32-node circular graph, empty context | 1.0499 [1.0236, 1.0829] | 1.0296 [1.0241, 1.0353] |
| 64 B Span, empty context | 1.0496 [1.0265, 1.0691] | 1.0303 [1.0191, 1.0406] |
| Depth 32, empty context | 0.9967 [0.9767, 1.0113] | 1.0072 [0.9986, 1.0148] |

The context Int32 and Utf16 mixed-object signals remain after changing the measurement protocol and on both architectures. ARM64 Int32 empty context has stable individual process medians around 20 ns baseline versus 23.3 ns candidate and near-unity A/A controls. The x64 absolute difference is around 2 ns. These are scoped operation costs, not a claim about end-to-end application throughput.

Raw Int32 and 64 B two-segment sequence results remain noisy. In the latter, default-mode A/A reaches 1.61x on x64 and 1.40x on ARM64, and absolute medians span approximately 400–680 ns. Do not call the x64 B/A of 0.8283 a speedup or say the first sequence concern is resolved.

With tiering disabled, Int32 empty context remains slower on x64 (+5.61%, interval +3.10% to +7.87%) and ARM64 (+9.92%, interval +8.90% to +10.93%). Other OFF-mode findings are architecture-specific: x64 depth +4.17% and raw references +2.86%; ARM64 small Span +5.85% and two-segment sequence +1.42%. The ARM64 mixed-object ratio is +4.52%, but one A/A ratio is 1.15. OFF is a diagnostic configuration, not evidence that the default runtime passes, and ON versus OFF differences cannot be assigned causally to tiering across different hosts.

All eight focused cases have equal A/B summary median thread B/op, and their configured-limit variants match at that precision. Small background total-allocation excursions remain visible in retained ranges. Balanced C/B comparisons no longer show the first matrix's implausible large limited-mode improvements. Most intervals include 1; a few ARM64 small differences are below 1%. This is not a proof that all configured-limit overhead is exactly zero.

### Acceptance decision and bounded next work

Do not mark the performance gate complete or describe the candidate as having no default-path regression. Independent review agrees the Int32/context, Utf16 mixed-object and reference signals deserve source/JIT diagnosis rather than being dismissed as harness artifacts.

The next scoped investigation should prioritize the context initialization/reset and reader construction/payload-limit path, using small one-change experiments that preserve every limit and exception-order guarantee. Capture method-specific JIT/code-size evidence to distinguish added setup/check work from code layout or inlining effects. Do not assume a constructor inlining tweak fixes this; the previous experiment did not prove that.

The 64 KiB x64 Span empty/custom signal is not included among the eight focused cases and remains a separate open confirmation target. The noisy two-segment case needs tier/convergence observation before interpreting speed changes. Broader host/workload repetition and a documented acceptable regression budget are needed before a general performance acceptance decision. No synthetic transport test replaces real asynchronous transport evidence.

No production source or PR metadata has been changed. No merge, release, deletion, permission change, or raw-log/artifact upload was performed.
