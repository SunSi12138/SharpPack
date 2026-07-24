using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MemoryPack.Formatters;

namespace MemoryPack.Tests;

public class OriginalHeadCompatibilityTest
{
    static readonly GoldenDocument Corpus = LoadCorpus();

    public static IEnumerable<object[]> BuiltInCases()
        => Corpus.Entries
            .Where(static entry => entry.Category is
                "well-known" or
                "generic-shape" or
                "array-rank" or
                "configuration")
            .Select(static entry => new object[] { entry });

    [Fact]
    public void Corpus_WasGeneratedFromThePinnedOriginalHead()
    {
        Corpus.SourceCommit.Should().Be(
            "85ab9ad76c380aca48c09ff3a0ad955ee5a2902b");
        Corpus.WellKnownFormatterCount.Should().Be(117);
        Corpus.GenericShapeCount.Should().Be(68);
        Corpus.Entries.Should().HaveCount(203);
    }

    [Theory]
    [MemberData(nameof(BuiltInCases))]
    public void BuiltInPayloads_AreReadableFromOriginalHead(
        GoldenEntry entry)
    {
        var type = Type.GetType(entry.Type, throwOnError: true)!;
        var invoker = GoldenInvoker.Create(type);
        var payload = Convert.FromHexString(entry.PayloadHex);
        var context = entry.Configuration == nameof(MemoryPackStringEncoding.Utf16)
            ? new MemoryPackSerializerContext(MemoryPackSerializerConfiguration.Utf16)
            : new MemoryPackSerializerContext();

        var defaultValue = entry.Configuration == nameof(MemoryPackStringEncoding.Utf8)
            ? invoker.Deserialize(payload, context: null)
            : invoker.Deserialize(payload, context);
        var contextValue = invoker.Deserialize(payload, context);
        var contextPayload = invoker.Serialize(contextValue, context);

        _ = invoker.Deserialize(contextPayload, context);

        if (entry.Configuration == nameof(MemoryPackStringEncoding.Utf8))
        {
            var defaultPayload = invoker.Serialize(defaultValue, context: null);
            _ = invoker.Deserialize(defaultPayload, context: null);

            if (IsByteDeterministic(entry))
            {
                defaultPayload.Should().Equal(payload);
                contextPayload.Should().Equal(payload);
            }
        }
        else if (IsByteDeterministic(entry))
        {
            contextPayload.Should().Equal(payload);
        }
    }

    [Fact]
    public void GeneratedObjectPayload_IsByteCompatible()
    {
        var value = new GoldenObject
        {
            Id = 42,
            Name = "golden",
            Values = [1, 3, 5],
        };
        AssertBothPaths(
            value,
            "032A000000F9FFFFFF06000000676F6C64656E03000000010000000300000005000000");
    }

    [Fact]
    public void VersionTolerantPayload_IsByteCompatible()
    {
        var value = new GoldenVersionTolerant { Id = 17, Name = "vt" };
        AssertBothPaths(
            value,
            "02040A11000000FDFFFFFF020000007674");
    }

    [Fact]
    public void CircularReferencePayload_PreservesIdentity()
    {
        var value = new GoldenCircular { Name = "self" };
        value.Next = value;
        var context = new MemoryPackSerializerContext();
        var originalPayload = Convert.FromHexString(
            "020C0200FBFFFFFF0400000073656C66FA00");

        MemoryPackSerializer.Serialize(value).Should().Equal(originalPayload);
        MemoryPackSerializer.Serialize(value, context).Should().Equal(originalPayload);

        var fromDefault = MemoryPackSerializer.Deserialize<GoldenCircular>(originalPayload)!;
        var fromContext = MemoryPackSerializer.Deserialize<GoldenCircular>(
            originalPayload,
            context)!;
        ReferenceEquals(fromDefault, fromDefault.Next).Should().BeTrue();
        ReferenceEquals(fromContext, fromContext.Next).Should().BeTrue();
    }

    [Fact]
    public void StaticUnionPayload_IsByteCompatible()
    {
        IGoldenUnion value = new GoldenUnionA { Value = 1234 };
        AssertBothPaths(value, "0301D2040000");
    }

    [Fact]
    public void DynamicUnionPayloads_AreByteCompatible()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(
                new DynamicUnionFormatterBuilder<GoldenDynamicBase>()
                    .Add<GoldenDynamicA>(7)
                    .Add<GoldenDynamicB>(42)
                    .Build())
            .Build();

        GoldenDynamicBase first = new GoldenDynamicA { Value = 5678 };
        GoldenDynamicBase second = new GoldenDynamicB { Value = "dynamic" };

