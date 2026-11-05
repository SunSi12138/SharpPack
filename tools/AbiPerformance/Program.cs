using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpPack;

static class Program
{
    static int sink;
    static void Main(string[] args)
    {
        var small = new ExactModel { Number = 42, Text = "ABI wrapper", Values = [1, 2, 3, 4] };
        var large = new ExactModel { Number = 43, Text = new string('x', 1024), Values = Enumerable.Range(0, 1024).ToArray() };
        var buffered = new BufferedModel { Payload = small, Items = [small, large, small] };
        var node = new CircularModel { Number = 7 };
        node.Next = new CircularModel { Number = 8, Next = node };
        var cases = new (string Name, Func<byte[]> Run)[] {
            ("exact-small", () => SharpPackSerializer.Serialize(small)),
            ("exact-large", () => SharpPackSerializer.Serialize(large)),
            ("buffered-version-tolerant", () => SharpPackSerializer.Serialize(buffered)),
            ("circular-reference", () => SharpPackSerializer.Serialize(node)) };
        Validate(small, large, buffered, node);
        var selected = args.FirstOrDefault(x => x.StartsWith("--case=", StringComparison.Ordinal))?[7..];
        if (selected is not null && !cases.Any(x => x.Name == selected)) throw new Exception("Unknown case");
        var results = new List<object>();
        foreach (var item in cases.Where(x => selected is null || x.Name == selected))
        {
            var payload = item.Run();
            if (args.Contains("--jit")) { for (var i = 0; i < 64; i++) Consume(item.Run()); continue; }
            // Warm default tiered compilation/PGO on each fresh process before measurement.
            var warm = Stopwatch.StartNew();
            while (warm.ElapsedMilliseconds < 5000) Batch(item.Run, 1000);
            Thread.Sleep(100); // Give the background tiering thread an opportunity to finish.
            Batch(item.Run, 10000);
            if (args.Contains("--jit-tiered")) continue;
            var iterations = 1024;
            while (Measure(item.Run, iterations).Nanoseconds * iterations < 100_000_000 && iterations < 16_777_216)
                iterations *= 2;
            var samples = new List<Measurement>();
            for (var sample = 0; sample < 9; sample++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                samples.Add(Measure(item.Run, iterations));
            }
            results.Add(new { name = item.Name, iterations, payloadBytes = payload.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(payload)), samples });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), serverGc = GCSettings.IsServerGC,
            processorCount = Environment.ProcessorCount, frequency = Stopwatch.Frequency, results, sink }));
    }

    static void Validate(ExactModel small, ExactModel large, BufferedModel buffered, CircularModel node)
    {
        foreach (var model in new[] { small, large })
        {
            if (model is not ISharpPackExactSizeSerializable<ExactModel>) throw new Exception("Exact path missing");
            var copy = SharpPackSerializer.Deserialize<ExactModel>(SharpPackSerializer.Serialize(model))!;
            if (copy.Number != model.Number || copy.Text != model.Text || !copy.Values!.SequenceEqual(model.Values!))
                throw new Exception("Exact roundtrip failed");
        }
        var versioned = SharpPackSerializer.Deserialize<BufferedModel>(SharpPackSerializer.Serialize(buffered))!;
        if (versioned.Payload?.Number != small.Number || versioned.Items?.Length != 3 ||
            versioned.Items[1].Values?.Length != 1024) throw new Exception("Buffered roundtrip failed");
        var circular = SharpPackSerializer.Deserialize<CircularModel>(SharpPackSerializer.Serialize(node))!;
        if (circular.Number != 7 || circular.Next?.Number != 8 || !ReferenceEquals(circular.Next.Next, circular))
            throw new Exception("Circular roundtrip failed");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Batch(Func<byte[]> operation, int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++) { var bytes = operation(); checksum ^= bytes.Length + bytes[0]; }
        Volatile.Write(ref sink, checksum);
    }

    static void Consume(byte[] value) => Volatile.Write(ref sink, value.Length);
    static Measurement Measure(Func<byte[]> operation, int iterations)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        Batch(operation, iterations);
        var elapsed = Stopwatch.GetTimestamp() - start;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return new(elapsed * 1e9 / Stopwatch.Frequency / iterations, (double)allocated / iterations);
    }
    record Measurement(double Nanoseconds, double AllocatedBytes);
}

[SharpPackable]
public partial class ExactModel
{
    public int Number { get; set; }
    public string? Text { get; set; }
    public int[]? Values { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class BufferedModel
{
    [SharpPackOrder(0)] public ExactModel? Payload { get; set; }
    [SharpPackOrder(1)] public ExactModel[]? Items { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class CircularModel
{
    [SharpPackOrder(0)] public int Number { get; set; }
    [SharpPackOrder(1)] public CircularModel? Next { get; set; }
}
