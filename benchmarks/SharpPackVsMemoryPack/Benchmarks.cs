using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using System.Buffers;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SharpPackVsMemoryPack
{
    static SharpPackVsMemoryPack()
    {
        if (Environment.GetEnvironmentVariable(
                "SHARPPACK_BENCHMARK_HIGH_THROUGHPUT") == "1")
        {
            SharpPack.SharpPackSerializer.ConfigureRuntime(
                SharpPack.SharpPackSerializerRuntimeOptions.HighThroughput);
        }
    }

    [Params(0, 16, 1024)]
    public int ItemCount { get; set; }

    MemoryPackPayload memoryPackValue = null!;
    SharpPackPayload sharpPackValue = null!;
    byte[] memoryPackPayload = null!;
    byte[] sharpPackPayload = null!;
    ArrayBufferWriter<byte> memoryPackWriter = null!;
    ArrayBufferWriter<byte> sharpPackWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        var values = Enumerable.Range(0, ItemCount).ToArray();
        var tags = Enumerable.Range(0, Math.Max(1, ItemCount / 16))
            .ToDictionary(static i => i, static i => $"tag-{i}");

        memoryPackValue = new MemoryPackPayload
        {
            Id = 42,
            Name = "SharpPack versus MemoryPack",
            Values = values,
            Children = Enumerable.Range(0, ItemCount / 4)
                .Select(static i => new MemoryPackChild
                {
                    Id = i,
                    Name = $"child-{i}",
                })
                .ToList(),
            Tags = tags,
        };
        sharpPackValue = new SharpPackPayload
        {
            Id = memoryPackValue.Id,
            Name = memoryPackValue.Name,
            Values = values,
            Children = Enumerable.Range(0, ItemCount / 4)
                .Select(static i => new SharpPackChild
                {
                    Id = i,
                    Name = $"child-{i}",
                })
                .ToList(),
            Tags = tags,
        };

        memoryPackPayload =
            MemoryPack.MemoryPackSerializer.Serialize(memoryPackValue);
        sharpPackPayload =
            SharpPack.SharpPackSerializer.Serialize(sharpPackValue);
        if (!memoryPackPayload.AsSpan().SequenceEqual(sharpPackPayload))
        {
            throw new InvalidOperationException(
                "SharpPack and MemoryPack wire payloads differ.");
        }

        memoryPackWriter = new ArrayBufferWriter<byte>(
            memoryPackPayload.Length);
        sharpPackWriter = new ArrayBufferWriter<byte>(
            sharpPackPayload.Length);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")]
    public byte[] MemoryPackSerialize()
        => MemoryPack.MemoryPackSerializer.Serialize(memoryPackValue);

    [Benchmark, BenchmarkCategory("Serialize")]
    public byte[] SharpPackSerialize()
        => SharpPack.SharpPackSerializer.Serialize(sharpPackValue);

    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")]
    public MemoryPackPayload? MemoryPackDeserialize()
        => MemoryPack.MemoryPackSerializer
            .Deserialize<MemoryPackPayload>(memoryPackPayload);

    [Benchmark, BenchmarkCategory("Deserialize")]
    public SharpPackPayload? SharpPackDeserialize()
        => SharpPack.SharpPackSerializer
            .Deserialize<SharpPackPayload>(sharpPackPayload);

    [Benchmark(Baseline = true), BenchmarkCategory("BufferWriter")]
    public int MemoryPackBufferWriter()
    {
        MemoryPack.MemoryPackSerializer.Serialize(
            memoryPackWriter,
            memoryPackValue);
        var count = memoryPackWriter.WrittenCount;
        memoryPackWriter.Clear();
        return count;
    }

    [Benchmark, BenchmarkCategory("BufferWriter")]
    public int SharpPackBufferWriter()
    {
        var count = SharpPack.SharpPackSerializer.Serialize(
            ref sharpPackWriter,
            sharpPackValue);
        sharpPackWriter.Clear();
        return count;
    }
}

[SharpPack.SharpPackable]
public partial class SharpPackPayload
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int[]? Values { get; set; }
    public List<SharpPackChild>? Children { get; set; }
    public Dictionary<int, string>? Tags { get; set; }
}

[SharpPack.SharpPackable]
public partial class SharpPackChild
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

[MemoryPack.MemoryPackable]
public partial class MemoryPackPayload
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int[]? Values { get; set; }
    public List<MemoryPackChild>? Children { get; set; }
    public Dictionary<int, string>? Tags { get; set; }
}

[MemoryPack.MemoryPackable]
public partial class MemoryPackChild
{
    public int Id { get; set; }
    public string? Name { get; set; }
}
