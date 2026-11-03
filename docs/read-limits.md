# Deserialization read limits

SharpPack can bound resource-amplifying declarations that the codec can identify
cheaply before expensive allocation or graph work. Limits are opt-in through a
`SharpPackSerializerContext` and do not change payload bytes.

```csharp
var context = new SharpPackSerializerContext(
    SharpPackSerializerConfiguration.Default with
    {
        ReadLimits = SharpPackReadLimits.Default with
        {
            MaxDepth = 64,
            MaxCollectionLength = 100_000,
            MaxReferenceCount = 100_000,
        },
        MaxPayloadBytes = 8 * 1024 * 1024,
    });
```

`MaxDepth` is inclusive. The current-compatible default is 999, matching the
historical reader behavior that rejected depth 1000. Collection and reference
limits default to `int.MaxValue`. Zero is the compatibility sentinel for these
settings, so `default(SharpPackSerializerConfiguration)` continues to behave
like earlier releases.

`MaxCollectionLength` is checked after structural header validation and before
SharpPack allocates or grows the corresponding collection. Specialized array,
multidimensional-array, generated, BitArray logical length, compressed BitPack,
public Dangerous collection-header, and legacy Streaming
collection paths honor the same effective limit. String lengths are
intentionally not governed by this setting.

`MaxReferenceCount` is checked only when circular-reference deserialization is
about to insert a new reference-table entry. Ordinary payloads do not pay this
cost.

`MaxPayloadBytes` is a top-level operation/transport boundary rather than
reader state. In-memory context overloads check the supplied buffer once.
Length-delimited Stream and Pipe APIs reject an oversized declared length before
renting or waiting for the payload. Length-prefixed Streaming combines its
existing per-call `maxFrameLength` with the context limit and uses the smaller
value. Unknown-length Stream reads reject as accumulated input crosses the
configured maximum, reading at most one probe byte beyond the limit. Pool rent
requests and read slices are bounded by the remaining budget plus that probe;
ArrayPool may return a larger bucket, so this is not an exact allocation cap.
Pending buffers are returned on read, cancellation, or limit failures until
ownership transfers to the sequence builder. The unframed buffer-backed MemoryStream overload preserves
its existing one-value/trailing-bytes contract and validates the actual consumed
value length after parsing because no message boundary is declared up front.
This legacy overload does not promise pre-allocation rejection of the entire
value. Use a declared payload length or framed API for that guarantee.

These are amplification guards, not a managed-memory sandbox. SharpPack does not
attempt to account for arbitrary allocations performed by custom formatters,
constructors, application callbacks, or user code. A custom formatter that
manually allocates from unguarded scalar data remains responsible for its own
application-specific validation.

Limit violations use `SharpPackSerializationException.ReadLimitKind` with
`PayloadBytes`, `Depth`, `CollectionLength`, or `ReferenceCount`. Other failures
leave this nullable property unset. This is a narrow resource-boundary reason,
not the complete error-code taxonomy planned in #16. Tests and callers need not
parse exception text. Structural header checks still precede cardinality checks
where the reader has enough information; a failure is classified at the first
boundary reached, not by a second whole-payload verification pass.
