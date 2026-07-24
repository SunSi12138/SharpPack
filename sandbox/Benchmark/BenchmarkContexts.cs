using SharpPack;

namespace Benchmark;

internal static class BenchmarkContexts
{
    internal static SharpPackSerializerContext Utf8 { get; } =
        new(SharpPackSerializerConfiguration.Utf8);

    internal static SharpPackSerializerContext Utf16 { get; } =
        new(SharpPackSerializerConfiguration.Utf16);
}
