using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpPack;
using SharpPack.Streaming;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var options = args.Chunk(2).ToDictionary(x => x[0], x => x[1]);
string group = options["--group"];
int rotation = int.Parse(options.GetValueOrDefault("--rotation", "0"));
bool calibrating = options.ContainsKey("--calibrate");
string? selectedCase = options.GetValueOrDefault("--case");
int warmupMilliseconds = int.Parse(options.GetValueOrDefault("--warmup", "1000"));
var counts = calibrating ? null : JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(options["--counts"]))!;
var cases = new List<Case>();
var empty = new SharpPackSerializerContext();
var utf16 = new SharpPackSerializerContext(SharpPackSerializerConfiguration.Utf16);
var custom = new SharpPackSerializerContextBuilder().Register(new BytesFormatter()).Build();
var modes = new[] { new Mode("raw", null), new Mode("empty", empty), new Mode("utf16", utf16), new Mode("custom", custom) };
bool supported = typeof(SharpPackSerializerConfiguration).GetProperty("ReadLimits") != null;
var limited = supported ? Limits() : null;
Console.WriteLine("ENV " + JsonSerializer.Serialize(new {
    runtime = RuntimeInformation.FrameworkDescription, arch = RuntimeInformation.ProcessArchitecture.ToString(),
    cpuCount = Environment.ProcessorCount, serverGC = GCSettings.IsServerGC,
    affinity = Process.GetCurrentProcess().ProcessorAffinity.ToInt64(), frequency = Stopwatch.Frequency,
    group, selectedCase, warmupMilliseconds, limited = supported, tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    tieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
    jitDisasm = Environment.GetEnvironmentVariable("DOTNET_JitDisasm"), harness = Hash(Assembly.GetExecutingAssembly().Location),
    core = Hash(typeof(SharpPackSerializer).Assembly.Location), streaming = Hash(typeof(SharpPackStreamingSerializer).Assembly.Location)
}));

if (group == "surface")
{
    foreach (int n in new[] { 64, 4096, 65536, 1048576 })
    {
        byte[] value = Enumerable.Range(0, n).Select(i => unchecked((byte)(i * 31))).ToArray();
        foreach (var mode in modes) AddBytes(value, mode, false);
        if (limited != null && n is 64 or 65536) AddBytes(value, new Mode("limited", Limits(payload: n + 4, collection: n)), true);
        if (n is 64 or 65536)
            foreach (var mode in modes.Take(2)) AddWrites(value, mode);
    }
}
else if (group == "aggregate")
{
    foreach (var mode in modes.Take(2))
    {
        AddShapes(mode, false);
        AddTransports(mode, false);
    }
    AddMixed(new Mode("utf16", utf16), false);
    if (limited != null)
    {
        AddShapes(new Mode("limited", limited), true);
        AddTransports(new Mode("limited", limited), true);
        VerifyRejects();
    }
}
else throw new InvalidOperationException("Unknown group");
cases.Add(new Case("harness-control", () => Sinks.Number = 123, false, "none"));
Console.WriteLine("CHECKS " + JsonSerializer.Serialize(new { common = cases.Count(x => !x.Limited), limited = cases.Count(x => x.Limited) }));
var common = cases.Where(x => !x.Limited).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
var extra = cases.Where(x => x.Limited).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
var ordered = common.Skip(rotation % common.Count).Concat(common.Take(rotation % common.Count)).Concat(extra).ToArray();
if (selectedCase != null)
{
    ordered = ordered.Where(x => x.Name == selectedCase).ToArray();
    Require(ordered.Length == 1, "Selected case was not unique");
}
foreach (var item in ordered)
{
    item.Run();
    var watch = Stopwatch.StartNew();
    while (watch.ElapsedMilliseconds < warmupMilliseconds) RunBatch(item.Run, 128);
    int iterations;
    if (calibrating)
    {
        iterations = 0;
        watch.Restart();
        do { RunBatch(item.Run, 128); iterations += 128; }
        while (watch.ElapsedMilliseconds < 50 && iterations < 100_000_000);
        Console.WriteLine("CAL " + JsonSerializer.Serialize(new { item.Name, iterations, item.Limited, item.PayloadHash }));
        continue;
    }
    iterations = counts![item.Name];
    long jitBytesBefore = JitDumpLength();
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
    long jitBytesAfter = JitDumpLength();
    Console.WriteLine("RESULT " + JsonSerializer.Serialize(new { item.Name, item.Limited, item.PayloadHash, iterations, samples, jitBytesBefore, jitBytesAfter }));
}
GC.KeepAlive(Sinks.Value);
GC.KeepAlive(Sinks.Number);

