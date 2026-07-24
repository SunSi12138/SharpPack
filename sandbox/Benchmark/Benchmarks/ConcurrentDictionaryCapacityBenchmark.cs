using System.Collections.Concurrent;
using MemoryPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class ConcurrentDictionaryCapacityBenchmark
{
    byte[] payload = null!;

    [Params(32, 1024, 16384)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var value = new ConcurrentDictionary<int, int>();
        for (var index = 0; index < Count; index++)
        {
            value[index] = index;
        }

        payload = MemoryPackSerializer.Serialize(value);
    }

    [Benchmark]
    public ConcurrentDictionary<int, int>? Deserialize()
        => MemoryPackSerializer.Deserialize<ConcurrentDictionary<int, int>>(
            payload);
}
