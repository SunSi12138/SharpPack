using Benchmark.BenchmarkNetUtilities;
using BinaryPack.Models.Helpers;
using SharpPack;
using System.Net.Http;

namespace Benchmark.Benchmarks;

[PayloadColumn]
public class Utf16VsUtf8
{
    readonly string ascii;
    readonly string japanese;
    readonly string largeAscii;

    readonly byte[] utf16Jpn;
    readonly byte[] utf8Jpn;
    readonly byte[] utf16Ascii;
    readonly byte[] utf8Ascii;
    readonly byte[] utf16LargeAscii;
    readonly byte[] utf8LargeAscii;

    public Utf16VsUtf8()
    {
        this.japanese = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん";
        this.ascii = "abcedfghijklmnopqrstuvwxyz0123456789";
        this.utf16Jpn = SharpPackSerializer.Serialize(japanese, BenchmarkContexts.Utf16);
        this.utf8Jpn = SharpPackSerializer.Serialize(japanese, BenchmarkContexts.Utf8);
        this.utf16Ascii = SharpPackSerializer.Serialize(ascii, BenchmarkContexts.Utf16);
        this.utf8Ascii = SharpPackSerializer.Serialize(ascii, BenchmarkContexts.Utf8);

        this.largeAscii = RandomProvider.NextString(600);
        this.utf16LargeAscii = SharpPackSerializer.Serialize(largeAscii, BenchmarkContexts.Utf16);
        this.utf8LargeAscii = SharpPackSerializer.Serialize(largeAscii, BenchmarkContexts.Utf8);
    }

    [Benchmark]
    public byte[] SerializeUtf16Ascii()
    {
        return SharpPackSerializer.Serialize(ascii, BenchmarkContexts.Utf16);
    }

    [Benchmark]
    public byte[] SerializeUtf16Japanese()
    {
        return SharpPackSerializer.Serialize(japanese, BenchmarkContexts.Utf16);
    }

    [Benchmark]
    public byte[] SerializeUtf8Ascii()
    {
        return SharpPackSerializer.Serialize(ascii, BenchmarkContexts.Utf8);
    }

    [Benchmark]
    public byte[] SerializeUtf8Japanese()
    {
        return SharpPackSerializer.Serialize(japanese, BenchmarkContexts.Utf8);
    }

    [Benchmark]
    public byte[] SerializeUtf16LargeAscii()
    {
        return SharpPackSerializer.Serialize(largeAscii, BenchmarkContexts.Utf16);
    }

    [Benchmark]
    public byte[] SerializeUtf8LargeAscii()
    {
        return SharpPackSerializer.Serialize(largeAscii, BenchmarkContexts.Utf8);
    }

    [Benchmark]
    public void DeserializeUtf16Ascii()
    {
        SharpPackSerializer.Deserialize<string>(utf16Ascii);
    }

    [Benchmark]
    public void DeserializeUtf16Japanese()
    {
        SharpPackSerializer.Deserialize<string>(utf16Jpn);
    }

    [Benchmark]
    public void DeserializeUtf8Ascii()
    {
        SharpPackSerializer.Deserialize<string>(utf8Ascii);
    }

    [Benchmark]
    public void DeserializeUtf8Japanese()
    {
        SharpPackSerializer.Deserialize<string>(utf8Jpn);
    }

    [Benchmark]
    public void DeserializeUtf16LargeAscii()
    {
        SharpPackSerializer.Deserialize<string>(utf16LargeAscii);
    }

    [Benchmark]
    public void DeserializeUtf8LargeAscii()
    {
        SharpPackSerializer.Deserialize<string>(utf8LargeAscii);
    }
}