[MethodImpl(MethodImplOptions.NoInlining)]
static void RunBatch(Action run, int iterations)
{
    for (int i = 0; i < iterations; ++i) run();
}

static long JitDumpLength()
{
    try
    {
        var path = Environment.GetEnvironmentVariable("DOTNET_JitStdOutFile");
        return path is not null && File.Exists(path) ? new FileInfo(path).Length : -1;
    }
    catch (IOException) { return -1; }
    catch (UnauthorizedAccessException) { return -1; }
}

void AddBytes(byte[] value, Mode mode, bool isLimited)
{
    var context = mode.Context;
    var payload = Aligned(Encode(value, context));
    var first = new Segment(payload[..(payload.Length / 2)]);
    var last = first.Append(payload[(payload.Length / 2)..]);
    var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    byte[] ReadSpan() => context == null ? SharpPackSerializer.Deserialize<byte[]>(payload.Span)! : SharpPackSerializer.Deserialize<byte[]>(payload.Span, context)!;
    byte[] ReadSequence() => context == null ? SharpPackSerializer.Deserialize<byte[]>(sequence)! : SharpPackSerializer.Deserialize<byte[]>(sequence, context)!;
    Require(ReadSpan().SequenceEqual(value) && ReadSequence().SequenceEqual(value), "Byte surface roundtrip");
    Add($"BytesSpan{value.Length}.{mode.Name}", () => Sinks.Value = ReadSpan(), payload, isLimited);
    Add($"BytesSequence2x{value.Length}.{mode.Name}", () => Sinks.Value = ReadSequence(), payload, isLimited);
    // Match the existing SerializerSurfaceBenchmark's31-byte segmentation at small/medium sizes.
    if (value.Length is 64 or 4096)
    {
        var denseFirst = new Segment(payload[..Math.Min(31, payload.Length)]);
        var denseLast = denseFirst;
        for (int i = denseLast.Memory.Length; i < payload.Length; i += 31)
            denseLast = denseLast.Append(payload.Slice(i, Math.Min(31, payload.Length - i)));
        var dense = new ReadOnlySequence<byte>(denseFirst, 0, denseLast, denseLast.Memory.Length);
        byte[] ReadDense() => context == null ? SharpPackSerializer.Deserialize<byte[]>(dense)! : SharpPackSerializer.Deserialize<byte[]>(dense, context)!;
        Require(ReadDense().SequenceEqual(value), "Dense sequence roundtrip");
        Add($"BytesSequence31x{value.Length}.{mode.Name}", () => Sinks.Value = ReadDense(), payload, isLimited);
    }
}

void AddWrites(byte[] value, Mode mode)
{
    var context = mode.Context;
    var expected = Encode(value, context);
    var writer = new ArrayBufferWriter<byte>(expected.Length);
    var destination = new byte[expected.Length];
    void Buffer()
    {
        writer.Clear();
        Sinks.Number = context == null ? SharpPackSerializer.Serialize(ref writer, value) : SharpPackSerializer.Serialize(ref writer, value, context);
    }
    void Destination()
    {
        bool ok = context == null ? SharpPackSerializer.TrySerialize(destination, value, out var written) : SharpPackSerializer.TrySerialize(destination, value, context, out written);
        if (!ok) throw new InvalidOperationException("Destination unexpectedly too small");
        Sinks.Number = written;
    }
    Buffer(); Destination();
    Require(writer.WrittenSpan.SequenceEqual(expected) && destination.AsSpan().SequenceEqual(expected), "Writer bytes");
    Add($"WriteArray{value.Length}.{mode.Name}", () => Sinks.Value = Encode(value, context), expected, false);
    Add($"WriteBuffer{value.Length}.{mode.Name}", Buffer, expected, false);
    Add($"WriteDestination{value.Length}.{mode.Name}", Destination, expected, false);
}

