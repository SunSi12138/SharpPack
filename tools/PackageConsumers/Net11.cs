using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpPack;

internal static class Net11
{
    public static void Verify()
    {
        if (Environment.Version.Major != 11)
            throw new InvalidOperationException("Expected a .NET 11 consumer runtime.");

        var context = new SharpPackSerializerContext();
        Check(context, BFloat16.One, BFloat16.NegativeZero, BFloat16.NaN, BFloat16.PositiveInfinity, "0080");
        Check(context, Decimal32.Parse("1.2500", CultureInfo.InvariantCulture), Decimal32.NegativeZero,
            Decimal32.NaN, Decimal32.PositiveInfinity, "000080B2");
        Check(context, Decimal64.Parse("1.250000", CultureInfo.InvariantCulture), Decimal64.NegativeZero,
            Decimal64.NaN, Decimal64.PositiveInfinity, "000000000000C0B1");
        Check(context, Decimal128.Parse("1.25000000000000000000000000000000", CultureInfo.InvariantCulture),
            Decimal128.NegativeZero, Decimal128.NaN, Decimal128.PositiveInfinity, "000000000000000000000000000040B0");
    }

    static void Check<T>(SharpPackSerializerContext context, T finite, T negativeZero, T nan, T infinity,
        string expectedZeroBytes) where T : unmanaged
    {
        var scalar = SharpPackSerializer.Serialize(negativeZero);
        if (Convert.ToHexString(scalar) != expectedZeroBytes)
            throw new InvalidOperationException($"Unexpected {typeof(T).Name} signed-zero wire representation.");
        EqualBits(negativeZero, SharpPackSerializer.Deserialize<T>(scalar));

        T? nullable = nan;
        var nullableCopy = SharpPackSerializer.Deserialize<T?>(SharpPackSerializer.Serialize(nullable, context), context);
        EqualBits(nan, nullableCopy ?? throw new InvalidOperationException("Nullable number lost its value."));

        var values = new[] { finite, negativeZero, nan, infinity };
        var arrayCopy = SharpPackSerializer.Deserialize<T[]>(SharpPackSerializer.Serialize(values, context), context);
        if (arrayCopy?.Length != values.Length)
            throw new InvalidOperationException("Numeric array changed length.");
        for (var i = 0; i < values.Length; i++)
            EqualBits(values[i], arrayCopy[i]);

        var model = new Net11NumericModel<T> { Value = finite, Keys = new() { [finite] = negativeZero } };
        var modelCopy = SharpPackSerializer.Deserialize<Net11NumericModel<T>>(
            SharpPackSerializer.Serialize(model, context), context)!;
        EqualBits(finite, modelCopy.Value);
        EqualBits(negativeZero, modelCopy.Keys[finite]);
    }

    static void EqualBits<T>(T expected, T actual) where T : unmanaged
    {
        if (!MemoryMarshal.AsBytes(new[] { expected }.AsSpan()).SequenceEqual(
            MemoryMarshal.AsBytes(new[] { actual }.AsSpan())))
            throw new InvalidOperationException($"{typeof(T).Name} changed its floating-point bits.");
    }
}

[SharpPackable]
internal partial class Net11NumericModel<T> where T : unmanaged
{
    public T Value { get; set; }
    public Dictionary<T, T> Keys { get; set; } = new();
}
