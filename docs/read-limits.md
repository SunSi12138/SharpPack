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
multidimensional-array, generated, compressed BitPack, and legacy Streaming
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
configured maximum. The unframed buffer-backed MemoryStream overload preserves
its existing one-value/trailing-bytes contract and validates the actual consumed
value length after parsing because no message boundary is declared up front.

These are amplification guards, not a managed-memory sandbox. SharpPack does not
attempt to account for arbitrary allocations performed by custom formatters,
constructors, application callbacks, or user code. A custom formatter that
manually allocates from unguarded scalar data remains responsible for its own
application-specific validation.

Until stable error codes land in issue #16, limit violations use
`SharpPackSerializationException`; tests do not depend on exception message
text.
