using System.Runtime.CompilerServices;

namespace SharpPack.Formatters;

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
            SharpPackSerializationException.ThrowInvalidLength(elementCount);
        }

        var byteCount = (long)elementCount * Unsafe.SizeOf<T>();
        if (byteCount > int.MaxValue)
        {
            SharpPackSerializationException.ThrowSizeOverflow();
        }
        return (int)byteCount;
    }

    public static int AddHeader(int byteCount)
    {
        if (byteCount > int.MaxValue - 4)
        {
            SharpPackSerializationException.ThrowSizeOverflow();
        }
        return byteCount + 4;
    }

    static void ValidateDimension(int dimension)
    {
        if (dimension < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(dimension);
        }
    }

    static int ValidateProduct(int payloadLength, long product)
    {
        if (product > int.MaxValue)
        {
            SharpPackSerializationException.ThrowSizeOverflow();
        }
        if (payloadLength != product)
        {
            SharpPackSerializationException.ThrowMessage(
                $"The multidimensional array declares {payloadLength} elements " +
                $"but its dimensions require {product}.");
        }
        return (int)product;
    }
}
