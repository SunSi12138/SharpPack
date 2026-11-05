# Generated-code ABI

Generated code and the runtime are versioned independently when consumers use
NuGet packages. Consequently, every CLR-public member emitted code can reference
is a binary compatibility surface, including members marked
`EditorBrowsable(EditorBrowsableState.Never)`.

## Boundary and owner

The **Generator** owns the source shape, selected runtime calls, generated names,
and diagnostics. **SharpPack.Core** owns the signatures and semantics of runtime
members those sources call. Consumer-authored partial types remain consumer
owned.

## Allowed behavior

- Add an ABI member after explicit API review.
- Change generated text while preserving semantics, provided the representative
  snapshot diff is reviewed.
- Retain obsolete forwarding members to preserve binaries.
- Use public-hidden APIs where generated code needs cross-assembly access.

## Forbidden behavior

- Remove, rename, narrow accessibility, or change the signature of a referenced
  runtime member in a 1.x release.
- Treat IntelliSense visibility as ABI visibility.
- Depend on generator implementation types at runtime.
- Edit consumer-owned declarations beyond emitted partial augmentations.
- Accept an unexplained generated-source baseline rewrite.

## Verification

`FormatterGenerationTest.FrozenGeneratedCodeAbi_CompilesAgainstCurrentRuntime`
remains a backward-compatibility smoke gate: it compiles a frozen generated-code
shape directly against the current `SharpPack.Core` runtime, so covered ABI
removals cannot be hidden by updating Generator in the same change.

The broader CLR-public surface of `SharpPack.Core` and `SharpPack.Streaming`
is captured in `eng/baselines/public-api/`. The published Generator package is
also covered for its host-facing entry point, `SharpPack.Generator.SharpPackGenerator`,
including its public `Initialize` contract. The baselines include public,
protected, and protected-internal members, including CLR-public members hidden
from IntelliSense. Signature-affecting required/optional custom modifiers are
preserved as `modreq`/`modopt`; for example, an `init` accessor records its
`IsExternalInit` requirement. Property accessors also retain dispatch
semantics such as `abstract`, `virtual`, `override`, and `sealed override`.
Generic constraints preserve nullable constraint metadata where it changes the
contract, including `notnull` and the `class` / `class?` distinction.
Trimming/NativeAOT contracts expressed with
`DynamicallyAccessedMembersAttribute` are preserved on public generic
parameters, parameters, return values, fields, and properties. CI regenerates
the surface in memory and fails when it differs from the checked-in text;
additions therefore require an explicit reviewed baseline update, while removals
and signature or trimming-contract changes cannot pass silently.

`eng/baselines/generated/representative.g.cs.txt` captures complete generated
source for a fixed fixture covering a simple object, unmanaged/fixed exact-size
data, version-tolerant and circular/reference-aware models, a union, a nested
generic, a custom formatter, and formatter-override dependency paths. This is a
representative emitter change detector rather than an exhaustive ABI catalog.

Both baselines are maintained by `tools/SharpPack.Baselines`. CI runs:

`dotnet run --project tools/SharpPack.Baselines/SharpPack.Baselines.csproj -c Release -- verify`

On the x64 CI leg it also runs
`bash tools/SharpPack.Baselines/characterize-failures.sh`. That characterization
temporarily changes the shipped `SharpPackSerializerRuntimeOptions.ThreadBufferSize`
accessor from `init` to `set` and requires the Core API gate to fail, restores
the source, then temporarily changes the actual Generator emitter header and
requires the generated-source snapshot gate to fail. The script restores both
files and finishes with a clean successful verification; the workflow's final
`git diff --exit-code` is an additional restoration check.

For an intentional API or generated-source change, run the same command with
`update` instead of `verify` and review the resulting text diff before
committing it. NativeAOT and current-generator tests remain complementary checks
for the current generator/runtime pair.

Snapshot text is a detector rather than a promise of forever-identical source.
An intentional update must state whether it changes ABI, wire bytes, allocation
behavior, or only implementation text.

## Runtime handshake (ABI 1)

`SharpPack.CompilerServices.SharpPackGeneratedCodeContract.Version` is a public,
IntelliSense-hidden constant owned by Core. Its value describes the generated-code
contract, not the NuGet package or wire-format version. This Generator accepts
exactly ABI `[1, 1]`. No future ABI range is implied.

Before semantic extraction or C# emission, the Generator resolves the marker
from the assembly containing `SharpPackableAttribute`. A consumer-defined type
with the same marker name cannot override the runtime's value. A missing,
malformed, older, or newer marker produces one `SHARPPACK043` error per compilation
at the first annotated model/union formatter. The diagnostic includes the detected
ABI, supported range, and package-alignment remediation. All C# output, including
external-union target factories, is suppressed for the incompatible compilation.
TypeScript output is also suppressed. A project with no recognized SharpPack
annotations does not require the runtime marker; ordinary unresolved-attribute
compiler diagnostics still apply when Core is completely absent.

