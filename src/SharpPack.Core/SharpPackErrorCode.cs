namespace SharpPack;

/// <summary>Stable serialization failure categories. Numeric values must never be reused or renumbered.</summary>
public enum SharpPackErrorCode
{
    Unknown = 0,
    UnexpectedEndOfPayload = 1,
    InvalidLength = 2,
    SizeOverflow = 3,
    InvalidWireData = 4,
    InvalidEncoding = 5,
    DepthLimitExceeded = 6,
    ResourceLimitExceeded = 7,
    FormatterResolutionFailed = 8,
    TypeResolutionFailed = 9,
    FrameLengthMismatch = 10,
    RecursiveFormatterConstruction = 11,
}
