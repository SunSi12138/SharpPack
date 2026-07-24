using System.Buffers;
using MemoryPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class FormatterHotPathBenchmark
{
    readonly FormatterBenchmarkDto value = new()
    {
        Id = 42,
        Name = "MemoryPack",
    };
    readonly MemoryPackSerializerContext emptyContext = new();
    readonly MemoryPackSerializerContext configuredContext =
        new(MemoryPackSerializerConfiguration.Utf8);
    readonly MemoryPackSerializerContext customContext =
        new MemoryPackSerializerContextBuilder()
            .Register(new PassthroughIntFormatter())
            .Build();
    ArrayBufferWriter<byte> bufferWriter = new(1024);
    readonly byte[] spanBuffer = new byte[1024];

    [GlobalSetup]
    public void Setup()
    {
        _ = MemoryPackSerializer.Serialize(value);
        _ = MemoryPackSerializer.Serialize(value, emptyContext);
        _ = MemoryPackSerializer.Serialize(value, configuredContext);
        _ = MemoryPackSerializer.Serialize(value, customContext);
    }

    [Benchmark(Baseline = true)]
    public byte[] Default()
        => MemoryPackSerializer.Serialize(value);

    [Benchmark]
    public byte[] EmptyContext()
        => MemoryPackSerializer.Serialize(value, emptyContext);

    [Benchmark]
    public byte[] ConfiguredContext()
        => MemoryPackSerializer.Serialize(value, configuredContext);

    [Benchmark]
    public byte[] CustomContext()
        => MemoryPackSerializer.Serialize(value, customContext);

    [Benchmark]
    public int BufferWriter()
    {
        bufferWriter.Clear();
        return MemoryPackSerializer.Serialize(ref bufferWriter, value);
    }

    [Benchmark]
    public int SpanDestination()
    {
        MemoryPackSerializer.TrySerialize(
            spanBuffer,
            value,
            out var written);
        return written;
    }

    sealed class PassthroughIntFormatter : MemoryPackFormatter<int>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer,
            scoped ref int value)
            => writer.WriteUnmanaged(value);

        public override void Deserialize(
            ref MemoryPackReader reader,
            scoped ref int value)
            => reader.ReadUnmanaged(out value);
    }
}