ABI 1 is an unreleased contract being finalized in the #17 validation branches.
The handshake-only candidate `d4c15564` predates the compiler-services adapters.
The Generator therefore validates both the ABI number and the required helper
shapes from the same runtime assembly. A missing helper, malformed constructor or
method, restrictive generic constraint, or incompatible preservation-attribute
usage produces `SHARPPACK043` before emission. Consumer-defined lookalikes cannot
satisfy the runtime capability check. Before any release freezes ABI 1, further
required additions must be accompanied by capability checks or an ABI change.

The additive migration routes exact-size output and temporary version-tolerant
buffers through `CompilerServices.SharpPackExactBufferWriter` and
`CompilerServices.SharpPackPooledBufferWriter`, and emitted preservation markers
through `CompilerServices.PreserveAttribute`. The adapters are value types with
direct forwards to the existing storage and lease implementation. They add no
resolver, registry, dictionary, new buffer object, or virtual-dispatch layer.
The pool's lease IDs, finally-return, reset ordering, and duplicate-return checks
are retained. The old Internal types, preservation marker, exact writer factory,
and all old interfaces stay available for 1.x generated source and binaries.

Generated-source snapshots intentionally change helper names and generic writer
arguments. Wire layout does not change. Throughput or allocation performance
acceptance must be established separately; source structure is not timing evidence.

`RuntimeAbiDiagnosticsTest` verifies matching/missing/invalid/older/newer markers,
one error for multiple models, external-union suppression, consumer marker
shadowing, and re-evaluation after references change in the same generator driver.
The unsupported-runtime fixtures intentionally omit reader/writer/formatter APIs
so any accidental emission would create compiler cascades and fail the tests.

`tools/AbiCompatibility/verify.py` verifies real local NuGet packages in isolated
consumers, without project references:

- current aggregate package and current split Core + Generator: build and run;
- current Generator + Core from pinned pre-handshake `6666ada1`: fail with
  `SHARPPACK043`, no Generator exception or unrelated C# compiler errors;
- Generator from that same pinned revision + current Core: build and run.

That last case establishes the tested predecessor only, not a blanket promise
for arbitrary older generators. New Generator + pre-handshake Core is deliberately
unsupported even if a simple generated model might happen to compile. Align the
packages to a runtime carrying ABI 1. Full CI additionally checks public API and
unchanged generated-source baselines, the ordinary test suite, original wire
corpus, and NativeAOT publish/run on x64 and ARM64. Passing source checks alone is
not evidence that any execution gate passed.

## Per-member inventory and ownership

`eng/baselines/generated/runtime-dependencies.txt` records exact bound Core type,
constructor, method, property, and field documentation IDs, normalized to original
generic definitions. The public API baseline independently freezes signatures,
constraints, ref/scoped-related metadata, and accessor contracts. Together they
record what generated C# uses and the shape Core must continue providing.

`GeneratedCodeDependencyBaseline` inspects only generated trees using Roslyn
symbol/type information. It also records explicit-interface implementations,
overrides, and implicit parameterless formatter-base constructor calls. It rejects
unclassified Core references and any semantic dependency on `SharpPack.Internal`.
A comment mentioning a helper is not a dependency. User-owned declarations and
custom-formatter implementations are outside Core's fixed inventory; the fixture
covers both custom-formatter attribute forms and the shipped attributes.

The inventory fixture includes the representative models plus all 67 known generic
formatter mappings, ten other concrete formatter definitions (77 constructors in
total), all 90 selected unmanaged overloads (six families, arities 1–15), ordinary
and external generic unions, collection/set/dictionary models, optimized and
buffered version-tolerant paths, circular references, exact-size paths, and mutable
and immutable list deserialization. Coverage assertions fail if a mapped formatter
or required unmanaged overload is not reached. A valid injected generated
Serialize call must change the collected member set, characterizing detection of
an extra runtime dependency. Every update to the map or emitter requires reviewing
the inventory diff, not automatically accepting regenerated text.

Classification decisions:

- `user`: reader/writer operations, formatter interfaces/base classes and concrete
  formatter constructors, factory registration, and custom formatter attributes
  remain supported low-level APIs. They are not duplicated in CompilerServices.
- `legacy-generated`: fixed-size/exact-size/context factory markers, optimized
  non-generic ListFormatter helper overloads, hidden exact UTF8 and context
  dependency helpers, and generated throw helpers remain explicit 1.x ABI.
  Their identity is retained; later optional forwarding does not justify removal.
- `compiler-services`: newly introduced generated-only adapters and marker.
- Accidental public implementation surface not used by the generator, such as the
  reusable writer's constructor, enumeration and async-reset helpers, is outside
  this generated dependency inventory but still protected by the public API
  baseline. No 1.x removals or narrowing are authorized by this classification.

The package matrix additionally tests `d4c15564` Generator + current Core and
current Generator + `d4c15564` Core. The former must build/run using retained old
helpers; the latter must report the missing compiler-services capability instead
of unrelated C# errors. The pinned pre-handshake `6666ada1` cases remain in place.
