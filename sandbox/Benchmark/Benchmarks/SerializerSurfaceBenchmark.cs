using System.Buffers;
using System.IO.Pipelines;
using MemoryPack;
using MemoryPack.Streaming;

namespace Benchmark.Benchmarks;

public enum SerializerContextScenario
{
    Default,
    Empty,
    Configured,
    Custom,
}

/// <summary>
/// Measures every public transport surface over the payload sizes used by the
/// performance acceptance suite. FormatterArchitectureBenchmark supplies the
/// type-shape matrix; this class supplies the transport/size/context matrix.
/// </summary>
[MemoryDiagnoser]
public class SerializerSurfaceBenchmark
{
    readonly MemoryPackSerializerContext emptyContext = new();
    readonly MemoryPackSerializerContext configuredContext =
        new(MemoryPackSerializerConfiguration.Utf16);
    readonly MemoryPackSerializerContext customContext =
        new MemoryPackSerializerContextBuilder()
            .Register(new PassthroughByteArrayFormatter())
            .Build();

    byte[] value = null!;
    byte[] serialized = null!;
    byte[] destination = null!;
    ArrayBufferWriter<byte> bufferWriter = null!;
    MemoryStream stream = null!;
    ReadOnlySequence<byte> segmented = default;

    [Params(64, 4 * 1024, 64 * 1024, 1024 * 1024)]
    public int PayloadSize { get; set; }

    [ParamsAllValues]
    public SerializerContextScenario ContextScenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        value = GC.AllocateUninitializedArray<byte>(PayloadSize);
        for (var i = 0; i < value.Length; i++)
        {
            value[i] = unchecked((byte)(i * 31));
        }

        serialized = Serialize(value);
        destination = GC.AllocateUninitializedArray<byte>(serialized.Length);
        bufferWriter = new ArrayBufferWriter<byte>(serialized.Length);
        stream = new MemoryStream(serialized.Length);
        segmented = CreateSegmentedSequence(serialized, 31);
    }

    [Benchmark(Baseline = true)]
    public byte[] ByteArray()
        => Serialize(value);

    [Benchmark]
    public int BufferWriter()
    {
        bufferWriter.Clear();
        return GetContext() is { } context
            ? MemoryPackSerializer.Serialize(ref bufferWriter, value, context)
            : MemoryPackSerializer.Serialize(ref bufferWriter, value);
    }

    [Benchmark]
    public int DestinationSpan()
    {
        var success = GetContext() is { } context
            ? MemoryPackSerializer.TrySerialize(destination, value, context, out var written)
            : MemoryPackSerializer.TrySerialize(destination, value, out written);
        return success ? written : -1;
    }

    [Benchmark]
    public byte[]? ReadOnlySpan()
        => GetContext() is { } context
            ? MemoryPackSerializer.Deserialize<byte[]>(serialized, context)
            : MemoryPackSerializer.Deserialize<byte[]>(serialized);

    [Benchmark]
    public byte[]? ReadOnlySequence()
        => GetContext() is { } context
            ? MemoryPackSerializer.Deserialize<byte[]>(segmented, context)
            : MemoryPackSerializer.Deserialize<byte[]>(segmented);

    [Benchmark]
    public async ValueTask<int> Stream()
    {
        stream.SetLength(0);
        if (GetContext() is { } context)
        {
            await MemoryPackSerializer.SerializeAsync(stream, value, context);
        }
        else
        {
            await MemoryPackSerializer.SerializeAsync(stream, value);
        }

        return checked((int)stream.Length);
    }

    [Benchmark]
    public async ValueTask<int> Pipe()
    {
        var pipe = new Pipe(
            new PipeOptions(
                pauseWriterThreshold: long.MaxValue,
                resumeWriterThreshold: long.MaxValue));
        var context = GetContext();
        var written = await MemoryPackStreamingSerializer.SerializeFrameAsync(
            pipe.Writer,
            value,
            context);
        _ = await MemoryPackStreamingSerializer.DeserializeFrameAsync<byte[]>(
            pipe.Reader,
            written,
            context);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
        return written;
    }

    byte[] Serialize(byte[] source)
        => GetContext() is { } context
            ? MemoryPackSerializer.Serialize(source, context)
            : MemoryPackSerializer.Serialize(source);

    MemoryPackSerializerContext? GetContext()
        => ContextScenario switch
        {
            SerializerContextScenario.Default => null,
            SerializerContextScenario.Empty => emptyContext,
            SerializerContextScenario.Configured => configuredContext,
            SerializerContextScenario.Custom => customContext,
            _ => throw new ArgumentOutOfRangeException(),
        };

    static ReadOnlySequence<byte> CreateSegmentedSequence(
        byte[] source,
        int segmentSize)
    {
        Segment? first = null;
        Segment? last = null;
        for (var offset = 0; offset < source.Length; offset += segmentSize)
        {
            var length = Math.Min(segmentSize, source.Length - offset);
            var segment = new Segment(source.AsMemory(offset, length));
            if (first is null)
            {
                first = segment;
            }
            else
            {
                last!.Append(segment);
            }

            last = segment;
        }

        return first is null
            ? ReadOnlySequence<byte>.Empty
            : new ReadOnlySequence<byte>(
                first,
                0,
                last!,
                last!.Memory.Length);
    }

    sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
            => Memory = memory;

        public Segment Append(Segment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
            return next;
        }
    }

    sealed class PassthroughByteArrayFormatter : MemoryPackFormatter<byte[]>
    {
        public override void Serialize<TBufferWriter>(
            ref MemoryPackWriter<TBufferWriter> writer,
            scoped ref byte[]? value)
            => writer.WriteUnmanagedArray(value);

        public override void Deserialize(
            ref MemoryPackReader reader,
            scoped ref byte[]? value)
            => reader.ReadUnmanagedArray(ref value);
    }
}
