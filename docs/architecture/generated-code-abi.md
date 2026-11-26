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

## Version handshake

Keep `SharpPack.Core` and `SharpPack.Generator` aligned when using the split
packages. The aggregate `SharpPack` package supplies both. This generator
accepts ABI 1, identified by
`SharpPack.CompilerServices.SharpPackGeneratedCodeContract.Version` in Core.
The value versions the generated-code contract, not NuGet or the wire format.

If a project requests generation with an incompatible runtime, the generator
reports one `SHARPPACK043` on a source declaration and skips
generation. The diagnostic shows the detected and required ABI. It reads the
marker from the global SharpPack.Core reference, not from an alias-only reference
or a consumer-defined type with the same name. Standalone
`GenerateType.NoGenerate` contracts and inactive TypeScript output do not
require the marker.

The handshake does not remove the runtime members used by existing generated
assemblies. Compatibility with an older standalone generator depends on the
specific package pair and deployment mode; trimmed deployments must also
follow the AOT migration below.

## Generated dependency inventory

`eng/baselines/generated/runtime-dependencies.txt` lists the Core types and
members the generator binds in the ABI fixtures. It records exact symbol IDs,
including generic arities, and scope details such as `scoped`, `UnscopedRef`,
ref-like types, and ref returns. Update this manifest with the public API and
generated-source baselines when intentionally changing a runtime dependency.

The `SharpPack.CompilerServices` namespace owns new generated-only contracts,
including the version marker and the generated preservation attribute. The
inventory also identifies existing user APIs and legacy generated dependencies.
The existing exact and pooled buffer types retain their signatures and emitted
call paths; the handshake does not require a runtime wrapper or a buffer rewrite.
Other `SharpPack.Internal` references fail the inventory check.

`Packaged consumers` builds isolated package consumers for the current
aggregate package and split Core/Generator packages, then publishes and runs
them under NativeAOT. It also builds the generator from the `v1.1.0` source tag
against the current runtime for an untrimmed round trip, and expects
`SHARPPACK043` from the current generator against that tag's older runtime.
This is a tested package pair, not a compatibility promise for all versions.

## Deserialize-only NativeAOT roots

Generic deserialization retains `NonPublicConstructors`, including generated
static constructors. This is narrower than retaining every non-public method
on an arbitrary caller type (notably `System.Type`), but it still retains all
non-public constructors, not only the static constructor.

Generated static initialization calls the same typed preservation methods
available to handwritten models. Each uses a static-interface delegate to keep
the corresponding factory available to runtime resolution. Known member
factory dependencies are preserved when creating the enclosing type's factory.
The generated code does not depend on the compiler's private metadata spelling.
A private static initializer is anchored on the cold factory path when the
model does not require strict initialization. The generator chooses its names
without rebinding existing identifiers. No global registry or per-read dispatch
is needed.

Cross-assembly unions continue to require the generated context registration
extension. Runtime-discovered public factories should still use explicit
`RegisterFactory<T, TFactory>()` for AOT. Closed collection formatter native
code still needs the explicit roots described in the README.

The isolated `sandbox/DeserializeOnlyAot` consumer uses literal payloads and no
model serialization or model construction before reading. It is separate from
the round-trip sample so serialization cannot accidentally preserve factories.
CI checks that a non-inlined unused private method has no native code symbol;
a separately compiled, explicitly invoked method serves as the positive
control. The isolated consumer also covers generic wrapper annotations and
the application-owned factory consumer runs in NativeAOT and trimmed JIT.

This NativeAOT root contract requires rebuilding model assemblies with the
matching updated Generator as well as updating Core. A runtime-only DLL update
cannot retrofit the new static-constructor roots into old generated code.
The frozen generated-code ABI gate checks compilation against the runtime; it
does not promise deserialize-only NativeAOT compatibility for older generated
assemblies. The [migration guide](../aot-factory-migration.md) covers package
alignment, generic wrappers, and handwritten factories.
