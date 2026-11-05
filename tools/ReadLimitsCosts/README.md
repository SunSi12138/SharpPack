# State, reader and allocation/copy diagnosis

This experiment changes no production source. It compares exact current dev
`6666ada` and original PR #30 `56113519`, preserving the unsuccessful Init-only
inlining experiment and earlier performance evidence.

## Component probes

Each process builds only its selected fixture. Reflection binds internal Init,
ResetAndExit and payload-guard delegates during setup; there is no reflection
in the timed callback. Cases distinguish repeated Init, Init plus dirty
ResetAndExit every operation, and repeated already-clear public Reset. The
latter is not isolated dirty-reset cost. Reader constructors pass the reader by
ref to a non-inlined consumer before Dispose, avoiding extra struct copying.
Manual initialized Int32 read and the public context entry provide controls.

The payload guard exists only in the PR. GuardDiagnostic rows compare that
candidate's accepted guard against its same-signature noop in separate balanced
processes, for unlimited/default and exact accepted limits. Exact plus-one
rejection and PayloadBytes classification are checked outside timing. No
deserializer has a guard removed. The noop may be optimized differently; these
are diagnostic differences for this call shape, not mathematical lower bounds
or additive pieces of end-to-end deserialization time.

## Allocation/copy probes

Lengths 65504, 65536 and 65568 compare empty-context allocating reads, ordinary
array ref reuse, and matched GC.AllocateUninitializedArray plus
Unsafe.CopyBlockUnaligned controls. The 65536 cases also include no-context and
custom-formatter paths, and pinned reused outputs with data addresses modulo 64
equal to 0 or 32. Normal reused arrays are retained as distinct controls: pinning
changes heap/GC behavior, not only alignment.

Pinned selection is bounded to 64 allocations for both targets and revisions.
Only the selected array remains rooted for timed collections; unused candidates
are released. Ref reuse verifies instance identity, length and contents.
Matched copy controls use the same allocation/copy primitive as the production
reader, but are still not identical complete operations and cannot be blindly
subtracted from serializer timings.

The payload buffer is aligned to 64 bytes. Its byte-array body starts four bytes
later, so the actual copy source is normally modulo 64=4. Both buffer and data
offsets modulo 64/4096 are reported. A separate 64-operation observation pass
after timing records only address remainders, never pointers. Those observations
do not establish output alignment during the timed samples. Ordinary reused
arrays can move during the forced collections before samples.

If a difference disappears with reuse, this narrows investigation to allocation,
copy conditions and their interactions; it does not prove alignment is the root
cause. Neighboring sizes and the pure allocation/copy controls help distinguish
these possibilities without weakening any check.

## Protocol and limits

Four jobs cover component/copy groups on x64 and ARM64. They use one byte-identical
managed harness IL, exact SDK 10.0.114, verified loaded hashes and one-CPU affinity.
The workflow verifies unchanged production trees and generator source. Each case
has eight alternating process pairs and two same-binary right/right pairs,
two-second warmup through the same non-inlined batch callsite, five samples and
identical calibrated counts. All process medians, allocation/GC ranges, control
medians and post-timing observations are retained.

Intervals resample eight paired log ratios, with no multiple-comparison
adjustment or predeclared equivalence budget. This is not a complete performance
acceptance gate. Fixed warmup does not prove tier convergence; method-level
microprobes do not reproduce every optimizer or cache interaction in public
entry points. No outliers are removed. Raw subprocess logs stay in temporary
runner storage; no logs or artifacts are uploaded.
