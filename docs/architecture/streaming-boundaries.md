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

**Allowed:** advance a pipe precisely through examined/consumed positions, await
flush/read operations, and impose documented size limits before allocation.

**Forbidden:** retain transport memory after advancing it, busy-loop while no
progress is possible, allocate from an untrusted length before validating it, or
hide cancellation and transport exceptions.

**Verification:** tests must force one-byte fragmentation, coalesced frames,
oversized/truncated input, cancellation, and a transport that applies
backpressure. `StreamingSerializer.SerializeAwaitsPipeBackpressure` configures
low `PipeOptions` pause/resume thresholds and proves serialization remains
incomplete until the reader drains the pipe. Resource-ownership tests verify
that success and failure release temporary state without closing the transport.

## Evolution rule

New framing features must be negotiated or unambiguously detectable and must not
change Core payload bytes. A pull request changing framing must add old/new
interoperability cases; a pull request changing Core must run the streaming suite
without modifying framing expectations.
