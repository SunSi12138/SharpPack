using MemoryPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class CircularReferenceOptimizationBenchmark
{
    CircularReferenceBenchmarkNode[] value = null!;
    byte[] payload = null!;

    [Params(32, 1024)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        value = new CircularReferenceBenchmarkNode[Count];
        var root = new CircularReferenceBenchmarkNode
        {
            Id = 0,
            Name = "root",
        };
        value[0] = root;
        for (var index = 1; index < value.Length; index++)
        {
            value[index] = new CircularReferenceBenchmarkNode
            {
                Id = index,
                Name = "node-" + index,
                Shared = root,
            };
        }

        payload = MemoryPackSerializer.Serialize(value);
    }

    [Benchmark]
    public byte[] Serialize()
        => MemoryPackSerializer.Serialize(value);

    [Benchmark]
    public CircularReferenceBenchmarkNode[]? Deserialize()
        => MemoryPackSerializer.Deserialize<
            CircularReferenceBenchmarkNode[]>(payload);
}

[MemoryPackable(GenerateType.CircularReference)]
public partial class CircularReferenceBenchmarkNode
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }

    [MemoryPackOrder(1)]
    public string? Name { get; set; }

    [MemoryPackOrder(2)]
    public CircularReferenceBenchmarkNode? Shared { get; set; }
}
