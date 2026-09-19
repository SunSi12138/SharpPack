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
is captured in `eng/baselines/public-api/`. The baseline includes public,
protected, and protected-internal members, including CLR-public members hidden
from IntelliSense. CI regenerates the surface in memory and fails when it differs
from the checked-in text; additions therefore require an explicit reviewed
baseline update, while removals and signature changes cannot pass silently.

`eng/baselines/generated/representative.g.cs.txt` captures complete generated
source for a fixed fixture covering a simple object, unmanaged/fixed exact-size
data, version-tolerant and circular/reference-aware models, a union, a nested
generic, a custom formatter, and formatter-override dependency paths. This is a
representative emitter change detector rather than an exhaustive ABI catalog.

Both baselines are maintained by `tools/SharpPack.Baselines`. CI runs:

`dotnet run --project tools/SharpPack.Baselines/SharpPack.Baselines.csproj -c Release -- verify`

For an intentional API or generated-source change, run the same command with
`update` instead of `verify` and review the resulting text diff before
committing it. NativeAOT and current-generator tests remain complementary checks
for the current generator/runtime pair.

Snapshot text is a detector rather than a promise of forever-identical source.
An intentional update must state whether it changes ABI, wire bytes, allocation
behavior, or only implementation text.
