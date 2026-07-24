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

    [DoesNotReturn]
    public static void ThrowMessage(string message)
    {
        throw new SharpPackSerializationException(message);
    }

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(byte expected, byte actual)
    {
        throw new SharpPackSerializationException($"Current object's property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(Type type, byte expected, byte actual)
    {
        throw new SharpPackSerializationException($"{type.FullName} property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidCollection()
    {
        throw new SharpPackSerializationException($"Current read to collection, the buffer header is not collection.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidRange(int expected, int actual)
    {
        throw new SharpPackSerializationException($"Requires size is {expected} but buffer length is {actual}.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidAdvance()
    {
        throw new SharpPackSerializationException($"Cannot advance past the end of the buffer.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidLength(int length)
    {
        throw new SharpPackSerializationException($"Length must be non-negative, actual: {length}.");
    }

    [DoesNotReturn]
    public static void ThrowSizeOverflow()
    {
        throw new SharpPackSerializationException("The requested serialization size exceeds the supported buffer size.");
    }

    [DoesNotReturn]
    public static void ThrowSequenceReachedEnd()
    {
        throw new SharpPackSerializationException($"Sequence reached end, reader can not provide more buffer.");
    }

    [DoesNotReturn]
    public static void ThrowWriteInvalidMemberCount(byte memberCount)
    {
        throw new SharpPackSerializationException($"MemberCount/Tag allows < 250 but try to write {memberCount}.");
    }

    [DoesNotReturn]
    public static void ThrowInsufficientBufferUnless(int length)
    {
        throw new SharpPackSerializationException($"Length header size is larger than buffer size, length: {length}.");
    }

    [DoesNotReturn]
    public static void ThrowNotFoundInUnionType(Type actualType, Type baseType)
    {
        throw new SharpPackSerializationException($"Type {actualType.FullName} is not annotated in {baseType.FullName} SharpPackUnion.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidTag(ushort tag, Type baseType)
    {
        throw new SharpPackSerializationException($"Data read tag: {tag} but not found in {baseType.FullName} SharpPackUnion annotations.");
    }

    [DoesNotReturn]
    public static void ThrowReachedDepthLimit(Type type)
    {
        throw new SharpPackSerializationException($"Serializing Type '{type}' reached depth limit, maybe detect circular reference.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidConcurrrentCollectionOperation()
    {
        throw new SharpPackSerializationException($"ConcurrentCollection is Added/Removed in serializing, however serialize concurrent collection is not thread-safe.");
    }

    [DoesNotReturn]
    public static void ThrowDeserializeObjectIsNull(string target)
    {
        throw new SharpPackSerializationException($"Deserialized {target} is null.");
    }

    [DoesNotReturn]
    public static void ThrowFailedEncoding(OperationStatus status)
    {
        throw new SharpPackSerializationException($"Failed in Utf8 encoding/decoding process, status: {status}.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidEncodingLength()
    {
        throw new SharpPackSerializationException("The encoded payload length does not match its string header.");
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
        throw new SharpPackSerializationException($"In decompress process, limit is {limit} but target size is {size}.");
    }
}
