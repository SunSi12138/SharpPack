namespace SidecarFixture;

// This assembly has no reference to SharpPack.
public sealed class ExternalNote
{
    public int Count { get; init; }
    public string? Text { get; init; }
}

public sealed record ExternalRecord(string? Caption);

public struct ExternalValue
{
    public string? Label { get; init; }
}

public sealed class ExternalEnvelope<T>
{
    public Dictionary<string, T[]>? Messages { get; init; }
    public ExternalValue Value { get; init; }
    public ExternalRecord? Title { get; init; }
}