void AddShapes(Mode mode, bool isLimited)
{
    var context = mode.Context;
    var primitive = Aligned(Encode(123, context));
    int ReadInt() => context == null ? SharpPackSerializer.Deserialize<int>(primitive.Span) : SharpPackSerializer.Deserialize<int>(primitive.Span, context);
    Require(ReadInt() == 123, "Primitive");
    Add("Int32." + mode.Name, () => Sinks.Number = ReadInt(), primitive, isLimited);
    // Setup chooses a distinct callback. The timed separate entry contains no
    // raw/context mode branch; the shared ReadInt above remains unchanged.
    int ReadRawInt() => SharpPackSerializer.Deserialize<int>(primitive.Span);
    int ReadContextInt() => SharpPackSerializer.Deserialize<int>(primitive.Span, context!);
    if (!isLimited && mode.Name == "raw")
    {
        Require(ReadRawInt() == 123, "Separate raw primitive");
        Add("Int32Separate.raw", () => Sinks.Number = ReadRawInt(), primitive, false);
    }
    if (!isLimited && mode.Name == "empty")
    {
        Require(ReadContextInt() == 123, "Separate context primitive");
        Add("Int32Separate.empty", () => Sinks.Number = ReadContextInt(), primitive, false);
    }

    int[] ints = Enumerable.Range(0, 256).ToArray();
    var collectionMode = isLimited ? new Mode(mode.Name, Limits(collection: 256)) : mode;
    AddModel("Array256", ints, collectionMode, x => x.SequenceEqual(ints), isLimited);
    var strings = Enumerable.Range(0, 128).Select(x => "message-" + x).ToList();
    AddModel("List128", strings, isLimited ? new Mode(mode.Name, Limits(collection: 128)) : mode, x => x.SequenceEqual(strings), isLimited);
    var arrays = Enumerable.Range(0, 32).Select(x => Enumerable.Range(x, 32).ToArray()).ToArray();
    AddModel("NestedArrays32", arrays, isLimited ? new Mode(mode.Name, Limits(collection: 32)) : mode, x => x.Length == 32 && x.SelectMany(a => a).SequenceEqual(arrays.SelectMany(a => a)), isLimited);
    var node = Depth(32);
    AddModel("Depth32", node, isLimited ? new Mode(mode.Name, Limits(depth: 33)) : mode, x => CountDepth(x) == 32, isLimited);
    var cycle = Cycle(32);
    AddModel("References32", cycle, isLimited ? new Mode(mode.Name, Limits(references: 32)) : mode, x => CountCycle(x) == 32, isLimited);
    AddMixed(mode, isLimited);
}

void AddMixed(Mode mode, bool isLimited)
{
    var value = new Mixed { Id = 23, Name = "network-消息-" + new string('x', 96), Values = Enumerable.Range(0, 16).ToArray() };
    AddModel("Mixed", value, mode, x => x.Id == 23 && x.Name == value.Name && x.Values.SequenceEqual(value.Values), isLimited);
    if (mode.Name == "utf16")
    {
        var expected = Encode(value, mode.Context);
        Add("WriteMixed.utf16", () => Sinks.Value = Encode(value, mode.Context), expected, false);
    }
}

void AddModel<T>(string name, T value, Mode mode, Func<T, bool> valid, bool isLimited)
{
    var context = mode.Context;
    var payload = Aligned(Encode(value, context));
    T Read() => (context == null ? SharpPackSerializer.Deserialize<T>(payload.Span) : SharpPackSerializer.Deserialize<T>(payload.Span, context))!;
    Require(valid(Read()), name + " roundtrip");
    Add(name + "." + mode.Name, () => Sinks.Value = Read(), payload, isLimited);
}

