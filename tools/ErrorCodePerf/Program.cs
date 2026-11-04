using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using SharpPack;

Console.WriteLine($"ENV runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; cpuCount={Environment.ProcessorCount}; serverGC={GCSettings.IsServerGC}; frequency={Stopwatch.Frequency}");
Console.WriteLine("Protocol: single-process warmed microbenchmark, 7 timed samples >=100ms, median ns/op; allocations via per-thread counter. Not BenchmarkDotNet or a regression gate.");
var cases = new List<(string Name, Func<object?> Run)>();
foreach (var n in new[] {64, 4096, 65536})
{
    var value = Enumerable.Range(0,n).Select(i=>(byte)i).ToArray();
    var payload = SharpPackSerializer.Serialize(value);
    var a = new Segment(payload.AsMemory(0,payload.Length/2));
    var b = new Segment(payload.AsMemory(payload.Length/2)); a.Add(b);
    var sequence = new ReadOnlySequence<byte>(a,0,b,b.Memory.Length);
    if (!SharpPackSerializer.Deserialize<byte[]>(sequence)!.SequenceEqual(value)) throw new Exception("Sequence correctness failed");
    cases.Add(($"BytesSpan{n}", () => SharpPackSerializer.Deserialize<byte[]>(payload)));
    cases.Add(($"BytesSequence{n}", () => SharpPackSerializer.Deserialize<byte[]>(sequence)));
}
var nested = Enumerable.Range(0,32).Select(i=>Enumerable.Range(i,32).ToArray()).ToArray();
var nestedPayload = SharpPackSerializer.Serialize(nested);
if (!SharpPackSerializer.Deserialize<int[][]>(nestedPayload)!.SelectMany(x=>x).SequenceEqual(nested.SelectMany(x=>x))) throw new Exception("Nested correctness failed");
cases.Add(("NestedArrays32x32", () => SharpPackSerializer.Deserialize<int[][]>(nestedPayload)));
var strings = Enumerable.Range(0,128).Select(i=>"value"+i).ToList();
var stringsPayload = SharpPackSerializer.Serialize(strings);
if (!SharpPackSerializer.Deserialize<List<string>>(stringsPayload)!.SequenceEqual(strings)) throw new Exception("List correctness failed");
cases.Add(("StringList128", () => SharpPackSerializer.Deserialize<List<string>>(stringsPayload)));
// Both pinned revisions contain the .NET 10-safe BitArray formatter.
foreach (var n in new[]{32,4096,65536})
{
    var bits = new BitArray(n); for(int i=0;i<n;i+=3) bits[i]=true;
    var payload=SharpPackSerializer.Serialize(bits);
    var restored=SharpPackSerializer.Deserialize<BitArray>(payload)!;
    if(restored.Length!=n || Enumerable.Range(0,n).Any(i=>restored[i]!=bits[i])) throw new Exception("BitArray correctness failed");
    var writer=new ArrayBufferWriter<byte>(payload.Length);
    cases.Add(($"BitArrayWriter{n}", ()=>{writer.Clear(); SharpPackSerializer.Serialize(ref writer,bits); return writer;}));
}
var text = new string('x', 1024);
var textPayload = SharpPackSerializer.Serialize(text);
var output = new ArrayBufferWriter<byte>(4096);
cases.Add(("StringUtf8Read1024", () => SharpPackSerializer.Deserialize<string>(textPayload)));
cases.Add(("StringUtf8Write1024", () => { output.Clear(); SharpPackSerializer.Serialize(ref output, text); return output; }));
var scalarPayload = SharpPackSerializer.Serialize(123);
cases.Add(("ScalarRead", () => SharpPackSerializer.Deserialize<int>(scalarPayload)));
cases.Add(("ScalarWrite", () => { output.Clear(); SharpPackSerializer.Serialize(ref output, 123); return output; }));
object? sink=null;
foreach(var item in cases)
{
    var warm=Stopwatch.StartNew(); while(warm.ElapsedMilliseconds<700) sink=item.Run();
    var pilot=Stopwatch.StartNew(); int count=0; do { for(int i=0;i<128;i++)sink=item.Run(); count+=128; }while(pilot.ElapsedMilliseconds<100);
    var iterations=Math.Max(128,count);
    var timings=new List<double>(); var allocations=new List<double>();
    for(int sample=0;sample<7;sample++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
        for(int i=0;i<iterations;i++) sink=item.Run();
        long end=Stopwatch.GetTimestamp(), after=GC.GetAllocatedBytesForCurrentThread();
        timings.Add((end-start)*1e9/Stopwatch.Frequency/iterations); allocations.Add((after-before)/(double)iterations);
    }
    timings.Sort();allocations.Sort();
    Console.WriteLine($"RESULT,{item.Name},{timings[3]:F3},{allocations[3]:F3},{timings[0]:F3},{timings[6]:F3},{iterations}");
}
GC.KeepAlive(sink);
sealed class Segment:ReadOnlySequenceSegment<byte>
{
    public Segment(ReadOnlyMemory<byte> memory){Memory=memory;}
    public void Add(Segment next){next.RunningIndex=RunningIndex+Memory.Length;Next=next;}
}
