# PR #30 reader initialization investigation

## Decision

Do not promote the `Init` inlining experiment as a performance fix. It preserves
the tested behavior, but the three-way measurements show no stable improvement
to the main context-path regressions. The original PR and all prior evidence
remain unchanged.

## Pinned revisions and scope

- A: current dev `6666ada1d878da64b2f4d2c1107fbea30fad074f`.
- B: original PR #30 `56113519bb9771fe33ec39daed59181024918228`.
- C: experimental candidate `f50b9fd28673fdde87c7357dbce5fc1115478c94`,
  branch `validation/pr30-init-inline-20261105`.
- JIT instrumentation repair: `63b8a7da2379535c0c55545a7f6556efb4d4b5bd`,
  branch `validation/pr30-init-jit-fix-20261105`. Its workflow still measures C
  frozen at `f50b9fd`; it does not change production code or rerun timings.

The only production change from B to C is an `AggressiveInlining` attribute on
`SharpPackReaderOptionalState.Init`. The workflow verifies this exact textual
change and that no other file under `src` differs. No limit check, exception
ordering, reference counting, reset behavior, public API or wire rule is removed.

Source review ruled out two hypotheses: limits are normalized when the context
is constructed, not for each operation, and the payload guard reads the cached
`MaxPayloadBytes` rather than copying the complete configuration. A changed
inlining decision was a plausible remaining hypothesis, not an established
root cause.

## Functional validation

