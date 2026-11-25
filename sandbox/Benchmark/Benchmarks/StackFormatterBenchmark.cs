using SharpPack;
using System.Buffers;

namespace Benchmark.Benchmarks;

public class StackFormatterBenchmark
{
    readonly ArrayBufferWriter<byte> destination = new(64 * 1024);
    Stack<int> numbers = null!;
    Stack<string> labels = null!;

    [Params(16, 1024)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        numbers = new Stack<int>(Enumerable.Range(0, Count));
        labels = new Stack<string>(Enumerable.Range(0, Count).Select(i => $"item-{i}"));
    }

    [Benchmark]
    public int Integers()
    {
        destination.ResetWrittenCount();
        return SharpPackSerializer.Serialize(destination, numbers);
    }

    [Benchmark]
    public int Strings()
    {
        destination.ResetWrittenCount();
        return SharpPackSerializer.Serialize(destination, labels);
    }
}
