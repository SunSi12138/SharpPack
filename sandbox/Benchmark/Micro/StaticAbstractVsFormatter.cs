using SharpPack;
using System.Buffers;

namespace Benchmark.Micro;

public class StaticAbstractVsFormatter
{
    IntClass value;
    ArrayBufferWriter<byte> bufferWriter;
    ISharpPackFormatter<IntClass> formatter;

    public StaticAbstractVsFormatter()
    {
        this.value = new IntClass { Value = 999999 };
        this.bufferWriter = new ArrayBufferWriter<byte>(99999);

        using var state = SharpPackWriterOptionalStatePool.Rent(null);
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref bufferWriter, state);
        this.formatter = writer.GetFormatter<IntClass>();
    }

    [Benchmark]
    public void WriteValue()
    {
        bufferWriter.Clear();
        using var state = SharpPackWriterOptionalStatePool.Rent(null);
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref bufferWriter, state);
        
        writer.WriteValue(value); // GetFormatter<T>.Serialize(ref writer, ref value);
    }

    [Benchmark]
    public void FormatterSerialize()
    {
        bufferWriter.Clear();
        using var state = SharpPackWriterOptionalStatePool.Rent(null);
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref bufferWriter, state);
        formatter.Serialize(ref writer, ref value!); // ISharpPackFormatter<T>.Serialize(ref writer, rf value)
    }

    [Benchmark(Baseline = true)]
    public void WritePackable()
    {
        bufferWriter.Clear();
        using var state = SharpPackWriterOptionalStatePool.Rent(null);
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref bufferWriter, state);
        writer.WritePackable(value); // T.Serialize(ref writer, ref value);
    }

    [Benchmark]
    public void Direct()
    {
        bufferWriter.Clear();
        using var state = SharpPackWriterOptionalStatePool.Rent(null);
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref bufferWriter, state);
        writer.WriteUnmanagedWithObjectHeader(1, value.Value);
    }















}

[SharpPackable]
public partial class IntClass
{
    public int Value { get; set; }
}


[SharpPackable]
public partial class IntClass2
{
    public int Value { get; set; }
}

