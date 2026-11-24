# Stable error codes

Catch `SharpPackSerializationException` and inspect `ErrorCode`; do not parse
`Message`. Existing constructors and `ThrowMessage` retain `Unknown`. To
provide a code when creating an exception, use the three-argument constructor
and pass `null` when there is no inner exception.

The explicit numeric values in `SharpPackErrorCode` are a compatibility surface.
Only append new categories. Never renumber, reuse, or reinterpret released values.
Messages remain human-facing and may change independently of classification.

## Throw-site inventory

- Buffer/sequence exhaustion: `UnexpectedEndOfPayload`.
- Negative lengths/dimensions, unsupported member counts on write, declared collection-count mismatch: `InvalidLength`.
- Unrepresentable buffer/frame sizes and dimension products: `SizeOverflow`.
- Invalid headers, property counts on read, union tags, reference IDs, array dimension/count mismatch and unexpected nulls: `InvalidWireData`.
- Invalid UTF encoding or encoded-length mismatch: `InvalidEncoding`.
- Reader/writer recursion depth: `DepthLimitExceeded`.
- Decompression and configured frame size limits: `ResourceLimitExceeded`.
- Formatter discovery/configuration: `FormatterResolutionFailed`.
- Disabled/unregistered Type values or IDs and unregistered union types: `TypeResolutionFailed`.
- Exact payload consumption mismatch: `FrameLengthMismatch`.
- Reentrant formatter construction: `RecursiveFormatterConstruction`.
- Arbitrary messages, concurrent collection mutation, Brotli operation failures,
  repeated decompression and exact-size writer misuse retain `Unknown` rather
  than claiming a malformed-wire diagnosis without evidence.

No new wrapping is introduced at the transport or user formatter boundary.
Transport EOF remains `EndOfStreamException`, cancellation remains cancellation,
and business exceptions thrown by formatter operations remain their original
exceptions. A complete but internally truncated frame is a payload failure, not
transport EOF. Formatter discovery is a separate, SharpPack-owned boundary.

The broader reader limits tracked in #15 will use these codes when integrated;
depth rejection is `DepthLimitExceeded`, and the other limits use
`ResourceLimitExceeded`.