[Exact candidate Build-Debug run 42003051783](https://github.com/SunSi12138/SharpPack/actions/runs/42003051783)
passed on x64 and ARM64:

- Each architecture: 654 passed, zero failed, zero skipped tests.
- Public API and generated-source baselines, collectible ALC sample, package
  build, NativeAOT publish/run and clean-tree checks passed.
- Both architectures read all 190 current-writer reverse-compatible payloads
  with the original reader. The 11 fixture-local and two legacy Type cases
  retain their existing separate treatment.
- Existing sandbox MessagePack 3.1.8 NU1902 and generic-Type analysis warnings
  are not fixed by this experiment. A successful workflow is not warning-free
  acceptance or permission to merge.

## Three-way default-runtime measurements

[Run 42003051853](https://github.com/SunSi12138/SharpPack/actions/runs/42003051853)
contains two successful performance jobs and two failed first-attempt JIT jobs.
Do not describe the overall run as green.

- [x64 performance](https://github.com/SunSi12138/SharpPack/actions/runs/42003051853/job/127162844022)
- [ARM64 performance](https://github.com/SunSi12138/SharpPack/actions/runs/42003051853/job/127162843932)

The same managed harness IL runs against three runtime variants. SDK 10.0.114,
binary hashes, one-CPU affinity, payload hashes, settings and identical per-case
iteration counts are checked. Each process measures one case after two seconds
of warmup through the same non-inlined batch function. Eight balanced ABC
triples and two same-binary B/B pairs retain every process median, allocation
range and GC count. All fixtures initialize before case filtering. Intervals
bootstrap eight paired log ratios and are exploratory, within-host and
unadjusted for multiple comparisons.

Selected candidate-versus-original-PR results (C/B; lower is faster):

| Case | x64 C/B [95% interval] | ARM64 C/B [95% interval] |
|---|---:|---:|
| Int32, empty context | 1.0247 [0.9852, 1.0752] | 1.0024 [0.9809, 1.0217] |
| Mixed generated object, Utf16 | 1.0157 [0.9900, 1.0546] | 1.0056 [1.0018, 1.0095] |
| Circular references, empty context | 0.9945 [0.9870, 1.0011] | 0.9924 [0.9873, 0.9979] |
| 64 B Span, empty context | 0.9913 [0.9662, 1.0173] | 1.0086 [1.0035, 1.0140] |
| 64 KiB Span, empty context | 1.0151 [0.9711, 1.0654] | 0.9895 [0.9818, 0.9995] |
| 64 KiB Span, custom formatter | 0.9823 [0.9421, 1.0248] | 1.0005 [0.9961, 1.0055] |

There is no consistent improvement in the primary Int32/context or Utf16 mixed
object signals. ARM64 reference-context reads show a small improvement of about 0.76%, while
Mixed and small Span become slightly slower. These trade-offs are not enough
to justify promoting a general fix, especially across many exploratory comparisons. All ten scenarios
have equal A/B/C summary median thread B/op at the reported precision; this is
not an assertion that every total-allocation sample is identical.

The original PR's performance concerns remain visible versus A:

- ARM64 Int32 empty context: B/A 1.1564 [1.1414, 1.1752]; C/A 1.1592.
- Mixed Utf16: B/A 1.0819 on x64 and 1.0736 on ARM64; C/A 1.0989 and 1.0797.
- x64 64 KiB Span empty/custom: B/A 1.1355 [1.0578, 1.2141] and
  1.1799 [1.1446, 1.2138]. This independently preserves the earlier x64 signal,
  without establishing a universal architecture-independent percentage.

Noise remains material: x64 Int32 empty context has a B/B ratio of 1.6042 and
its B/A interval crosses 1 in this run. ARM64 64 KiB Span empty context has a
B/B ratio of 0.7520. Some sequence cases remain variable. Do not pick favorable
outliers or claim the small C/B differences establish equivalence.

## JIT evidence and instrumentation history

The first JIT jobs stopped because the runtime assembly filter did not restrict
the dump as expected and the summary's maximum method count was exceeded.
This was a diagnostic-script failure, not a production test failure. Partial
FullOpts summaries remain in the original run. Raw instruction dumps were not
uploaded.

The repair applies explicit method-name selection independently of the runtime
filter, prints bounded per-method records, and retains any available symbolic
comments. Six parser controls and eight statistics controls pass.

[Repaired JIT run 42004274894](https://github.com/SunSi12138/SharpPack/actions/runs/42004274894)
passed on both architectures, recording all 24 probe configurations each:

- [x64 JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42004274894/job/127166973372)
- [ARM64 JIT](https://github.com/SunSi12138/SharpPack/actions/runs/42004274894/job/127166973953)

JIT probes are separate processes with eight-second warmup, not the two-second
timing processes. They observe FullOpts, Tier0/instrumented Tier0 and Tier1
code. Their outcomes do not establish which tier every measured sample used.

On x64, the standalone `ReadInt` Tier1 wrapper contains a named `Init` call in
B and not in C. The attribute therefore changes that particular call site's
inlining. However, the same harness's hotter Tier1 `RunBatch` probe already
contains no named `Init` call in B. Its B/C code-size, opcode-count and call
summaries match for both selected shapes:

| Tier1 batch probe | A code bytes | B code bytes | C code bytes |
|---|---:|---:|---:|
| x64 Int32 empty context | 1233 | 1391 | 1391 |
| x64 Mixed Utf16 | 2675 | 2883 | 2883 |
| ARM64 Int32 empty context | 1464 | 1656 | 1656 |
| ARM64 Mixed Utf16 | 3044 | 3244 | 3244 |

Matching summaries are not proof of byte-identical machine code; operands and
control-flow details are not fully retained. ARM64 indirect calls are emitted
as register targets without symbolic names in these dumps. Do not infer
absence of an ARM64 `Init` call from an empty named-call list. More generally,
no explicit named call does not exclude every indirect/fallback path.

The B/A batch summaries show extra code and instructions, consistent with
additional state/guard work, but code-size growth alone does not identify the
hot cost or prove a causal decomposition. In FullOpts x64, baseline and PR both
have standalone `Init` calls; a blanket claim that the PR simply lost inlining
would be wrong. Removing one standalone call is not a demonstrated fix for the
measured warmed path.

## Remaining work and boundaries

Keep the attribute-only candidate as a recorded experiment rather than moving
it into PR #30. A more useful next investigation would separately quantify the
extra context-state initialization/reset stores, reader limit loads and payload
guard work at the actual timed entry. Do not remove checks or keep context state
across operations merely to improve a benchmark. Any broader fast path needs
new semantic review and reentrancy/custom-formatter/state-reset tests.

The new verification scripts initially lacked independent review because the
review tool returned `agent thread limit reached`; the production experiment
direction had independent source review. Once capacity became available, an
independent reviewer checked the three-way count/pair/control logic, six JIT
parser tests, result tables and JIT interpretation, and returned PASS. The
review supports not promoting the candidate and preserves the small mixed
trade-offs and all inference limits above.

No original PR update, merge, release, deletion, permission change or raw-log
artifact upload was performed. Existing evidence and validation branches are
preserved.
