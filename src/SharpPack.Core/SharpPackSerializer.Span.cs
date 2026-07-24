using SharpPack.Internal;

namespace SharpPack;

public static partial class SharpPackSerializer
{
    /// <summary>
    /// Serializes directly into the caller-provided destination.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the destination was large enough; otherwise
    /// <see langword="false"/> and <paramref name="written"/> is zero.
    /// </returns>
    public static unsafe bool TrySerialize<T>(
        Span<byte> destination,
        scoped in T? value,
        out int written)
    {
        fixed (byte* pointer = destination)
        {
            var bufferWriter = new SpanBufferWriter(
                pointer,
                destination.Length);
            try
            {
                written = Serialize(ref bufferWriter, value);
                return true;
            }
            catch (InsufficientDestinationBufferException)
            {
                written = 0;
                return false;
            }
        }
    }

    /// <summary>
    /// Serializes directly into the caller-provided destination using a
    /// serializer context.
    /// </summary>
    public static unsafe bool TrySerialize<T>(
        Span<byte> destination,
        scoped in T? value,
        SharpPackSerializerContext context,
        out int written)
    {
        ArgumentNullException.ThrowIfNull(context);
        fixed (byte* pointer = destination)
        {
            var bufferWriter = new SpanBufferWriter(
                pointer,
                destination.Length);
            try
            {
                written = Serialize(ref bufferWriter, value, context);
                return true;
            }
            catch (InsufficientDestinationBufferException)
            {
                written = 0;
                return false;
            }
        }
    }
}
