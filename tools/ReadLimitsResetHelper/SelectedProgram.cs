using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpPack;
using SharpPack.Streaming;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = args.Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
string name = options["--case"], group = options["--group"];
int warmup = int.Parse(options.GetValueOrDefault("--warmup", "2000"));
bool calibrating = options.ContainsKey("--calibrate");
bool supported = typeof(SharpPackSerializerConfiguration).GetProperty("ReadLimits") is not null;
var item = group == "component" ? Fixtures.Component(name, supported) : Fixtures.Copy(name);
item.Run(); item.Validate();
Console.WriteLine("ENV " + JsonSerializer.Serialize(new {
    runtime = RuntimeInformation.FrameworkDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(),
    affinity = Process.GetCurrentProcess().ProcessorAffinity.ToInt64(), serverGC = GCSettings.IsServerGC,
    group, selectedCase = name, warmupMilliseconds = warmup, limited = supported,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    tieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
    harness = Fixtures.Hash(Assembly.GetExecutingAssembly().Location),
    core = Fixtures.Hash(typeof(SharpPackSerializer).Assembly.Location),
    streaming = Fixtures.Hash(typeof(SharpPackStreamingSerializer).Assembly.Location)
}));
var watch = Stopwatch.StartNew();
while (watch.ElapsedMilliseconds < warmup) RunBatch(item.Run, 128);
int iterations;
if (calibrating)
{
    iterations = 0;
    watch.Restart();
    do { RunBatch(item.Run, 128); iterations += 128; }
    while (watch.ElapsedMilliseconds < 50 && iterations < 100_000_000);
    Console.WriteLine("CAL " + JsonSerializer.Serialize(new { Name = name, iterations, item.PayloadHash }));
}
else
{
    iterations = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(options["--counts"]))![name];
    var samples = new List<object>();
    for (int sample = 0; sample < 5; ++sample)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long beforeThread = GC.GetAllocatedBytesForCurrentThread();
        long beforeTotal = GC.GetTotalAllocatedBytes(true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        RunBatch(item.Run, iterations);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long afterThread = GC.GetAllocatedBytesForCurrentThread();
        long afterTotal = GC.GetTotalAllocatedBytes(true);
        samples.Add(new { ns = elapsed * 1e9 / Stopwatch.Frequency / iterations,
            threadBytes = (afterThread - beforeThread) / (double)iterations,
            totalBytes = (afterTotal - beforeTotal) / (double)iterations,
            gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1, gen2 = GC.CollectionCount(2) - g2 });
    }
    item.Validate();
    Console.WriteLine("RESULT " + JsonSerializer.Serialize(new { Name = name, iterations, item.PayloadHash, samples }));
    // The observational allocation/alignment pass is deliberately after timing.
    Console.WriteLine("OBS " + JsonSerializer.Serialize(item.Observe()));
    item.Validate();
}
GC.KeepAlive(item); GC.KeepAlive(Sinks.Value); GC.KeepAlive(Sinks.Number);

[MethodImpl(MethodImplOptions.NoInlining)]
static void RunBatch(Action action, int count)
{
    for (int i = 0; i < count; ++i) action();
}

sealed record Probe(Action Run, Action Validate, Func<object> Observe, string PayloadHash);
static class Sinks { public static object? Value; public static long Number; }
sealed class Holder { public byte[]? Value; }

