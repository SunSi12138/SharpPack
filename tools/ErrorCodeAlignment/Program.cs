using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpPack;

Console.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}");
foreach (var inputAlignment in new[] { 4, 20 })
foreach (var outputAlignment in new[] { 0, 16 })
{
    var input = AllocateString(inputAlignment);
    var output = AllocateWriter(outputAlignment);
    var text = input.Value;
    var writer = output.Value;
    try
    {
        Func<object?> run = () => { writer.Clear(); SharpPackSerializer.Serialize(ref writer, text); return writer; };
        run();
        if (SharpPackSerializer.Deserialize<string>(writer.WrittenSpan) != text) throw new Exception("Roundtrip failed");
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 1500) run();
        var pilot = Stopwatch.StartNew(); int count = 0;
        do { for (int i=0;i<128;i++) run(); count+=128; } while (pilot.ElapsedMilliseconds < 200);
        var timings = new List<double>(); var allocations = new List<double>();
        for(int sample=0;sample<9;sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if ((input.Handle.AddrOfPinnedObject().ToInt64() & 63) != inputAlignment || (output.Handle.AddrOfPinnedObject().ToInt64() & 63) != outputAlignment) throw new Exception("Alignment changed");
            long before=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
            for(int i=0;i<count;i++) run();
            long elapsed=Stopwatch.GetTimestamp()-start, allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            timings.Add(elapsed*1e9/Stopwatch.Frequency/count); allocations.Add(allocated/(double)count);
        }
        timings.Sort(); allocations.Sort();
        Console.WriteLine($"RESULT,I{inputAlignment}O{outputAlignment},{timings[4]:F3},{allocations[4]:F3},{timings[0]:F3},{timings[8]:F3},{count}");
        GC.KeepAlive(text); GC.KeepAlive(writer);
    }
    finally { input.Handle.Free(); output.Handle.Free(); }
}

static (string Value, GCHandle Handle) AllocateString(int alignment)
{
    for (int i=0;i<256;i++)
    {
        var value=new string('x',1024);
        var handle=GCHandle.Alloc(value,GCHandleType.Pinned);
        if ((handle.AddrOfPinnedObject().ToInt64() & 63)==alignment) return (value,handle);
        handle.Free();
    }
    throw new Exception("Could not establish requested input alignment");
}
static (ArrayBufferWriter<byte> Value, GCHandle Handle) AllocateWriter(int alignment)
{
    for (int i=0;i<256;i++)
    {
        var value=new ArrayBufferWriter<byte>(4096);
        if(!MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)value.GetMemory(),out var segment)) throw new Exception("No backing array");
        var handle=GCHandle.Alloc(segment.Array!,GCHandleType.Pinned);
        if ((handle.AddrOfPinnedObject().ToInt64() & 63)==alignment) return (value,handle);
        handle.Free();
        // Vary object-size increments instead of assuming allocator placement.
        GC.KeepAlive(new byte[i%8+1]);
    }
    throw new Exception("Could not establish requested output alignment");
}
