using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharpPack;

internal static class ReadOnlyPerformance
{
    static int sink;
    static readonly byte[] nested = [1, 1, 42, 0, 0, 0];

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ReadNested() => SharpPackSerializer.Deserialize<NestedModel>(nested)!.Item!.Value;

    internal static void Run()
    {
        if (ReadNested() != 42) throw new InvalidOperationException("Incorrect nested payload");
        var warmup = Stopwatch.StartNew();
        while (warmup.ElapsedMilliseconds < 500)
            for (int i = 0; i < 10000; i++) sink = ReadNested();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        const int iterations = 5000000;
        double[] samples = new double[15];
        double[] allocations = new double[15];
        int[] gen0 = new int[15];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var collections = GC.CollectionCount(0);
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) sink = ReadNested();
            var end = Stopwatch.GetTimestamp();
            samples[sample] = (end - start) * (1e9 / Stopwatch.Frequency) / iterations;
            allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
            gen0[sample] = GC.CollectionCount(0) - collections;
        }
        if (sink != 42 || allocations.Any(value => value != 48.0))
            throw new InvalidOperationException("Incorrect result or unexpected allocation count");
        var sorted = samples.OrderBy(value => value).ToArray();
        Console.WriteLine("MEASURE " + JsonSerializer.Serialize(new Measurement(samples, allocations, gen0, sorted[7], iterations, sink), MeasurementJsonContext.Default.Measurement));
    }
}
internal sealed record Measurement(double[] samples, double[] allocations, int[] gen0, double median, int iterations, int sink);
[System.Text.Json.Serialization.JsonSerializable(typeof(Measurement))]
internal partial class MeasurementJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