        MemoryPackSerializer.Serialize(first, context).Should()
            .Equal(Convert.FromHexString("07012E160000"));
        MemoryPackSerializer.Serialize(second, context).Should()
            .Equal(Convert.FromHexString(
                "2A01F8FFFFFF0700000064796E616D6963"));
    }

    [Fact]
    public void ExternalUnionPayload_IsByteCompatible()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .RegisterGoldenExternalUnionFormatter()
            .Build();
        IGoldenExternalUnion value =
            new GoldenExternalUnionA { Value = 2468 };
        var originalPayload = Convert.FromHexString("0501A4090000");

        MemoryPackSerializer.Serialize(value, context)
            .Should().Equal(originalPayload);
        MemoryPackSerializer.Deserialize<IGoldenExternalUnion>(
            originalPayload,
            context).Should().BeOfType<GoldenExternalUnionA>()
            .Which.Value.Should().Be(2468);
    }

    [Fact]
    public void CustomFormatterAndClosedGenericPayloads_AreCompatible()
    {
        var context = new MemoryPackSerializerContextBuilder()
            .Register(new GoldenCustomFormatter())
            .Register(new GoldenGenericFormatter<int>())
            .RegisterCollection<GoldenList, int>()
            .Build();

        var customPayload = Convert.FromHexString("01B77A0000");
        var genericPayload = Convert.FromHexString("019E0A0000");
        var collectionPayload = Convert.FromHexString(
            "0400000002000000040000000600000008000000");

        MemoryPackSerializer.Deserialize<GoldenCustom>(
            customPayload,
            context)!.Value.Should().Be(31415);
        MemoryPackSerializer.Deserialize<GoldenGeneric<int>>(
            genericPayload,
            context)!.Value.Should().Be(2718);
        MemoryPackSerializer.Deserialize<GoldenList>(
            collectionPayload,
            context).Should().Equal(2, 4, 6, 8);

        MemoryPackSerializer.Serialize(
            new GoldenCustom { Value = 31415 },
            context).Should().Equal(customPayload);
        MemoryPackSerializer.Serialize(
            new GoldenGeneric<int> { Value = 2718 },
            context).Should().Equal(genericPayload);
        MemoryPackSerializer.Serialize(
            new GoldenList { 2, 4, 6, 8 },
            context).Should().Equal(collectionPayload);
    }

    [Fact]
    public void CompressionPayload_IsByteCompatible()
    {
        var value = new GoldenCompression
        {
            Bits = [true, false, true, true, false, false, true],
            Bytes = System.Text.Encoding.UTF8.GetBytes(
                "brotli/brotli/brotli/兼容"),
            Text = "brotli/string/brotli/string/兼容",
            Object = new GoldenObject
            {
                Id = 99,
                Name = "compressed",
                Values = [9, 8, 7],
            },
        };
        var entry = Corpus.Entries.Single(static entry =>
            entry.Category == "compression");
        var originalPayload = Convert.FromHexString(entry.PayloadHex);
        var context = new MemoryPackSerializerContext();

        MemoryPackSerializer.Serialize(value).Should().Equal(originalPayload);
        MemoryPackSerializer.Serialize(value, context)
            .Should().Equal(originalPayload);
        MemoryPackSerializer.Deserialize<GoldenCompression>(
            originalPayload,
            context).Should().BeEquivalentTo(value);
    }

    [Fact]
    public void BigInteger_LegacyPayloadIsReadableAndCanonicalWriterIsStable()
    {
        var entry = Corpus.Entries.Single(static entry =>
            entry.Type.StartsWith(
                "System.Numerics.BigInteger,",
                StringComparison.Ordinal));
        var legacyPayload = Convert.FromHexString(entry.PayloadHex);
        var context = new MemoryPackSerializerContext();

        MemoryPackSerializer.Deserialize<System.Numerics.BigInteger>(legacyPayload)
            .Should().Be(System.Numerics.BigInteger.Zero);
        MemoryPackSerializer.Deserialize<System.Numerics.BigInteger>(
            legacyPayload,
            context).Should().Be(System.Numerics.BigInteger.Zero);

        var value = System.Numerics.BigInteger.Parse(
            "123456789012345678901234567890",
            System.Globalization.CultureInfo.InvariantCulture);
        var expected = Convert.FromHexString(
            "0D000000D20A3F4EEEE073C3F60FE98E01");

        MemoryPackSerializer.Serialize(value).Should().Equal(expected);
        MemoryPackSerializer.Serialize(value, context).Should().Equal(expected);
    }

    [Fact]
    public void TypePayload_ResolvesApplicationAssemblyInFreshContext()
    {
        var value = new GoldenTypeContainer { Value = typeof(GoldenObject) };
        var payload = MemoryPackSerializer.Serialize(value);
        var context = new MemoryPackSerializerContext();

        var decoded = MemoryPackSerializer.Deserialize<GoldenTypeContainer>(
            payload,
            context);

        decoded!.Value.Should().Be(typeof(GoldenObject));
    }

    static void AssertBothPaths<T>(T value, string expectedHex)
    {
        var expected = Convert.FromHexString(expectedHex);
        var context = new MemoryPackSerializerContext();

        MemoryPackSerializer.Serialize(value).Should().Equal(expected);
        MemoryPackSerializer.Serialize(value, context).Should().Equal(expected);
        _ = MemoryPackSerializer.Deserialize<T>(expected);
        _ = MemoryPackSerializer.Deserialize<T>(expected, context);
    }

    static bool IsByteDeterministic(GoldenEntry entry)
        => entry.Deterministic
           && !entry.Type.StartsWith(
               "System.Nullable`1",
               StringComparison.Ordinal)
           && !entry.Type.StartsWith(
               "System.Collections.BitArray",
               StringComparison.Ordinal);

    static GoldenDocument LoadCorpus()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Compatibility",
            "original-head-golden.json");
        return JsonSerializer.Deserialize<GoldenDocument>(
            File.ReadAllText(path))!;
    }

    public sealed record GoldenDocument(
        string SourceCommit,
        int WellKnownFormatterCount,
        int GenericShapeCount,
        IReadOnlyList<GoldenEntry> Entries);

    public sealed record GoldenEntry(
        string Category,
        string Type,
        string Configuration,
        string PayloadHex,
        bool Deterministic,
        string Decoded)
    {
        public override string ToString() => $"{Category}: {Type}";
    }

    abstract class GoldenInvoker
    {
        internal abstract object? Deserialize(
            byte[] payload,
            MemoryPackSerializerContext? context);

        internal abstract byte[] Serialize(
            object? value,
            MemoryPackSerializerContext? context);

        internal static GoldenInvoker Create(Type type)
            => (GoldenInvoker)Activator.CreateInstance(
                typeof(GoldenInvoker<>).MakeGenericType(type))!;
    }

    sealed class GoldenInvoker<T> : GoldenInvoker
    {
        internal override object? Deserialize(
            byte[] payload,
            MemoryPackSerializerContext? context)
            => context is null
                ? MemoryPackSerializer.Deserialize<T>(payload)
                : MemoryPackSerializer.Deserialize<T>(payload, context);

        internal override byte[] Serialize(
            object? value,
            MemoryPackSerializerContext? context)
        {
            var typedValue = (T?)value;
            return context is null
                ? MemoryPackSerializer.Serialize(typedValue)
                : MemoryPackSerializer.Serialize(typedValue, context);
        }
    }
}

