using System.Buffers;
using SharpPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class FormatterHotPathBenchmark
{
    readonly FormatterBenchmarkDto value = new()
    {
        Id = 42,
        Name = "SharpPack",
    };
    readonly SharpPackSerializerContext emptyContext = new();
    readonly SharpPackSerializerContext configuredContext =
        new(SharpPackSerializerConfiguration.Utf8);
    readonly SharpPackSerializerContext customContext =
        new SharpPackSerializerContextBuilder()
            .Register(new PassthroughIntFormatter())
            .Build();
    ArrayBufferWriter<byte> bufferWriter = new(1024);
    readonly byte[] spanBuffer = new byte[1024];

    [GlobalSetup]
    public void Setup()
    {
        _ = SharpPackSerializer.Serialize(value);
        _ = SharpPackSerializer.Serialize(value, emptyContext);
        _ = SharpPackSerializer.Serialize(value, configuredContext);
        _ = SharpPackSerializer.Serialize(value, customContext);
    }

    [Benchmark(Baseline = true)]
    public byte[] Default()
        => SharpPackSerializer.Serialize(value);

    [Benchmark]
    public byte[] EmptyContext()
        => SharpPackSerializer.Serialize(value, emptyContext);

    [Benchmark]
    public byte[] ConfiguredContext()
        => SharpPackSerializer.Serialize(value, configuredContext);

    [Benchmark]
    public byte[] CustomContext()
        => SharpPackSerializer.Serialize(value, customContext);

    [Benchmark]
    public int BufferWriter()
    {
        bufferWriter.Clear();
        return SharpPackSerializer.Serialize(ref bufferWriter, value);
    }

    [Benchmark]
    public int SpanDestination()
    {
        SharpPackSerializer.TrySerialize(
            spanBuffer,
            value,
            out var written);
        return written;
    }

    sealed class PassthroughIntFormatter : SharpPackFormatter<int>
    {
        public override void Serialize<TBufferWriter>(
            ref SharpPackWriter<TBufferWriter> writer,
            scoped ref int value)
            => writer.WriteUnmanaged(value);

        public override void Deserialize(
            ref SharpPackReader reader,
            scoped ref int value)
            => reader.ReadUnmanaged(out value);
    }
}
