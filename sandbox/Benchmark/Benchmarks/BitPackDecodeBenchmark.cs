using System.Buffers;
using MemoryPack;
using MemoryPack.Compression;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class BitPackDecodeBenchmark
{
    byte[] payload = null!;
    bool[] currentDestination = null!;
    bool[] scalarDestination = null!;
    MemoryPackReaderOptionalStateLease readerState;

    [Params(32, 256, 4096, 65536)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        bool[]? source = Enumerable.Range(0, Length)
            .Select(static index => (index * 31 & 7) < 3)
            .ToArray();
        var bufferWriter = new ArrayBufferWriter<byte>();
        using var writerState = MemoryPackWriterOptionalStatePool.Rent();
        var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(
            ref bufferWriter,
            writerState);
        BitPackFormatter.Default.Serialize(ref writer, ref source);
        writer.Flush();
        payload = bufferWriter.WrittenSpan.ToArray();
        currentDestination = new bool[Length];
        scalarDestination = new bool[Length];
        readerState = MemoryPackReaderOptionalStatePool.Rent();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ((IDisposable)readerState).Dispose();
    }

    [Benchmark]
    public bool[] Current()
    {
        var reader = new MemoryPackReader(payload, readerState);
        bool[]? destination = currentDestination;
        try
        {
            BitPackFormatter.Default.Deserialize(
                ref reader,
                ref destination);
            return destination!;
        }
        finally
        {
            reader.Dispose();
        }
    }

    [Benchmark(Baseline = true)]
    public bool[] Scalar()
    {
        var reader = new MemoryPackReader(payload, readerState);
        bool[]? destination = scalarDestination;
        try
        {
            ScalarDeserialize(ref reader, ref destination);
            return destination!;
        }
        finally
        {
            reader.Dispose();
        }
    }

    static void ScalarDeserialize(
        ref MemoryPackReader reader,
        ref bool[]? value)
    {
        if (!reader.DangerousTryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<bool>();
            return;
        }

        var readCount = ((length - 1) / 32) + 1;
        var requireSize = readCount * 4;
        if (reader.Remaining < requireSize)
        {
            MemoryPackSerializationException.ThrowInsufficientBufferUnless(
                length);
        }

        if (value is null || value.Length != length)
        {
            value = new bool[length];
        }

        var bit = 0;
        var data = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (bit == 0)
            {
                reader.ReadUnmanaged(out data);
            }

            value[index] = (data & (1 << bit)) != 0;
            bit = (bit + 1) & 31;
        }
    }
}
