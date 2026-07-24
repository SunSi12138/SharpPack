using System.Runtime.CompilerServices;

namespace MemoryPack.Formatters;

internal static class FormatterValidation
{
    internal const int MaximumInitialCollectionCapacity = 4096;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InitialCapacity(int length)
        => Math.Min(length, MaximumInitialCollectionCapacity);

    public static int ValidateDimensions(int payloadLength, int first, int second)
    {
        ValidateDimension(first);
        ValidateDimension(second);
        return ValidateProduct(payloadLength, (long)first * second);
    }

    public static int ValidateDimensions(
        int payloadLength,
        int first,
        int second,
        int third)
    {
        ValidateDimension(first);
        ValidateDimension(second);
        ValidateDimension(third);
        return ValidateProduct(payloadLength, (long)first * second * third);
    }

    public static int ValidateDimensions(
        int payloadLength,
        int first,
        int second,
        int third,
        int fourth)
    {
        ValidateDimension(first);
        ValidateDimension(second);
        ValidateDimension(third);
        ValidateDimension(fourth);
        return ValidateProduct(
            payloadLength,
            (long)first * second * third * fourth);
    }

    public static int ByteCount<T>(int elementCount)
    {
        if (elementCount < 0)
        {
            MemoryPackSerializationException.ThrowInvalidLength(elementCount);
        }

        var byteCount = (long)elementCount * Unsafe.SizeOf<T>();
        if (byteCount > int.MaxValue)
        {
            MemoryPackSerializationException.ThrowSizeOverflow();
        }
        return (int)byteCount;
    }

    public static int AddHeader(int byteCount)
    {
        if (byteCount > int.MaxValue - 4)
        {
            MemoryPackSerializationException.ThrowSizeOverflow();
        }
        return byteCount + 4;
    }

    static void ValidateDimension(int dimension)
    {
        if (dimension < 0)
        {
            MemoryPackSerializationException.ThrowInvalidLength(dimension);
        }
    }

    static int ValidateProduct(int payloadLength, long product)
    {
        if (product > int.MaxValue)
        {
            MemoryPackSerializationException.ThrowSizeOverflow();
        }
        if (payloadLength != product)
        {
            MemoryPackSerializationException.ThrowMessage(
                $"The multidimensional array declares {payloadLength} elements " +
                $"but its dimensions require {product}.");
        }
        return (int)product;
    }
}
