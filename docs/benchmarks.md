# SharpPack versus MemoryPack benchmarks

This comparison uses BenchmarkDotNet 0.15.8 and the public MemoryPack 1.21.4
package. Both serializers run side by side in the same process against
generated models with identical fields, values and member order. Every setup
verifies that the two serializers produce byte-for-byte identical payloads.

## Reproduce

From the repository root:

```shell
dotnet run --project sandbox/Benchmark -c Release -- \
  --filter '*SharpPackVsMemoryPack*' --noOverwrite
```

The benchmark source is
[`SharpPackVsMemoryPack.cs`](../sandbox/Benchmark/Benchmarks/SharpPackVsMemoryPack.cs).
`MemoryPack` is the baseline, so a ratio above `1.00` means SharpPack took more
time.

## Apple M4 ARM64 results

Environment: Apple M4 (10 physical cores), macOS 26.4.1, .NET SDK 10.0.102,
.NET 10.0.2 ARM64 RyuJIT. Measurements were collected on 2026-07-24 with the
default BenchmarkDotNet job and `MemoryDiagnoser`.

| Operation | Items | MemoryPack 1.21.4 | SharpPack 1.0.1 | Ratio | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| Serialize to `byte[]` | 16 | 78.79 ns | 89.76 ns | 1.14 | 240 B / 240 B |
| Serialize to `byte[]` | 1024 | 2,799.34 ns | 2,942.71 ns | 1.05 | 10,840 B / 10,840 B |
| Deserialize | 16 | 126.6 ns | 141.8 ns | 1.12 | 848 B / 848 B |
| Deserialize | 1024 | 4,570.6 ns | 5,049.8 ns | 1.10 | 29,392 B / 29,392 B |
| Serialize to `IBufferWriter<byte>` | 16 | 65.86 ns | 76.44 ns | 1.16 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 1024 | 2,568.02 ns | 2,719.19 ns | 1.06 | 0 B / 0 B |

For this representative object graph, SharpPack currently trades 5%–16%
throughput for context isolation, collectible-ALC support, reentrancy and
resource-ownership hardening. Managed allocations match MemoryPack, including
zero serializer allocations on the pre-sized `IBufferWriter<byte>` path.

These results describe one workload on one ARM64 machine. They are not a claim
that the same ratio applies to every type, payload size, runtime or processor.
Run the checked-in benchmark on the deployment hardware before making a
performance-sensitive choice.
