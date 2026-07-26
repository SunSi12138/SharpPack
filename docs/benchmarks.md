# SharpPack versus MemoryPack benchmarks

This comparison uses BenchmarkDotNet 0.15.8, SharpPack 1.1.0 and the public
MemoryPack 1.21.4 package. The benchmark is a standalone NuGet consumer and
does not reference SharpPack source projects. Both serializers run side by side
against generated models with identical fields, values and member order. Every
setup verifies that they produce byte-for-byte identical payloads before any
measurement starts.

## Reproduce

From the repository root after SharpPack 1.1.0 is available on NuGet.org:

```shell
dotnet run --project benchmarks/SharpPackVsMemoryPack -c Release -- \
  --filter '*SharpPackVsMemoryPack*' --noOverwrite
```

To run the `byte[]` tests with SharpPack's optional 80 KB retained buffer in a
fresh benchmark process:

```shell
SHARPPACK_BENCHMARK_HIGH_THROUGHPUT=1 \
dotnet run --project benchmarks/SharpPackVsMemoryPack -c Release -- \
  --filter '*MemoryPackSerialize*' '*SharpPackSerialize*' --noOverwrite
```

`MemoryPack` is the baseline, so a latency ratio above `1.00` means SharpPack
took more time. Throughput is reported as operations per second; higher is
better.

## Apple M4 ARM64 results

Environment: Apple M4 (10 physical cores), macOS 26.4.1, .NET SDK 10.0.102,
.NET 10.0.2 ARM64 RyuJIT. Measurements were collected on 2026-07-26 with
3 independent process launches, 4 warmup iterations, 12 measurement iterations
and `MemoryDiagnoser`.

### SharpPack 1.1.0 unmanaged results

The unmanaged payload is a 16-byte generated struct containing an `int` and a
`long`. Both serializers use their direct raw-memory path and produce identical
payloads. The round-trip benchmark uses non-inlined serialize and deserialize
helpers so BenchmarkDotNet measures the complete operation rather than a value
that the JIT can fold into harness overhead.

| Operation | MemoryPack | SharpPack | MP Ops/s | SP Ops/s | Ratio | Allocated MP / SP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serialize to `byte[]` | 2.365 ns | 2.363 ns | 422.92 M | 423.25 M | 1.00 | 40 B / 40 B |
| Serialize and deserialize round-trip | 2.937 ns | 2.888 ns | 340.53 M | 346.25 M | 0.98 | 40 B / 40 B |
| Serialize to pre-sized `IBufferWriter<byte>` | 2.402 ns | 2.191 ns | 416.32 M | 456.50 M | 0.91 | 0 B / 0 B |

SharpPack 1.1.0 is effectively tied with MemoryPack for unmanaged `byte[]`
serialization, is about 2% faster for the measured round-trip and about 9%
faster on the pre-sized `IBufferWriter<byte>` path. Neither writer implementation
allocates on that path.

### SharpPack 1.1.0 object graph results

SharpPack uses its default unpinned 8 KB retained buffer in this table.
MemoryPack 1.21.4 retained a pinned 256 KB first buffer per serializer thread.

| Operation | Items | MemoryPack | SharpPack | MP Ops/s | SP Ops/s | Ratio | Allocated MP / SP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Serialize to `byte[]` | 0 | 45.46 ns | 47.76 ns | 22.00 M | 20.94 M | 1.05 | 96 B / 96 B |
| Serialize to `byte[]` | 16 | 77.53 ns | 78.53 ns | 12.90 M | 12.73 M | 1.01 | 240 B / 240 B |
| Serialize to `byte[]` | 1024 | 2.659 μs | 2.840 μs | 376.05 K | 352.12 K | 1.07 | 10,840 B / 10,840 B |
| Deserialize | 0 | 59.38 ns | 58.24 ns | 16.84 M | 17.17 M | 0.98 | 416 B / 416 B |
| Deserialize | 16 | 124.31 ns | 122.04 ns | 8.04 M | 8.19 M | 0.98 | 848 B / 848 B |
| Deserialize | 1024 | 4.460 μs | 4.374 μs | 224.20 K | 228.63 K | 0.98 | 29,392 B / 29,392 B |
| Serialize to `IBufferWriter<byte>` | 0 | 40.99 ns | 38.37 ns | 24.40 M | 26.06 M | 0.95 (noisy) | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 16 | 64.13 ns | 65.05 ns | 15.59 M | 15.37 M | 1.01 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 1024 | 2.538 μs | 2.606 μs | 393.96 K | 383.75 K | 1.03 | 0 B / 0 B |

On this object graph, SharpPack deserialization is about 2% faster. `byte[]`
serialization ranges from 1% slower for the 16-item input to 7% slower for the
1024-item input. The pre-sized `IBufferWriter<byte>` path is within 1% for 16
items and 3% slower for 1024 items. The zero-item MemoryPack writer result had
high run-to-run variance, so the ratio marked `noisy` is reported for
completeness and is not treated as a demonstrated SharpPack improvement.
Managed allocations are identical, including zero serializer allocation on the
pre-sized writer path.

The wider `byte[]` gap relative to `IBufferWriter<byte>` indicates that final
array creation, buffer finalization and copying account for more of the
remaining difference than formatter dispatch or generated object traversal.

### SharpPack unpinned retained-buffer comparison

A separate NuGet-only configuration comparison measured the same generated
object graph with 8 KB, 80 KB and 256 KB retained first buffers. All three were
explicitly unpinned. The 8 KB job was the baseline; each job used 3 launches, 4
warmups and 12 measurement iterations. Independent pilot stages selected the
same invocation count for every buffer size at each input scale.

| Items | Per-operation allocation | 8 KB | 80 KB | 256 KB | 80 KB / 8 KB | 256 KB / 8 KB |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 112 B | 48.20 ns | 47.83 ns | 48.37 ns | 0.99 | 1.00 |
| 1024 | 10,856 B | 2.882 μs | 2.832 μs | 2.819 μs | 0.98 | 0.98 |
| 8192 | 88,499 B | 29.816 μs | 29.374 μs | 29.537 μs | 0.99 | 0.99 |
| 32768 | 358,403 B | 124.764 μs | 124.609 μs | 125.127 μs | 1.00 | 1.00 |

Every result was classified as `Same` by a 3% Mann-Whitney equivalence test,
and per-operation allocations were identical across buffer sizes. The 80 KB
configuration was 1%–2% faster for the approximately 11 KB and 88 KB results,
while 256 KB provided no stable advantage over 80 KB. The larger retained
buffers cost an additional 72 KB or 248 KB per active serializer thread,
respectively. This supports keeping 8 KB as the default; increasing an unpinned
buffer alone does not explain or eliminate the 7% large-object `byte[]` gap.

The default SharpPack configuration retains 248 KB less memory per active
serializer thread than a 256 KB first buffer. This is an intentional
steady-state memory tradeoff rather than a claim that the smaller buffer wins
every individual measurement.

These results describe one workload on one ARM64 machine. They are not a claim
that the same ratio applies to every type, payload size, runtime or processor.
Run the checked-in NuGet benchmark on the deployment hardware before making a
performance-sensitive choice.
