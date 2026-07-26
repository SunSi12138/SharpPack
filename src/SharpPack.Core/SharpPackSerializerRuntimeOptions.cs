namespace SharpPack;

/// <summary>
/// Process-wide runtime resource settings for ordinary byte-array return paths,
/// including calls that use an explicit serializer context.
/// Configure once during application startup, before the first retained
/// byte-array serializer state is created.
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
    /// Per-thread first buffer retained by ordinary
    /// <c>Serialize&lt;T&gt;()</c> byte-array return paths.
    /// Larger values avoid pooled segments for correspondingly sized payloads.
    /// Any value from 1 through <see cref="Array.MaxLength"/> is accepted.
    /// </summary>
    public int ThreadBufferSize { get; init; } = DefaultThreadBufferSize;

    /// <summary>
    /// Allocates the retained per-thread buffer in the pinned object heap.
    /// This is intended only for workloads that require stable native addresses.
    /// </summary>
    public bool PinThreadBuffer { get; init; }
}