void AddTransports(Mode mode, bool isLimited)
{
    var context = mode.Context;
    foreach (int n in new[] { 64, 65536 })
    {
        byte[] value = Enumerable.Range(0, n).Select(x => unchecked((byte)x)).ToArray();
        var payload = Encode(value, context);
        var stream = new MemoryStream(payload, writable: false);
        byte[] ReadFrame()
        {
            stream.Position = 0;
            return Complete(context == null ? SharpPackSerializer.DeserializeAsync<byte[]>(stream, payload.Length) : SharpPackSerializer.DeserializeAsync<byte[]>(stream, payload.Length, context))!;
        }
        Require(ReadFrame().SequenceEqual(value), "Stream frame");
        Add($"StreamFrame{n}.{mode.Name}", () => Sinks.Value = ReadFrame(), payload, isLimited);
        if (n != 64) continue;
        var exposed = new MemoryStream(payload, 0, payload.Length, false, true);
        byte[] ReadExposed()
        {
            exposed.Position = 0;
            return Complete(context == null ? SharpPackSerializer.DeserializeAsync<byte[]>(exposed) : SharpPackSerializer.DeserializeAsync<byte[]>(exposed, context))!;
        }
        var unknown = new UnknownLengthStream(payload);
        byte[] ReadUnknown()
        {
            unknown.Reset();
            return Complete(context == null ? SharpPackSerializer.DeserializeAsync<byte[]>(unknown) : SharpPackSerializer.DeserializeAsync<byte[]>(unknown, context))!;
        }
        Require(ReadExposed().SequenceEqual(value) && ReadUnknown().SequenceEqual(value), "Unframed streams");
        Add($"StreamExposed{n}.{mode.Name}", () => Sinks.Value = ReadExposed(), payload, isLimited);
        Add($"StreamUnknown{n}.{mode.Name}", () => Sinks.Value = ReadUnknown(), payload, isLimited);
        byte[] ReadPipe()
        {
            var pipe = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline,
                pauseWriterThreshold: long.MaxValue, resumeWriterThreshold: long.MaxValue, useSynchronizationContext: false));
            try
            {
                pipe.Writer.Write(payload);
                Complete(pipe.Writer.FlushAsync());
                return Complete(SharpPackStreamingSerializer.DeserializeFrameAsync<byte[]>(pipe.Reader, payload.Length, context))!;
            }
            finally { pipe.Reader.Complete(); pipe.Writer.Complete(); }
        }
        Require(ReadPipe().SequenceEqual(value), "Pipe frame");
        Add($"PipeConstructRead{n}.{mode.Name}", () => Sinks.Value = ReadPipe(), payload, isLimited);
    }
}

void VerifyRejects()
{
    void Rejected(Action action, string expected)
    {
        try { action(); throw new InvalidOperationException("Limit did not reject"); }
        catch (SharpPackSerializationException ex)
        {
            Require(ex.GetType().GetProperty("ReadLimitKind")!.GetValue(ex)!.ToString() == expected, "Wrong limit failure");
        }
    }
    var values = Enumerable.Range(0, 256).ToArray();
    byte[] bytes = Encode(values, null);
    int[]? destination = new[] { 99 };
    var original = destination;
    Rejected(() => SharpPackSerializer.Deserialize(bytes, ref destination, Limits(collection: 255)), "CollectionLength");
    Require(ReferenceEquals(destination, original) && destination[0] == 99, "Rejected destination changed");
    var stream = new MemoryStream(bytes);
    Rejected(() => Complete(SharpPackSerializer.DeserializeAsync<int[]>(stream, bytes.Length, Limits(payload: bytes.Length - 1))), "PayloadBytes");
    Require(stream.Position == 0, "Oversized frame was consumed");
    Rejected(() => SharpPackSerializer.Deserialize<DepthNode>(Encode(Depth(32), null), Limits(depth: 32)), "Depth");
    Rejected(() => SharpPackSerializer.Deserialize<ReferenceNode>(Encode(Cycle(32), null), Limits(references: 31)), "ReferenceCount");
    Console.WriteLine("LIMIT_CHECKS payload-before-read,collection-before-replacement,depth,reference:passed");
}

