using System.Buffers;
using System.Diagnostics;
using SharpPack;

internal static class ReadOnlyPerformance
{
    static int sink;

    internal static void Run()
    {
        byte[] model = [1, 42, 0, 0, 0];
        byte[] nested = [1, 1, 42, 0, 0, 0];
        var sequence = new ReadOnlySequence<byte>(model);
        var context = new SharpPackSerializerContextBuilder()
            .Register(new PlusOneFormatter()).Build();
        Measure("ordinary-span", () => SharpPackSerializer.Deserialize<OrdinaryModel>(model)!.Value);
        Measure("ordinary-sequence", () => SharpPackSerializer.Deserialize<OrdinaryModel>(sequence)!.Value);
        Measure("context-override", () => SharpPackSerializer.Deserialize<ContextModel>(model, context)!.Value);
        Measure("nested-span", () => SharpPackSerializer.Deserialize<NestedModel>(nested)!.Item!.Value);
    }

    static void Measure(string name, Func<int> read)
    {
        for (int i = 0; i < 20000; i++) sink = read();
        const int calibrationIterations = 200000;
        var calibrationStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < calibrationIterations; i++) sink = read();
        var calibrationSeconds = (Stopwatch.GetTimestamp() - calibrationStart) / (double)Stopwatch.Frequency;
        var iterations = (int)Math.Clamp(calibrationIterations * (0.1 / Math.Max(calibrationSeconds, 0.000001)), 100000, 10000000);
        double[] nanoseconds = new double[7];
        long[] allocations = new long[7];
        for (int sample = 0; sample < nanoseconds.Length; sample++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = read();
            var end = Stopwatch.GetTimestamp();
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            nanoseconds[sample] = (end - start) * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
        }
        Array.Sort(nanoseconds);
        Console.WriteLine($"PERF {name}: median_ns={nanoseconds[3]:F3}; allocation_min_bytes_per_op={allocations.Min() / (double)iterations:F3}; allocation_max_bytes_per_op={allocations.Max() / (double)iterations:F3}; samples=7; iterations={iterations}; sink={sink}");
    }
}
