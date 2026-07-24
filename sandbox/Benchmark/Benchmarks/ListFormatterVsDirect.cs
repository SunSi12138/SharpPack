using Benchmark.BenchmarkNetUtilities;
using Benchmark.Models;
using SharpPack;
using SharpPack.Formatters;
using Orleans.Serialization.Buffers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Benchmark.Benchmarks;

public class ListFormatterVsDirect
{
    List<MyClass> value;
    byte[] bytes;
    ISharpPackFormatter<List<MyClass?>> formatter;
    ArrayBufferWriter<byte> buffer;
    SharpPackWriterOptionalStateLease state;
    SharpPackReaderOptionalStateLease state2;

    public ListFormatterVsDirect()
    {
        value = Enumerable.Range(0, 100)
            .Select(_ => new MyClass { X = 100, Y = 99999999, Z = 4444, FirstName = "Hoge Huga Tako", LastName = "あいうえおかきくけこ" })
            .ToList();
        bytes = SharpPackSerializer.Serialize(value);
        formatter = new ListFormatter<MyClass>();
        buffer = new ArrayBufferWriter<byte>(bytes.Length);

        state = SharpPackWriterOptionalStatePool.Rent(null);
        state2 = SharpPackReaderOptionalStatePool.Rent(null);
    }

    [Benchmark, BenchmarkCategory(Categories.Serialize)]
    public void SerializeFormatter()
    {
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref buffer, state);
        formatter.Serialize(ref writer, ref value!);
        writer.Flush();
        buffer.Clear();
    }

    [Benchmark, BenchmarkCategory(Categories.Serialize)]
    public void SerializePackable()
    {
        var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(ref buffer, state);
        SharpPack.Formatters.ListFormatter.SerializePackable(ref writer, value!);
        writer.Flush();
        buffer.Clear();
    }


    [Benchmark, BenchmarkCategory(Categories.Deserialize)]
    public void DeserializeFormatter()
    {
        List<MyClass?>? list = null;
        var reader = new SharpPackReader(bytes, state2);
        //reader.ReadPackableArray
        // var a = SharpPack.Formatters.ListFormatter.DeserializePackable<(ref reader);
        formatter.Deserialize(ref reader, ref list);
        reader.Dispose();
    }

    [Benchmark, BenchmarkCategory(Categories.Deserialize)]
    public void DeserializePackable()
    {
        List<MyClass?>? list = null;
        var reader = new SharpPackReader(bytes, state2);
        ListFormatter.DeserializePackable(ref reader, ref list!);
        reader.Dispose();
    }
}
