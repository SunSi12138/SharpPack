# Stable error codes

Catch `SharpPackSerializationException` and inspect `ErrorCode`; do not parse
`Message`. Existing constructors and `ThrowMessage` retain `Unknown`. New
code-first constructors preserve the message and inner exception unchanged.

The explicit numeric values in `SharpPackErrorCode` are a compatibility surface.
Only append new categories. Never renumber, reuse, or reinterpret released values.
Messages remain human-facing and may change independently of classification.

## Throw-site inventory

- Buffer/sequence exhaustion: `UnexpectedEndOfPayload`.
- Negative lengths/dimensions, invalid member counts, declared collection-count mismatch: `InvalidLength`.
- Unrepresentable buffer/frame sizes and dimension products: `SizeOverflow`.
- Invalid headers, union tags, reference IDs, array dimension/count mismatch and unexpected nulls: `InvalidWireData`.
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

Issue #15's broader reader limits are not yet present on this branch's dev base;
when integrated, their rejection paths should use `ResourceLimitExceeded`.
