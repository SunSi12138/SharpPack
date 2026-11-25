using SharpPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class ReadLimitsBenchmark
{
    readonly SharpPackSerializerContext defaultContext = new();
    readonly SharpPackSerializerContext limitedContext = new(
        SharpPackSerializerConfiguration.Default with
        {
            ReadLimits = SharpPackReadLimits.Default with
            {
                MaxDepth = 64,
                MaxCollectionLength = 4096,
                MaxReferenceCount = 4096,
            },
            MaxPayloadBytes = 1024 * 1024,
        });

    byte[] collectionPayload = null!;
    byte[] depthPayload = null!;

    [GlobalSetup]
    public void Setup()
    {
        collectionPayload = SharpPackSerializer.Serialize(
            Enumerable.Range(0, 256).ToArray());
        depthPayload = SharpPackSerializer.Serialize(
            new ReadLimitsBenchmarkNode
            {
                Next = new ReadLimitsBenchmarkNode
                {
                    Next = new ReadLimitsBenchmarkNode(),
                },
            });
    }

    [Benchmark(Baseline = true)]
    public int[]? Collection_DefaultContext()
        => SharpPackSerializer.Deserialize<int[]>(
            collectionPayload,
            defaultContext);

    [Benchmark]
    public int[]? Collection_LimitedContext()
        => SharpPackSerializer.Deserialize<int[]>(
            collectionPayload,
            limitedContext);

    [Benchmark]
    public ReadLimitsBenchmarkNode? Depth_DefaultContext()
        => SharpPackSerializer.Deserialize<ReadLimitsBenchmarkNode>(
            depthPayload,
            defaultContext);

    [Benchmark]
    public ReadLimitsBenchmarkNode? Depth_LimitedContext()
        => SharpPackSerializer.Deserialize<ReadLimitsBenchmarkNode>(
            depthPayload,
            limitedContext);
}

[SharpPackable]
public partial class ReadLimitsBenchmarkNode
{
    public ReadLimitsBenchmarkNode? Next { get; set; }
}
