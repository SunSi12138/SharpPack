using SharpPack;

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

        payload = SharpPackSerializer.Serialize(value);
    }

    [Benchmark]
    public byte[] Serialize()
        => SharpPackSerializer.Serialize(value);

    [Benchmark]
    public CircularReferenceBenchmarkNode[]? Deserialize()
        => SharpPackSerializer.Deserialize<
            CircularReferenceBenchmarkNode[]>(payload);
}

[SharpPackable(GenerateType.CircularReference)]
public partial class CircularReferenceBenchmarkNode
{
    [SharpPackOrder(0)]
    public int Id { get; set; }

    [SharpPackOrder(1)]
    public string? Name { get; set; }

    [SharpPackOrder(2)]
    public CircularReferenceBenchmarkNode? Shared { get; set; }
}