void Add(string name, Action action, ReadOnlyMemory<byte> payload, bool isLimited) => cases.Add(new Case(name, action, isLimited, Convert.ToHexString(SHA256.HashData(payload.Span))));
static byte[] Encode<T>(T value, SharpPackSerializerContext? context) => context == null ? SharpPackSerializer.Serialize(value) : SharpPackSerializer.Serialize(value, context);
static T Complete<T>(ValueTask<T> task)
{
    if (!task.IsCompleted) throw new InvalidOperationException("Fixture must complete synchronously for allocation accounting");
    return task.GetAwaiter().GetResult();
}
static SharpPackSerializerContext Limits(int depth = 128, int collection = 1048576, int references = 128, int payload = 2097152)
{
    var property = typeof(SharpPackSerializerConfiguration).GetProperty("ReadLimits")!;
    object limits = Activator.CreateInstance(property.PropertyType)!;
    foreach (var pair in new[] { ("MaxDepth", depth), ("MaxCollectionLength", collection), ("MaxReferenceCount", references) })
    {
        var member = property.PropertyType.GetProperty(pair.Item1)!;
        member.SetValue(limits, pair.Item2);
        Require((int)member.GetValue(limits)! == pair.Item2, "Limit reflection setup mismatch");
    }
    object config = SharpPackSerializerConfiguration.Default;
    property.SetValue(config, limits);
    var maxPayload = typeof(SharpPackSerializerConfiguration).GetProperty("MaxPayloadBytes")!;
    maxPayload.SetValue(config, payload);
    Require((int)maxPayload.GetValue(config)! == payload && property.GetValue(config)!.Equals(limits), "Configuration setup mismatch");
    return new SharpPackSerializerContext((SharpPackSerializerConfiguration)config);
}
static unsafe Memory<byte> Aligned(byte[] bytes)
{
    var buffer = GC.AllocateArray<byte>(bytes.Length + 63, pinned: true);
    int offset;
    fixed (byte* pointer = buffer) offset = (int)((64 - ((nuint)pointer & 63)) & 63);
    bytes.CopyTo(buffer, offset);
    return buffer.AsMemory(offset, bytes.Length);
}
static DepthNode Depth(int count) => count == 0 ? null! : new DepthNode { Next = Depth(count - 1) };
static int CountDepth(DepthNode? node) { int count = 0; while (node != null) { count++; node = node.Next; } return count; }
static ReferenceNode Cycle(int count)
{
    var first = new ReferenceNode(); var last = first;
    for (int i = 1; i < count; i++) { last.Next = new ReferenceNode(); last = last.Next; }
    last.Next = first; return first;
}
static int CountCycle(ReferenceNode first)
{
    int count = 1; var next = first.Next;
    while (next != first && next != null && count < 1000) { count++; next = next.Next; }
    Require(ReferenceEquals(next, first), "Reference cycle not restored"); return count;
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
record Mode(string Name, SharpPackSerializerContext? Context);
record Case(string Name, Action Run, bool Limited, string PayloadHash);
static class Sinks { public static object? Value; public static int Number; }
sealed class Segment : ReadOnlySequenceSegment<byte>
{
    public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
    public Segment Append(ReadOnlyMemory<byte> memory) { var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length }; Next = next; return next; }
}
sealed class BytesFormatter : SharpPackFormatter<byte[]>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref byte[]? value) => writer.WriteUnmanagedArray(value);
    public override void Deserialize(ref SharpPackReader reader, scoped ref byte[]? value) => reader.ReadUnmanagedArray(ref value);
}
sealed class UnknownLengthStream(byte[] bytes) : Stream
{
    int position;
    public void Reset() => position = 0;
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) { int n = Math.Min(buffer.Length, bytes.Length - position); bytes.AsSpan(position, n).CopyTo(buffer); position += n; return n; }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Read(buffer.Span));
    public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
[SharpPackable] public partial class DepthNode { public DepthNode? Next { get; set; } }
[SharpPackable] public partial class Mixed { public int Id { get; set; } public string Name { get; set; } = ""; public int[] Values { get; set; } = []; }
[SharpPackable(GenerateType.CircularReference)] public partial class ReferenceNode { [SharpPackOrder(0)] public ReferenceNode? Next { get; set; } }
