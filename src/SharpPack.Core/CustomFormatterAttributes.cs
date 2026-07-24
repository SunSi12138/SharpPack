using SharpPack.Compression;
using SharpPack.Formatters;

namespace SharpPack;


public sealed class Utf8StringFormatterAttribute : SharpPackCustomFormatterAttribute<Utf8StringFormatter, string>
{
    public override Utf8StringFormatter GetFormatter()
    {
        return Utf8StringFormatter.Default;
    }
}

public sealed class Utf16StringFormatterAttribute : SharpPackCustomFormatterAttribute<Utf16StringFormatter, string>
{
    public override Utf16StringFormatter GetFormatter()
    {
        return Utf16StringFormatter.Default;
    }
}

public sealed class OrdinalIgnoreCaseStringDictionaryFormatter<TValue> : SharpPackCustomFormatterAttribute<DictionaryFormatter<string, TValue?>, Dictionary<string, TValue?>>
{
    static readonly DictionaryFormatter<string, TValue?> formatter = new DictionaryFormatter<string, TValue?>(StringComparer.OrdinalIgnoreCase);

    public override DictionaryFormatter<string, TValue?> GetFormatter()
    {
        return formatter;
    }
}

public sealed class InternStringFormatterAttribute : SharpPackCustomFormatterAttribute<InternStringFormatter, string>
{
    public override InternStringFormatter GetFormatter()
    {
        return InternStringFormatter.Default;
    }
}

public sealed class BitPackFormatterAttribute : SharpPackCustomFormatterAttribute<BitPackFormatter, bool[]>
{
    public override BitPackFormatter GetFormatter()
    {
        return BitPackFormatter.Default;
    }
}

public sealed class BrotliFormatterAttribute : SharpPackCustomFormatterAttribute<BrotliFormatter, byte[]>
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }
    public int DecompressionSizeLimit { get; }

    public BrotliFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = BrotliUtils.WindowBits_Default, int decompressionSizeLimit = BrotliFormatter.DefaultDecompssionSizeLimit)
    {
        this.CompressionLevel = compressionLevel;
        this.Window = window;
        this.DecompressionSizeLimit = decompressionSizeLimit;
    }

    public override BrotliFormatter GetFormatter()
    {
        return new BrotliFormatter(CompressionLevel, Window, DecompressionSizeLimit);
    }
}

public sealed class BrotliFormatterAttribute<T> : SharpPackCustomFormatterAttribute<BrotliFormatter<T>, T>
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }

    public BrotliFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = BrotliUtils.WindowBits_Default)
    {
        this.CompressionLevel = compressionLevel;
        this.Window = window;
    }

    public override BrotliFormatter<T> GetFormatter()
    {
        return new BrotliFormatter<T>(CompressionLevel, Window);
    }
}

public sealed class BrotliStringFormatterAttribute : SharpPackCustomFormatterAttribute<BrotliStringFormatter, string>
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }
    public int DecompressionSizeLimit { get; }

    public BrotliStringFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = BrotliUtils.WindowBits_Default, int decompressionSizeLimit = BrotliFormatter.DefaultDecompssionSizeLimit)
    {
        this.CompressionLevel = compressionLevel;
        this.Window = window;
        this.DecompressionSizeLimit = decompressionSizeLimit;
    }

    public override BrotliStringFormatter GetFormatter()
    {
        return new BrotliStringFormatter(CompressionLevel, Window, DecompressionSizeLimit);
    }
}

public sealed class MemoryPoolFormatterAttribute<T> : SharpPackCustomFormatterAttribute<MemoryPoolFormatter<T>, Memory<T?>>
{
    static readonly MemoryPoolFormatter<T> formatter = new MemoryPoolFormatter<T>();

    public override MemoryPoolFormatter<T> GetFormatter()
    {
        return formatter;
    }
}

public sealed class ReadOnlyMemoryPoolFormatterAttribute<T> : SharpPackCustomFormatterAttribute<ReadOnlyMemoryPoolFormatter<T>, ReadOnlyMemory<T?>>
{
    static readonly ReadOnlyMemoryPoolFormatter<T> formatter = new ReadOnlyMemoryPoolFormatter<T>();

    public override ReadOnlyMemoryPoolFormatter<T> GetFormatter()
    {
        return formatter;
    }
}
