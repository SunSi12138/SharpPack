# SharpPack versus MemoryPack benchmarks

This comparison uses BenchmarkDotNet 0.15.8, SharpPack 1.0.3 and the public
MemoryPack 1.21.4 package. The benchmark is a standalone NuGet consumer: it
does not reference SharpPack source projects. Both serializers run side by side
against generated models with identical fields, values and member order. Every
setup verifies that they produce byte-for-byte identical payloads before any
measurement starts.

## Reproduce

From the repository root after SharpPack 1.0.3 is available on NuGet.org:

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

SharpPack uses its default unpinned 8 KB retained buffer in the main table.
MemoryPack 1.21.4 retains a pinned 256 KB first buffer per serializer thread.

| Operation | Items | MemoryPack | SharpPack | MP Ops/s | SP Ops/s | Ratio | Allocated MP / SP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Serialize to `byte[]` | 0 | 45.69 ns | 47.16 ns | 21.88 M | 21.20 M | 1.03 | 96 B / 96 B |
| Serialize to `byte[]` | 16 | 77.98 ns | 78.31 ns | 12.82 M | 12.77 M | 1.00 | 240 B / 240 B |
| Serialize to `byte[]` | 1024 | 2.700 μs | 2.856 μs | 370.36 K | 350.19 K | 1.06 | 10,840 B / 10,840 B |
| Deserialize | 0 | 59.55 ns | 58.30 ns | 16.79 M | 17.15 M | 0.98 | 416 B / 416 B |
| Deserialize | 16 | 124.17 ns | 122.04 ns | 8.05 M | 8.19 M | 0.98 | 848 B / 848 B |
| Deserialize | 1024 | 4.452 μs | 4.392 μs | 224.60 K | 227.71 K | 0.99 | 29,392 B / 29,392 B |
| Serialize to `IBufferWriter<byte>` | 0 | 37.58 ns | 38.23 ns | 26.61 M | 26.16 M | 1.02 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 16 | 63.67 ns | 65.36 ns | 15.71 M | 15.30 M | 1.03 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 1024 | 2.519 μs | 2.628 μs | 396.96 K | 380.47 K | 1.04 | 0 B / 0 B |

On this object graph, SharpPack deserialization is 1%–2% faster, small
serialization is within 0%–3%, and 1024-item serialization is 4%–6% slower.
Managed allocations are identical. The pre-sized `IBufferWriter<byte>` path
has no serializer allocation in either implementation.

The default SharpPack configuration retains 248 KB less memory per serializer
thread than MemoryPack's 256 KB pinned first buffer. This is an intentional
steady-state memory tradeoff, not a claim that the smaller buffer is always
faster. With SharpPack's 80 KB high-throughput preset, the 1024-item `byte[]`
case measured 2.816 μs (355.07 K Ops/s) versus MemoryPack's 2.684 μs
(372.54 K Ops/s), a 1.05 latency ratio with the same 10,840 B allocation.

These results describe one workload on one ARM64 machine. They are not a claim
that the same ratio applies to every type, payload size, runtime or processor.
Run the checked-in NuGet benchmark on the deployment hardware before making a
performance-sensitive choice.
