# Streaming boundaries

## Layering

**Owner / boundary:** `SharpPack.Streaming` owns framing, incremental reads and
writes, and integration with streams/pipelines. `SharpPack.Core` exclusively owns
object encoding and formatter resolution.

**Allowed:** buffer an incomplete frame, request more data, serialize a complete
value through Core, honor cancellation between operations, and leave caller
resources open unless an API explicitly documents ownership transfer.

**Forbidden:** duplicate Core's object encoder, reinterpret object bytes, consume
past the current frame, report a truncated frame as a valid end-of-stream, block
asynchronously, or dispose a caller-owned transport.

**Verification:** streaming tests cover fragmented headers and payloads, multiple
frames in one buffer, truncation, cancellation, and ownership. Core wire tests
prove that the payload inside a frame remains the canonical representation.

## Backpressure and buffering

**Owner / boundary:** the transport owns its buffers and backpressure signal;
Streaming owns only state needed to finish the current logical operation.

**Allowed today:** advance a pipe precisely through examined/consumed
positions, await asynchronous flush/read operations, propagate transport
exceptions, and keep caller-owned transports open.

**Forbidden today:** retain transport memory after advancing it, busy-loop while
no progress is possible, synchronously block an asynchronous transport, or read
past the current logical frame.

**Pipe flush contract:** every public Pipe write path interprets
`FlushResult.IsCanceled` as `OperationCanceledException` and
`FlushResult.IsCompleted` as `InvalidOperationException`. SharpPack does not
complete caller-owned readers or writers. A completed writer flush means the
reader side has ended and the serialization operation cannot report success.

**Stream flush contract (1.x):** Core single-value `SerializeAsync(Stream, ...)`
writes the payload and performs one final `FlushAsync`, leaving the stream open.
The Streaming collection `SerializeAsync(Stream, ...)` path preserves its 1.x
behavior: it writes buffered chunks but does not issue an additional final flush;
the caller owns the final flush and stream lifetime.

**Payload boundaries:** Core general-stream deserialization reads to EOF (except
for the buffer-backed `MemoryStream` fast path, which advances only by the decoded
value). Payload-length and framed APIs read/consume exactly their declared
payload and fail if Core consumes a different byte count.

**Planned hardening (not a Stage 1 invariant):** add configurable read/payload
size limits before allocating from untrusted lengths (#15).

**Verification:** tests force one-byte fragmentation, coalesced frames,
truncated input, canceled/completed pipe flushes, stream final-flush ownership,
exact payload consumption, cancellation on covered paths, and transport backpressure.
`StreamingSerializer.SerializeAwaitsPipeBackpressure` configures low
`PipeOptions` pause/resume thresholds and proves serialization remains
incomplete until the reader drains the pipe. Resource-ownership tests verify
that success and failure release temporary state without closing the transport.

## Evolution rule

New framing features must be negotiated or unambiguously detectable and must not
change Core payload bytes. A pull request changing framing must add old/new
interoperability cases; a pull request changing Core must run the streaming suite
without modifying framing expectations.
