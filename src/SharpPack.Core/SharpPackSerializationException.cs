using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace SharpPack;

public class SharpPackSerializationException : Exception
{
    public SharpPackSerializationException(string message)
        : base(message)
    {
    }

    public SharpPackSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Stable machine-readable classification; do not parse Message.</summary>
    public SharpPackErrorCode ErrorCode { get; }

    public SharpPackSerializationException(SharpPackErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public SharpPackSerializationException(SharpPackErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    internal static SharpPackErrorCode ClassifyFormatterFailure(Exception exception)
    {
        // Reflection-based discovery may add one or more invocation wrappers.
        while (exception is System.Reflection.TargetInvocationException { InnerException: { } inner })
        {
            exception = inner;
        }
        return exception is SharpPackSerializationException { ErrorCode: SharpPackErrorCode.RecursiveFormatterConstruction }
            ? SharpPackErrorCode.RecursiveFormatterConstruction
            : SharpPackErrorCode.FormatterResolutionFailed;
    }

    [DoesNotReturn]
    public static void ThrowMessage(string message)
    {
        throw new SharpPackSerializationException(message);
    }

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(byte expected, byte actual)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData, $"Current object's property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(Type type, byte expected, byte actual)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData, $"{type.FullName} property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidCollection()
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData, $"Current read to collection, the buffer header is not collection.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidRange(int expected, int actual)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.UnexpectedEndOfPayload, $"Requires size is {expected} but buffer length is {actual}.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidAdvance()
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.UnexpectedEndOfPayload, $"Cannot advance past the end of the buffer.");
    }

    [DoesNotReturn]
    internal static void ThrowInvalidWriteAdvance()
    {
        throw new SharpPackSerializationException("Cannot advance past the end of the buffer.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidLength(int length)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidLength, $"Length must be non-negative, actual: {length}.");
    }

    [DoesNotReturn]
    public static void ThrowSizeOverflow()
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.SizeOverflow, "The requested serialization size exceeds the supported buffer size.");
    }

    [DoesNotReturn]
    public static void ThrowSequenceReachedEnd()
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.UnexpectedEndOfPayload, $"Sequence reached end, reader can not provide more buffer.");
    }

    [DoesNotReturn]
    public static void ThrowWriteInvalidMemberCount(byte memberCount)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidLength, $"MemberCount/Tag allows < 250 but try to write {memberCount}.");
    }

    [DoesNotReturn]
    public static void ThrowInsufficientBufferUnless(int length)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.UnexpectedEndOfPayload, $"Length header size is larger than buffer size, length: {length}.");
    }

    [DoesNotReturn]
    public static void ThrowNotFoundInUnionType(Type actualType, Type baseType)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.TypeResolutionFailed, $"Type {actualType.FullName} is not annotated in {baseType.FullName} SharpPackUnion.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidTag(ushort tag, Type baseType)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData, $"Data read tag: {tag} but not found in {baseType.FullName} SharpPackUnion annotations.");
    }

    [DoesNotReturn]
    public static void ThrowReachedDepthLimit(Type type)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.DepthLimitExceeded, $"Serializing Type '{type}' reached depth limit, maybe detect circular reference.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidConcurrrentCollectionOperation()
    {
        throw new SharpPackSerializationException($"ConcurrentCollection is Added/Removed in serializing, however serialize concurrent collection is not thread-safe.");
    }

    [DoesNotReturn]
    public static void ThrowDeserializeObjectIsNull(string target)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData, $"Deserialized {target} is null.");
    }

    [DoesNotReturn]
    public static void ThrowFailedEncoding(OperationStatus status)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidEncoding, $"Failed in Utf8 encoding/decoding process, status: {status}.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidEncodingLength()
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.InvalidEncoding, "The encoded payload length does not match its string header.");
    }

    [DoesNotReturn]
    public static void ThrowCompressionFailed(OperationStatus status)
    {
        throw new SharpPackSerializationException($"Failed in Brotli compression/decompression process, status: {status}.");
    }

    [DoesNotReturn]
    public static void ThrowCompressionFailed()
    {
        throw new SharpPackSerializationException($"Failed in Brotli compression/decompression process.");
    }

    [DoesNotReturn]
    public static void ThrowAlreadyDecompressed()
    {
        throw new SharpPackSerializationException($"BrotliDecompressor can not invoke Decompress twice, already invoked.");
    }

    [DoesNotReturn]
    public static void ThrowDecompressionSizeLimitExceeded(int limit, int size)
    {
        throw new SharpPackSerializationException(SharpPackErrorCode.ResourceLimitExceeded, $"In decompress process, limit is {limit} but target size is {size}.");
    }
}
