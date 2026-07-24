using MemoryPack;

namespace Benchmark;

internal static class BenchmarkContexts
{
    internal static MemoryPackSerializerContext Utf8 { get; } =
        new(MemoryPackSerializerConfiguration.Utf8);

    internal static MemoryPackSerializerContext Utf16 { get; } =
        new(MemoryPackSerializerConfiguration.Utf16);
}
