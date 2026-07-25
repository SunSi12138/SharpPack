namespace SharpPack;

/// <summary>
/// Process-wide runtime resource settings for the default byte-array serializer.
/// Configure once during application startup, before the first serialization.
/// </summary>
public sealed record SharpPackSerializerRuntimeOptions
{
    public const int DefaultThreadBufferSize = 8 * 1024;
    public const int HighThroughputThreadBufferSize = 80 * 1024;

    public static SharpPackSerializerRuntimeOptions Default { get; } = new();

    public static SharpPackSerializerRuntimeOptions HighThroughput { get; } =
        new()
        {
            ThreadBufferSize = HighThroughputThreadBufferSize,
        };

    /// <summary>
    /// Per-thread first buffer retained by <c>Serialize&lt;T&gt;()</c>.
    /// Larger values avoid pooled segments for correspondingly sized payloads.
    /// </summary>
    public int ThreadBufferSize { get; init; } = DefaultThreadBufferSize;

    /// <summary>
    /// Allocates the retained per-thread buffer in the pinned object heap.
    /// This is intended only for workloads that require stable native addresses.
    /// </summary>
    public bool PinThreadBuffer { get; init; }
}