static class Fixtures
{
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    public static Probe Component(string name, bool supported)
    {
        var context = new SharpPackSerializerContext();
        var state = new SharpPackReaderOptionalState();
        var init = typeof(SharpPackReaderOptionalState).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<SharpPackReaderOptionalState, SharpPackSerializerContext>>();
        var resetExit = typeof(SharpPackReaderOptionalState).GetMethod("ResetAndExit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<SharpPackReaderOptionalState>>();
        var payload = Aligned(SharpPackSerializer.Serialize(123), 64);
        Action run;
        string description;
        if (name == "InitOnly")
        {
            run = () => init(state, context);
            description = "Repeated initialization without resetting; delegate-shaped component probe.";
        }
        else if (name == "InitResetExit")
        {
            run = () => { init(state, context); resetExit(state); };
            description = "Initialization followed by dirty ResetAndExit every operation; two prebound delegates.";
        }
        else if (name == "ResetClean")
        {
            state.Reset();
            run = state.Reset;
            description = "Repeated public Reset of already-clear state, not isolated dirty-reset cost.";
        }
        else if (name.StartsWith("CtorSpan"))
        {
            int size = int.Parse(name["CtorSpan".Length..]);
            payload = Aligned(new byte[size], 64);
            run = () => { var reader = new SharpPackReader(payload.Span, state); try { Sinks.Number = Consume(ref reader); } finally { reader.Dispose(); } };
            description = "Span reader construction plus non-inlined by-ref consumer and Dispose; default optional state.";
        }
        else if (name == "ManualReadInt")
        {
            run = () => {
                init(state, context);
                var reader = new SharpPackReader(payload.Span, state);
                try { Sinks.Number = reader.ReadValue<int>(); }
                finally { reader.Dispose(); resetExit(state); }
            };
            description = "Manual initialized state, reader, Int32 read and reset; no serializer state acquisition.";
        }
        else if (name == "DeserializeInt")
        {
            run = () => Sinks.Number = SharpPackSerializer.Deserialize<int>(payload.Span, context);
            description = "Public context deserialization control.";
        }
        else if (name.StartsWith("Guard"))
        {
            Check(supported, "Guard probe requires read-limit candidate");
            bool exact = name.EndsWith("Exact");
            const int length = 65540;
            if (exact)
            {
                object configuration = SharpPackSerializerConfiguration.Default;
                var property = typeof(SharpPackSerializerConfiguration).GetProperty("MaxPayloadBytes")!;
                property.SetValue(configuration, length);
                Check((int)property.GetValue(configuration)! == length, "Guard setup readback");
                context = new SharpPackSerializerContext((SharpPackSerializerConfiguration)configuration);
            }
            var actual = typeof(SharpPackSerializer).GetMethod("EnsurePayloadWithinLimit", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Action<long, SharpPackSerializerContext?>>();
            actual(length, context);
            if (exact)
            {
                bool rejected = false;
                try { actual(length + 1, context); }
                catch (SharpPackSerializationException error)
                {
                    rejected = error.GetType().GetProperty("ReadLimitKind")?.GetValue(error)?.ToString() == "PayloadBytes";
                }
                Check(rejected, "Guard plus-one must reject");
            }
            Action<long, SharpPackSerializerContext?> callback = name.Contains("Noop") ? NoopGuard : actual;
            run = () => { callback(length, context); Sinks.Number = length; };
            description = "Accepted payload guard or same-signature noop control through prebound delegate; not additive whole-path cost.";
            return new Probe(run, () => Check(Sinks.Number == length, "Guard sink"), () => new { description, exact }, "guard-65540-" + exact);
        }
        else throw new InvalidOperationException("Unknown component");
        void Validate()
        {
            var current = typeof(SharpPackReaderOptionalState).GetProperty("SerializerContext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state);
            if (name == "InitOnly") Check(ReferenceEquals(current, context), "Init context identity");
            else Check(current is null, "State context was retained");
            if (name is "ManualReadInt" or "DeserializeInt") Check(Sinks.Number == 123, "Primitive value");
            if (name.StartsWith("CtorSpan")) Check(Sinks.Number == payload.Length, "Reader constructor state");
            if (supported)
            {
                Check((int)typeof(SharpPackReaderOptionalState).GetProperty("MaxDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)! == 999, "Default depth state");
                Check((int)typeof(SharpPackReaderOptionalState).GetProperty("MaxCollectionLength", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)! == int.MaxValue, "Default collection state");
            }
        }
        return new Probe(run, Validate, () => new { description }, HashBytes(payload.Span));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void NoopGuard(long length, SharpPackSerializerContext? context) { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long Consume(ref SharpPackReader reader) => reader.Remaining + reader.Consumed + (reader.OptionalState.HasFormatterOverrides ? 1 : 0);

    public static Probe Copy(string name)
    {
        // ReadAlloc.empty.65536, ReadReuse.custom.normal.65536,
        // CopyAlloc.raw.65536, CopyReuse.raw.pinned32.65536.
        string[] parts = name.Split('.');
        string operation = parts[0], mode = parts[1];
        int length = int.Parse(parts[^1]);
        string placement = operation.EndsWith("Reuse") ? parts[2] : "alloc";
        var value = Enumerable.Range(0, length).Select(i => unchecked((byte)(i * 31))).ToArray();
        SharpPackSerializerContext? context = mode switch {
            "raw" => null,
            "empty" => new SharpPackSerializerContext(),
            "custom" => new SharpPackSerializerContextBuilder().Register(new BytesFormatter()).Build(),
            _ => throw new InvalidOperationException("Unknown mode")
        };
        var bytes = SharpPackSerializer.Serialize(value);
        Check(bytes.Length == length + 4, "Expected byte-array wire header");
        var payload = Aligned(bytes, 64);
        var holder = new Holder();
        var retained = new List<byte[]>();
        int pinnedCandidates = 0;
        byte[]? expected = null;
        if (placement != "alloc")
        {
            if (placement == "normal") expected = new byte[length];
            else
            {
                int target = int.Parse(placement["pinned".Length..]);
                for (int attempt = 0; attempt < 64; ++attempt)
                {
                    var buffer = GC.AllocateArray<byte>(length, pinned: true);
                    retained.Add(buffer);
                    pinnedCandidates++;
                    if (expected is null && ArrayMod(buffer, 64) == target) expected = buffer;
                }
                Check(expected is not null, "Pinned alignment target not found within 64 attempts");
                // Fixed setup allocation count for both targets/revisions. Only
                // the selected array remains rooted during timed collections.
                retained.Clear();
            }
            holder.Value = expected;
        }
        Action run;
        if (operation == "ReadAlloc")
            run = () => { holder.Value = context is null ? SharpPackSerializer.Deserialize<byte[]>(payload.Span) : SharpPackSerializer.Deserialize<byte[]>(payload.Span, context); Sinks.Value = holder.Value; };
        else if (operation == "ReadReuse")
            run = () => { Sinks.Number = context is null ? SharpPackSerializer.Deserialize(payload.Span, ref holder.Value) : SharpPackSerializer.Deserialize(payload.Span, ref holder.Value, context); Sinks.Value = holder.Value; };
        else if (operation == "CopyAlloc")
            run = () => { holder.Value = GC.AllocateUninitializedArray<byte>(length); CopyBlock(payload.Span[4..], holder.Value, length); Sinks.Value = holder.Value; };
        else if (operation == "CopyReuse")
            run = () => { CopyBlock(payload.Span[4..], holder.Value!, length); Sinks.Number = length + 4; Sinks.Value = holder.Value; };
        else throw new InvalidOperationException("Unknown copy operation");
        void Validate()
        {
            Check(holder.Value is not null && holder.Value.AsSpan().SequenceEqual(value), "Copy/read content");
            if (expected is not null) Check(ReferenceEquals(holder.Value, expected), "Destination was replaced");
            if (placement.StartsWith("pinned")) Check(ArrayMod(holder.Value!, 64) == int.Parse(placement["pinned".Length..]), "Pinned alignment drift");
            GC.KeepAlive(retained);
        }
        object Observe()
        {
            var mod64 = new SortedDictionary<int, int>();
            var mod4096 = new SortedDictionary<int, int>();
            for (int i = 0; i < 64; ++i)
            {
                run();
                int a = ArrayMod(holder.Value!, 64), b = ArrayMod(holder.Value!, 4096);
                mod64[a] = mod64.GetValueOrDefault(a) + 1;
                mod4096[b] = mod4096.GetValueOrDefault(b) + 1;
            }
            return new { length, placement, pinnedCandidates,
                bufferMod64 = MemoryMod(payload, 64), bufferMod4096 = MemoryMod(payload, 4096),
                dataMod64 = MemoryMod(payload[4..], 64), dataMod4096 = MemoryMod(payload[4..], 4096),
                outputMod64 = mod64, outputMod4096 = mod4096,
                note = "64 observations after timing; not the distribution during timed samples. No addresses recorded." };
        }
        return new Probe(run, Validate, Observe, HashBytes(payload.Span));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CopyBlock(ReadOnlySpan<byte> source, byte[] destination, int length)
        => Unsafe.CopyBlockUnaligned(ref MemoryMarshal.GetArrayDataReference(destination), ref MemoryMarshal.GetReference(source), (uint)length);

    static unsafe int ArrayMod(byte[] value, int modulus) { fixed (byte* p = value) return (int)((nuint)p % (uint)modulus); }
    static unsafe int MemoryMod(Memory<byte> value, int modulus) { fixed (byte* p = value.Span) return (int)((nuint)p % (uint)modulus); }
    static unsafe Memory<byte> Aligned(byte[] value, int alignment)
    {
        var buffer = GC.AllocateArray<byte>(value.Length + alignment - 1, pinned: true);
        int offset;
        fixed (byte* p = buffer) offset = (int)(((nuint)alignment - (nuint)p % (uint)alignment) % (uint)alignment);
        value.CopyTo(buffer, offset);
        return buffer.AsMemory(offset, value.Length);
    }
}

sealed class BytesFormatter : SharpPackFormatter<byte[]>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref byte[]? value) => writer.WriteUnmanagedArray(value);
    public override void Deserialize(ref SharpPackReader reader, scoped ref byte[]? value) => reader.ReadUnmanagedArray(ref value);
}
