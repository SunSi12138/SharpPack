# Read-limit context/platform performance experiment

This isolated experiment extends the limited #30 measurements without changing
the PR or its previous validation branches. It compares current dev `6666ada`
with exact PR head `56113519`. The workflow proves that `8efea5af` to `6666ada`
changes no production sources, shared build properties or SDK policy, and that
the generator source tree is identical between the two measured revisions.
The extra dev changes are CI and sandbox MessagePack dependencies. No sandbox
benchmark packages are referenced by this neutral harness.

## Coverage

- Surface group: four context modes (no context, empty, Utf16 configuration,
  registered custom byte-array formatter); Span and two-segment sequence reads
  at64,4096,65536 and1048576 bytes.31-byte segmentation additionally covers64
  and4096. Allocating/reusable-buffer/destination-span writes cover64 and65536
  bytes without context and with empty context. Candidate limited reads cover
  exact byte/cardinality limits at64 and65536.
- Aggregate group: primitive, generated mixed string/object, array256,
  list128, nested32x32 arrays, depth32 and a32-node circular reference graph.
  Generated Utf16 mixed-string read/write is separate from byte-array tests.
  Framed MemoryStream reads cover64 and65536 bytes; exposed-buffer and
  unknown-length non-MemoryStream reads and Pipe creation+frame reads cover64.
  Pipe timings include construction, payload copy and endpoint cleanup, not
  deserialize-only transport costs. Candidate limited cases use exact
  collection/reference boundaries; depth32 requires33 reader entries including
  terminal null. Timing tests use accepted payloads.
- Separate untimed rejection controls check collection destination preservation,
  framed payload rejection before reading, and depth/reference failure kinds.
  They do not measure allocation avoidance. The unsafe old BitArray formatter
  is not executed; earlier safe-writer measurements remain separate evidence.

## Method and limits

The baseline builds the harness once. Its byte-identical managed IL is copied
to both execution directories, then only the candidate Core/Streaming DLLs are
substituted. Public signatures and assembly identities are stable for this
scoped comparison. No ReadyToRun/AOT harness is used. Reflection configures and
reads back added limit settings only during setup; timed operations do not use
reflection. Every process verifies loaded binary hashes and affinity.

Each host runs eight alternating AB/BA process pairs, with same-binary AA pairs
after blocks2 and6. Common cases rotate identically across each pair. Candidate
limited cases follow common cases. Calibration fixes identical iteration counts
across revisions. Each case warms for one second and records five timed samples,
thread/process allocation counters and GC counts. All fixture ValueTasks must
complete synchronously; all process threads inherit one-CPU affinity.

SDK10.0.114 is exact-pinned; runtime/architecture/affinity/hashes/process order are
recorded. Tiered compilation and tiered PGO are explicitly enabled. A fixed
warmup does not prove tier convergence. The intervals bootstrap eight paired
process log ratios, not40 correlated within-process samples. Two AA pairs help
spot noise but do not establish a validated regression threshold. Single hosted
machines per architecture, allocation/layout effects, thermal drift, limited
transport sizes and the absence of BenchmarkDotNet constrain interpretation.
This is not the complete original SerializerSurfaceBenchmark suite and cannot
alone establish a blanket no-regression or performance acceptance claim.

The summary retains every process median and does not discard outlier processes.
Raw subprocess logs and full structured samples stay in temporary runner storage;
only bounded numeric summaries are printed. No artifacts are uploaded, no
packages are published, and no PR, merge, release or permission changes occur.
