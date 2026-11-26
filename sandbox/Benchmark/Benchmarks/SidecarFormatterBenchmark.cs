using BenchmarkDotNet.Configs;
using SharpPack;
using SharpPack.Generated;
using SidecarFixture;
using System.Buffers;

[assembly: SharpPackExternalType(typeof(ExternalNote))]

namespace Benchmark.Benchmarks;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public partial class SidecarFormatterBenchmark
{
    readonly ExternalNote external = new() { Count = 42, Text = "same small request and wire bytes" };
    readonly SourceNote source = new() { Count = 42, Text = "same small request and wire bytes" };
    readonly ArrayBufferWriter<byte> destination = new(128);
    SharpPackSerializerContext context = null!;
    byte[] wire = null!;

    [GlobalSetup]
    public void Setup()
    {
        context = SharpPackExternalFormatters.Register(new SharpPackSerializerContextBuilder()).Build();
        wire = SharpPackSerializer.Serialize(source, context);
        if (!wire.AsSpan().SequenceEqual(SharpPackSerializer.Serialize(external, context)))
            throw new InvalidOperationException("The two benchmarks must consume the same wire shape.");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Write")]
    public int SourceWrite()
    {
        destination.ResetWrittenCount();
        return SharpPackSerializer.Serialize(destination, source, context);
    }

    [Benchmark, BenchmarkCategory("Write")]
    public int SidecarWrite()
    {
        destination.ResetWrittenCount();
        return SharpPackSerializer.Serialize(destination, external, context);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Read")]
    public SourceNote? SourceRead() => SharpPackSerializer.Deserialize<SourceNote>(wire, context);

    [Benchmark, BenchmarkCategory("Read")]
    public ExternalNote? SidecarRead() => SharpPackSerializer.Deserialize<ExternalNote>(wire, context);

    [SharpPackable]
    public sealed partial class SourceNote
    {
        public int Count { get; init; }
        public string? Text { get; init; }
    }
}
