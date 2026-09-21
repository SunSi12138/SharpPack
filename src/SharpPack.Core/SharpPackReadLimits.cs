namespace SharpPack;

/// <summary>
/// Bounds deserialization amplification at aggregate reader boundaries.
/// </summary>
/// <remarks>
/// Limits are inclusive. Zero is the current-compatible sentinel so
/// default(struct) configuration remains compatible with earlier releases.
/// </remarks>
public readonly record struct SharpPackReadLimits
{
    internal const int CurrentCompatibleMaxDepth = 999;

    public static SharpPackReadLimits Default => new()
    {
        MaxDepth = CurrentCompatibleMaxDepth,
        MaxCollectionLength = int.MaxValue,
        MaxReferenceCount = int.MaxValue,
    };

    public int MaxDepth { get; init; }

    public int MaxCollectionLength { get; init; }

    public int MaxReferenceCount { get; init; }

    internal SharpPackReadLimits Normalize()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCollectionLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxReferenceCount);

        return new SharpPackReadLimits
        {
            MaxDepth = MaxDepth == 0
                ? CurrentCompatibleMaxDepth
                : MaxDepth,
            MaxCollectionLength = MaxCollectionLength == 0
                ? int.MaxValue
                : MaxCollectionLength,
            MaxReferenceCount = MaxReferenceCount == 0
                ? int.MaxValue
                : MaxReferenceCount,
        };
    }
}
