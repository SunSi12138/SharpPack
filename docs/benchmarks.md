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
.NET 10.0.2 ARM64 RyuJIT. Measurements were collected on 2026-07-25 with the
default BenchmarkDotNet job and `MemoryDiagnoser`.

| Operation | Items | MemoryPack 1.21.4 | SharpPack current | Ratio | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| Serialize to `byte[]` | 0 | 46.60 ns | 49.18 ns | 1.06 | 96 B / 96 B |
| Serialize to `byte[]` | 16 | 77.88 ns | 79.05 ns | 1.01 | 240 B / 240 B |
| Serialize to `byte[]` | 1024 | 2,722.85 ns | 2,593.69 ns | 0.95 | 10,840 B / 10,840 B |
| Deserialize | 0 | 59.03 ns | 57.29 ns | 0.97 | 416 B / 416 B |
| Deserialize | 16 | 123.09 ns | 120.96 ns | 0.98 | 848 B / 848 B |
| Deserialize | 1024 | 4,381.01 ns | 4,387.73 ns | 1.00 | 29,392 B / 29,392 B |
| Serialize to `IBufferWriter<byte>` | 0 | 37.98 ns | 39.28 ns | 1.03 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 16 | 64.78 ns | 65.94 ns | 1.02 | 0 B / 0 B |
| Serialize to `IBufferWriter<byte>` | 1024 | 2,505.15 ns | 2,391.48 ns | 0.95 | 0 B / 0 B |

For this representative object graph, the 16-item small case is within 2% of
MemoryPack, small deserialization is 2%–3% faster, and the larger serialization
case is about 5% faster. The deliberately near-empty `byte[]` case remains 6%
slower because fixed entry, reentrancy and copy-cleanup costs dominate its
46–49 ns runtime. Managed allocations match MemoryPack, including zero
serializer allocations on the pre-sized `IBufferWriter<byte>` path.

These results describe one workload on one ARM64 machine. They are not a claim
that the same ratio applies to every type, payload size, runtime or processor.
Run the checked-in benchmark on the deployment hardware before making a
performance-sensitive choice.
