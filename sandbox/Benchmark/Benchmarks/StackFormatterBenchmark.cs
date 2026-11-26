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
        var expected = Environment.GetEnvironmentVariable("SHARPPACK_STACK_BENCHMARK_LAYOUT");
        if (expected is not null)
        {
            var layout = typeof(SharpPackSerializer).Assembly.GetType("SharpPack.Internal.CollectionsMarshalEx+StackView`1") is null
                ? "public" : "private";
            if (layout != expected)
                throw new InvalidOperationException($"The benchmark loaded the {layout} Stack path instead of {expected}.");
        }

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
