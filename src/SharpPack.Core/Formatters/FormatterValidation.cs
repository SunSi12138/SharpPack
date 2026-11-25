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
        if (third == 0)
        {
            return ValidateProduct(payloadLength, 0);
        }
        var pair = FitElementCount((long)first * second);
        return ValidateProduct(payloadLength, (long)pair * third);
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
        // A later zero makes the array empty, even if earlier nonzero
        // dimensions would by themselves exceed the element-count range.
        if (third == 0 || fourth == 0)
        {
            return ValidateProduct(payloadLength, 0);
        }
        var pair = FitElementCount((long)first * second);
        var triple = FitElementCount((long)pair * third);
        return ValidateProduct(payloadLength, (long)triple * fourth);
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

    static int FitElementCount(long product)
    {
        if (product > int.MaxValue)
        {
            SharpPackSerializationException.ThrowSizeOverflow();
        }
        return (int)product;
    }

    static int ValidateProduct(int payloadLength, long product)
    {
        var count = FitElementCount(product);
        if (payloadLength != count)
        {
            throw new SharpPackSerializationException(SharpPackErrorCode.InvalidWireData,
                $"The multidimensional array declares {payloadLength} elements " +
                $"but its dimensions require {count}.");
        }
        return count;
    }
}
