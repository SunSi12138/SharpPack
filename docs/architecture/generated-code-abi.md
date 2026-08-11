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

Public API comparison must cover `SharpPack.Core` and `SharpPack.Streaming`.
Generated-source characterization must cover a simple object, unmanaged/fixed
exact-size data, version tolerance, circular references, unions, nested generics,
custom formatters, and a context-override dependency. Existing generator tests
also compile emitted output, while NativeAOT validates that the resulting call
graph remains statically usable.

Snapshot text is a detector rather than a promise of forever-identical source.
An intentional update must state whether it changes ABI, wire bytes, allocation
behavior, or only implementation text.
