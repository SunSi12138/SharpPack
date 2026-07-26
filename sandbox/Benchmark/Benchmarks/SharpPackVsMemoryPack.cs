using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class SharpPackVsMemoryPackSerializeBenchmark
    : SharpPackVsMemoryPackBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public byte[] MemoryPackSerialize()
        => global::MemoryPack.MemoryPackSerializer.Serialize(MemoryPackValue);

    [Benchmark]
    public byte[] SharpPackSerialize()
        => global::SharpPack.SharpPackSerializer.Serialize(SharpPackValue);
}

[MemoryDiagnoser(displayGenColumns: false)]
public class SharpPackVsMemoryPackDeserializeBenchmark
    : SharpPackVsMemoryPackBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public MemoryPackComparisonPayload? MemoryPackDeserialize()
        => global::MemoryPack.MemoryPackSerializer
            .Deserialize<MemoryPackComparisonPayload>(MemoryPackPayload);

    [Benchmark]
    public SharpPackComparisonPayload? SharpPackDeserialize()
        => global::SharpPack.SharpPackSerializer
            .Deserialize<SharpPackComparisonPayload>(SharpPackPayload);
}

[MemoryDiagnoser(displayGenColumns: false)]
public class SharpPackVsMemoryPackBufferWriterBenchmark
    : SharpPackVsMemoryPackBenchmarkBase
{
    [Benchmark(Baseline = true)]
    public int MemoryPackBufferWriter()
    {
        global::MemoryPack.MemoryPackSerializer.Serialize(
            MemoryPackWriter,
            MemoryPackValue);
        var written = MemoryPackWriter.WrittenCount;
        MemoryPackWriter.Clear();
        return written;
    }

    [Benchmark]
    public int SharpPackBufferWriter()
    {
        var written = global::SharpPack.SharpPackSerializer.Serialize(
            ref SharpPackWriter,
            SharpPackValue);
        SharpPackWriter.Clear();
        return written;
    }
}

public abstract class SharpPackVsMemoryPackBenchmarkBase
{
    static SharpPackVsMemoryPackBenchmarkBase()
    {
        if (Environment.GetEnvironmentVariable(
                "SHARPPACK_BENCHMARK_HIGH_THROUGHPUT") == "1")
        {
            global::SharpPack.SharpPackSerializer.ConfigureRuntime(
                global::SharpPack.SharpPackSerializerRuntimeOptions
                    .HighThroughput);
        }
    }

    [Params(0, 16, 1024)]
    public int ItemCount { get; set; }

    protected MemoryPackComparisonPayload MemoryPackValue { get; private set; }
        = null!;

    protected SharpPackComparisonPayload SharpPackValue { get; private set; }
        = null!;

    protected byte[] MemoryPackPayload { get; private set; } = null!;

    protected byte[] SharpPackPayload { get; private set; } = null!;

    protected ArrayBufferWriter<byte> MemoryPackWriter = null!;

    protected ArrayBufferWriter<byte> SharpPackWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        var values = Enumerable.Range(0, ItemCount).ToArray();
        var tags = Enumerable.Range(0, Math.Max(1, ItemCount / 16))
            .ToDictionary(
                static index => index,
                static index => $"tag-{index}");

        MemoryPackValue = new MemoryPackComparisonPayload
        {
            Id = 42,
            Name = "SharpPack versus MemoryPack",
            Values = values,
            Children = Enumerable.Range(0, ItemCount / 4)
                .Select(static index => new MemoryPackComparisonChild
                {
                    Id = index,
                    Name = $"child-{index}"
                })
                .ToList(),
            Tags = tags
        };

        SharpPackValue = new SharpPackComparisonPayload
        {
            Id = MemoryPackValue.Id,
            Name = MemoryPackValue.Name,
            Values = values,
            Children = Enumerable.Range(0, ItemCount / 4)
                .Select(static index => new SharpPackComparisonChild
                {
                    Id = index,
                    Name = $"child-{index}"
                })
                .ToList(),
            Tags = tags
        };

        MemoryPackPayload =
            global::MemoryPack.MemoryPackSerializer.Serialize(MemoryPackValue);
        SharpPackPayload =
            global::SharpPack.SharpPackSerializer.Serialize(SharpPackValue);

        if (!MemoryPackPayload.AsSpan().SequenceEqual(SharpPackPayload))
        {
            throw new InvalidOperationException(
                "SharpPack and MemoryPack produced different wire payloads.");
        }

        MemoryPackWriter = new ArrayBufferWriter<byte>(
            MemoryPackPayload.Length);
        SharpPackWriter = new ArrayBufferWriter<byte>(
            SharpPackPayload.Length);
    }
}

[global::SharpPack.SharpPackable]
public partial class SharpPackComparisonPayload
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public int[]? Values { get; set; }

    public List<SharpPackComparisonChild>? Children { get; set; }

    public Dictionary<int, string>? Tags { get; set; }
}

[global::SharpPack.SharpPackable]
public partial class SharpPackComparisonChild
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

[global::MemoryPack.MemoryPackable]
public partial class MemoryPackComparisonPayload
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public int[]? Values { get; set; }

    public List<MemoryPackComparisonChild>? Children { get; set; }

    public Dictionary<int, string>? Tags { get; set; }
}

[global::MemoryPack.MemoryPackable]
public partial class MemoryPackComparisonChild
{
    public int Id { get; set; }

    public string? Name { get; set; }
}