[MemoryPackable]
public partial class GoldenObject
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int[]? Values { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class GoldenVersionTolerant
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }

    [MemoryPackOrder(1)]
    public string? Name { get; set; }
}

[MemoryPackable(GenerateType.CircularReference)]
public partial class GoldenCircular
{
    [MemoryPackOrder(0)]
    public string? Name { get; set; }

    [MemoryPackOrder(1)]
    public GoldenCircular? Next { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(3, typeof(GoldenUnionA))]
[MemoryPackUnion(9, typeof(GoldenUnionB))]
public partial interface IGoldenUnion;

[MemoryPackable]
public partial class GoldenUnionA : IGoldenUnion
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class GoldenUnionB : IGoldenUnion
{
    public string? Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial class GoldenDynamicBase;

[MemoryPackable]
public partial class GoldenDynamicA : GoldenDynamicBase
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class GoldenDynamicB : GoldenDynamicBase
{
    public string? Value { get; set; }
}

[MemoryPackable]
public partial class GoldenTypeContainer
{
    public Type? Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IGoldenExternalUnion;

[MemoryPackable]
public partial class GoldenExternalUnionA : IGoldenExternalUnion
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class GoldenExternalUnionB : IGoldenExternalUnion
{
    public string? Value { get; set; }
}

[MemoryPackUnionFormatter(typeof(IGoldenExternalUnion))]
[MemoryPackUnion(5, typeof(GoldenExternalUnionA))]
[MemoryPackUnion(6, typeof(GoldenExternalUnionB))]
public partial class GoldenExternalUnionFormatter;

public sealed class GoldenCustom
{
    public int Value { get; set; }
}

public sealed class GoldenCustomFormatter : MemoryPackFormatter<GoldenCustom>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref GoldenCustom? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(value.Value);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref GoldenCustom? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out int decoded);
        value = new GoldenCustom { Value = decoded };
    }
}

public sealed class GoldenGeneric<T>
{
    public T? Value { get; set; }
}

public sealed class GoldenGenericFormatter<T>
    : MemoryPackFormatter<GoldenGeneric<T>>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref GoldenGeneric<T>? value)
    {
        if (value is null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteValue(value.Value);
    }

    public override void Deserialize(
        ref MemoryPackReader reader,
        scoped ref GoldenGeneric<T>? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        value = new GoldenGeneric<T> { Value = reader.ReadValue<T>() };
    }
}

public sealed class GoldenList : List<int>;

[MemoryPackable]
public partial class GoldenCompression
{
    [BitPackFormatter]
    public bool[]? Bits { get; set; }

    [BrotliFormatter]
    public byte[]? Bytes { get; set; }

    [BrotliStringFormatter]
    public string? Text { get; set; }

    [BrotliFormatter<GoldenObject>]
    public GoldenObject? Object { get; set; }
}
