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

        VerifyNativeUnion(context);
    }

    static void VerifyNativeUnion(SharpPackSerializerContext context)
    {
        Net11Reading number = new(42);
        var payload = SharpPackSerializer.Serialize(number);
        if (!payload.AsSpan().SequenceEqual(new byte[] { 7, 42, 0, 0, 0 }) ||
            SharpPackSerializer.Deserialize<Net11ReorderedReading>(payload).Value is not 42)
            throw new InvalidOperationException("Native union tags changed when cases were reordered.");

        var model = new Net11UnionModel { Reading = new("text"), Optional = new((int?)17) };
        var restored = SharpPackSerializer.Deserialize<Net11UnionModel>(
            SharpPackSerializer.Serialize(model, context), context);
        if (restored?.Reading.Value is not "text" || restored.Optional.Value is not 17)
            throw new InvalidOperationException("Generated model lost a reference or nullable-value union case.");

        var empty = SharpPackSerializer.Serialize(default(Net11Reading));
        if (SharpPackSerializer.Deserialize<Net11Reading>(empty).Value is not null)
            throw new InvalidOperationException("The empty union acquired a value.");

        Net11Token token = new(new Net11Token.Word { Text = "nested" });
        var tokenCopy = SharpPackSerializer.Deserialize<Net11Token>(SharpPackSerializer.Serialize(token));
        if (tokenCopy.Value is not Net11Token.Word { Text: "nested" })
            throw new InvalidOperationException("A case declared inside its union did not round trip.");

        var overrideContext = new SharpPackSerializerContextBuilder().Register(new OffsetIntFormatter()).Build();
        var overridden = SharpPackSerializer.Serialize(number, overrideContext);
        if (!overridden.AsSpan().SequenceEqual(new byte[] { 7, 43, 0, 0, 0 }) ||
            SharpPackSerializer.Deserialize<Net11Reading>(overridden, overrideContext).Value is not 42)
            throw new InvalidOperationException("The native union did not honor its case's context formatter.");

        Net11ScalarCase state = new(Net11State.Ready);
        Net11ScalarCase stamp = new(new Net11Stamp(83));
        if (SharpPackSerializer.Deserialize<Net11ScalarCase>(SharpPackSerializer.Serialize(state)).Value is not Net11State.Ready ||
            SharpPackSerializer.Deserialize<Net11ScalarCase>(SharpPackSerializer.Serialize(stamp, context), context).Value is not Net11Stamp { Id: 83 })
            throw new InvalidOperationException("A user-defined enum or unmanaged union case did not round trip.");

        Net11NullableStamp nullableStamp = new((Net11Stamp?)new Net11Stamp(88));
        if (SharpPackSerializer.Deserialize<Net11NullableStamp>(SharpPackSerializer.Serialize(nullableStamp)).Value is not Net11Stamp { Id: 88 })
            throw new InvalidOperationException("A nullable user struct did not round trip through the default root.");

        Net11PrivateCases.Verify();
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
        if (SharpPackSerializer.Deserialize<T?>(SharpPackSerializer.Serialize<T?>(null, context), context).HasValue)
            throw new InvalidOperationException("A null number became present.");

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

    sealed class OffsetIntFormatter : SharpPackFormatter<int>
    {
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value)
            => writer.WriteUnmanaged(value + 1);

        public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
        {
            reader.ReadUnmanaged(out value);
            value--;
        }
    }
}

[SharpPackable]
internal partial class Net11NumericModel<T> where T : unmanaged
{
    public T Value { get; set; }
    public Dictionary<T, T> Keys { get; set; } = new();
}

[SharpPackable, SharpPackUnion(7, typeof(int)), SharpPackUnion(300, typeof(string))]
internal partial union Net11Reading(int, string);

[SharpPackable, SharpPackUnion(300, typeof(string)), SharpPackUnion(7, typeof(int))]
internal partial union Net11ReorderedReading(string, int);

[SharpPackable, SharpPackUnion(12, typeof(int?)), SharpPackUnion(99, typeof(bool))]
internal partial union Net11Optional(int?, bool);

[SharpPackable]
internal partial class Net11UnionModel
{
    public Net11Reading Reading { get; set; }
    public Net11Optional Optional { get; set; }
}

[SharpPackable, SharpPackUnion(2, typeof(Net11Token.Word)), SharpPackUnion(11, typeof(bool))]
internal partial union Net11Token(Net11Token.Word, bool)
{
    [SharpPackable]
    public partial class Word
    {
        public string? Text { get; set; }
    }
}

internal enum Net11State : byte { Ready = 5 }
internal readonly record struct Net11Stamp(int Id);

[SharpPackable, SharpPackUnion(19, typeof(Net11State)), SharpPackUnion(27, typeof(Net11Stamp))]
internal partial union Net11ScalarCase(Net11State, Net11Stamp)
{
    static Net11ScalarCase() { }
}

[SharpPackable, SharpPackUnion(8, typeof(Net11Stamp?)), SharpPackUnion(9, typeof(bool))]
internal partial union Net11NullableStamp(Net11Stamp?, bool);

internal static partial class Net11PrivateCases
{
    [SharpPackable]
    private partial class PrivateCase
    {
        public int Count { get; set; }
    }

    [SharpPackable, SharpPackUnion(7, typeof(PrivateCase)), SharpPackUnion(23, typeof(bool))]
    private partial union PrivateChoice(PrivateCase, bool);

    public static void Verify()
    {
        PrivateChoice input = new(new PrivateCase { Count = 31 });
        if (SharpPackSerializer.Deserialize<PrivateChoice>(SharpPackSerializer.Serialize(input)).Value is not PrivateCase { Count: 31 })
            throw new InvalidOperationException("A private union lost its accessible sibling case.");
    }
}
