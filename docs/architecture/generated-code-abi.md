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

This is an additive handshake stage of #17. It does not rename or remove any
existing helper, change generated C# for a supported pair, change wire bytes, or
add runtime dispatch/allocation. Existing generated source remains supported by
the frozen ABI compilation gate. The complete per-member generated dependency
inventory and eventual wrapper migration remain separate work.

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
