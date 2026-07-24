using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MemoryPack;
using MemoryPack.Compression;
using MemoryPack.Formatters;

const string SourceCommit = "85ab9ad76c380aca48c09ff3a0ad955ee5a2902b";

if (args is ["--verify-current", var currentPayloadPath])
{
    var currentDocument = JsonSerializer.Deserialize<CurrentPayloadDocument>(
        File.ReadAllText(currentPayloadPath))
        ?? throw new InvalidOperationException(
            "Could not read the current-writer payload corpus.");

    foreach (var entry in currentDocument.Entries)
    {
        var type = Type.GetType(entry.Type, throwOnError: true)!;
        var options = entry.Configuration == "Utf16"
            ? MemoryPackSerializerOptions.Utf16
            : MemoryPackSerializerOptions.Utf8;
        var decoded = MemoryPackSerializer.Deserialize(
            type,
            Convert.FromHexString(entry.PayloadHex),
            options);
        var description = Describe(decoded);
        if (!StringComparer.Ordinal.Equals(description, entry.Decoded))
        {
            throw new InvalidOperationException(
                $"Original HEAD decoded the current {entry.Type} payload as " +
                $"'{description}', expected '{entry.Decoded}'.");
        }
    }

    Console.WriteLine(
        $"Original HEAD read {currentDocument.Entries.Count} current-writer payloads.");
    return;
}

var entries = new List<GoldenEntry>();

_ = MemoryPackFormatterProvider.IsRegistered<int>();
var providerType = typeof(MemoryPackFormatterProvider);
var providerFormatters = (IEnumerable)providerType
    .GetField("formatters", BindingFlags.Static | BindingFlags.NonPublic)!
    .GetValue(null)!;

var wellKnown = new List<Type>();
foreach (var item in providerFormatters)
{
    var type = (Type)item!.GetType().GetProperty("Key")!.GetValue(item)!;
    if (type.Assembly != typeof(BaselineObject).Assembly)
    {
        wellKnown.Add(type);
    }
}

foreach (var type in wellKnown.OrderBy(static x => x.FullName, StringComparer.Ordinal))
{
    Add("well-known", type, CreateWellKnownValue(type), deterministic: true);
}

var genericDefinitions = typeof(MemoryPackFormatterProvider).Assembly
    .GetTypes()
    .SelectMany(static type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
    .Where(static field => typeof(IDictionary<Type, Type>).IsAssignableFrom(field.FieldType))
    .SelectMany(static field => ((IDictionary<Type, Type>?)field.GetValue(null))?.Keys ?? [])
    .Where(static type => type.IsGenericTypeDefinition)
    .Distinct()
    .OrderBy(static type => type.FullName, StringComparer.Ordinal)
    .ToArray();

foreach (var definition in genericDefinitions)
{
    var closed = CloseGenericDefinition(definition);
    Add("generic-shape", closed, CreateShapeValue(closed), deterministic: IsDeterministic(closed));
}

Add("array-rank", typeof(int[,]), new[,] { { 1, 2 }, { 3, 4 } }, true);
Add("array-rank", typeof(int[,,]), new int[1, 1, 2] { { { 5, 6 } } }, true);
Add("array-rank", typeof(int[,,,]), new int[1, 1, 1, 2] { { { { 7, 8 } } } }, true);
Add("configuration", typeof(string), "MemoryPack/兼容/UTF16", true, MemoryPackSerializerOptions.Utf16);

Add("object", typeof(BaselineObject), new BaselineObject { Id = 42, Name = "golden", Values = [1, 3, 5] }, true);
Add("version-tolerant", typeof(BaselineVersionTolerant), new BaselineVersionTolerant { Id = 17, Name = "vt" }, true);

var self = new BaselineCircular { Name = "self" };
self.Next = self;
Add("circular-reference", typeof(BaselineCircular), self, true, validateIdentity: true);

Add("static-union", typeof(IBaselineUnion), new BaselineUnionA { Value = 1234 }, true);

var dynamicFormatter = new DynamicUnionFormatter<BaselineDynamicBase>(
    (7, typeof(BaselineDynamicA)),
    (42, typeof(BaselineDynamicB)));
MemoryPackFormatterProvider.Register(dynamicFormatter);
Add("dynamic-union", typeof(BaselineDynamicBase), new BaselineDynamicA { Value = 5678 }, true);
Add("dynamic-union", typeof(BaselineDynamicBase), new BaselineDynamicB { Value = "dynamic" }, true);

Add("external-union", typeof(IBaselineExternalUnion), new BaselineExternalUnionA { Value = 2468 }, true);

MemoryPackFormatterProvider.Register(new BaselineCustomFormatter());
Add("custom-formatter", typeof(BaselineCustom), new BaselineCustom { Value = 31415 }, true);

MemoryPackFormatterProvider.Register(new BaselineGenericFormatter<int>());
Add("custom-generic", typeof(BaselineGeneric<int>), new BaselineGeneric<int> { Value = 2718 }, true);

MemoryPackFormatterProvider.RegisterCollection<BaselineList, int>();
Add("custom-collection", typeof(BaselineList), new BaselineList { 2, 4, 6, 8 }, true);

Add("compression", typeof(BaselineCompression), new BaselineCompression
{
    Bits = [true, false, true, true, false, false, true],
    Bytes = Encoding.UTF8.GetBytes("brotli/brotli/brotli/兼容"),
    Text = "brotli/string/brotli/string/兼容",
    Object = new BaselineObject
    {
        Id = 99,
        Name = "compressed",
        Values = [9, 8, 7],
    },
}, true);

Add("collection-value", typeof(ConcurrentDictionary<int, string>), new ConcurrentDictionary<int, string>(new[]
{
    new KeyValuePair<int, string>(1, "one"),
    new KeyValuePair<int, string>(2, "two"),
}), false);
Add("collection-value", typeof(ImmutableList<int>), ImmutableList.Create(1, 2, 3), true);
Add("collection-value", typeof(FrozenDictionary<int, string>), new Dictionary<int, string>
{
    [1] = "one",
    [2] = "two",
}.ToFrozenDictionary(), false);

var canonicalBigInteger = Convert.FromHexString(
    "0D000000D20A3F4EEEE073C3F60FE98E01");
var canonicalBigIntegerValue = (BigInteger)MemoryPackSerializer.Deserialize(
    typeof(BigInteger),
    canonicalBigInteger)!;
if (canonicalBigIntegerValue != BigInteger.Parse(
        "123456789012345678901234567890",
        CultureInfo.InvariantCulture))
{
    throw new InvalidOperationException(
        "Original HEAD could not read the canonical BigInteger payload.");
}

var document = new GoldenDocument(
    SourceCommit,
    wellKnown.Count,
    genericDefinitions.Length,
    entries);
Console.Write(JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));

void Add(
    string category,
    Type type,
    object? value,
    bool deterministic,
    MemoryPackSerializerOptions? options = null,
    bool validateIdentity = false)
{
    if (Nullable.GetUnderlyingType(type) is not null ||
        type == typeof(BitArray) ||
        type == typeof(BitArray[]) ||
        type == typeof(BigInteger) ||
        type == typeof(BigInteger[]))
    {
        deterministic = false;
    }

    var payload = MemoryPackSerializer.Serialize(type, value, options);
    var decoded = MemoryPackSerializer.Deserialize(type, payload, options);
    if (validateIdentity && decoded is BaselineCircular circular && !ReferenceEquals(circular, circular.Next))
    {
        throw new InvalidOperationException("Original circular-reference payload did not preserve identity.");
    }

    entries.Add(new GoldenEntry(
        category,
        FriendlyTypeName(type),
        options?.StringEncoding.ToString() ?? MemoryPackSerializerOptions.Default.StringEncoding.ToString(),
        Convert.ToHexString(payload),
        deterministic,
        Describe(decoded)));
}

static object? CreateWellKnownValue(Type type)
{
    if (type.IsArray)
    {
        var element = type.GetElementType()!;
        var array = Array.CreateInstance(element, 1);
        array.SetValue(CreateWellKnownValue(element), 0);
        return array;
    }

    var nullable = Nullable.GetUnderlyingType(type);
    if (nullable is not null)
    {
        return CreateWellKnownValue(nullable);
    }

    if (type == typeof(string)) return "MemoryPack/兼容";
    if (type == typeof(Version)) return new Version(1, 2, 3, 4);
    if (type == typeof(Uri)) return new Uri("https://example.com/memorypack?q=1");
    if (type == typeof(TimeZoneInfo)) return TimeZoneInfo.Utc;
    if (type == typeof(BigInteger)) return BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture);
    if (type == typeof(BitArray)) return new BitArray([true, false, true, true]);
    if (type == typeof(StringBuilder)) return new StringBuilder("MemoryPack/兼容");
    if (type == typeof(Type)) return typeof(Dictionary<string, int>);
    if (type == typeof(CultureInfo)) return CultureInfo.GetCultureInfo("en-US");
    if (type == typeof(Guid)) return Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    if (type == typeof(DateTime)) return new DateTime(638400000000000000, DateTimeKind.Utc);
    if (type == typeof(DateTimeOffset)) return new DateTimeOffset(638400000000000000, TimeSpan.Zero);
    if (type == typeof(TimeSpan)) return TimeSpan.FromTicks(123456789);
    if (type == typeof(DateOnly)) return new DateOnly(2024, 1, 2);
    if (type == typeof(TimeOnly)) return new TimeOnly(3, 4, 5, 6);
    if (type == typeof(Rune)) return new Rune('界');
    if (type == typeof(char)) return '界';
    if (type == typeof(bool)) return true;
    if (type == typeof(byte)) return (byte)0xA5;
    if (type == typeof(sbyte)) return (sbyte)-42;
    if (type == typeof(short)) return (short)-1234;
    if (type == typeof(ushort)) return (ushort)54321;
    if (type == typeof(int)) return -123456789;
    if (type == typeof(uint)) return 3456789012U;
    if (type == typeof(long)) return -1234567890123456789L;
    if (type == typeof(ulong)) return 12345678901234567890UL;
    if (type == typeof(float)) return 123.25f;
    if (type == typeof(double)) return -9876.5d;
    if (type == typeof(decimal)) return 1234567.8901m;
    if (type == typeof(Half)) return (Half)12.5f;
    if (type == typeof(Int128)) return Int128.Parse("-123456789012345678901234567890", CultureInfo.InvariantCulture);
    if (type == typeof(UInt128)) return UInt128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture);
    if (type == typeof(Complex)) return new Complex(1.25, -2.5);
    if (type == typeof(Vector2)) return new Vector2(1, 2);
    if (type == typeof(Vector3)) return new Vector3(1, 2, 3);
    if (type == typeof(Vector4)) return new Vector4(1, 2, 3, 4);
    if (type == typeof(Quaternion)) return new Quaternion(1, 2, 3, 4);
    if (type == typeof(Plane)) return new Plane(1, 2, 3, 4);
    if (type == typeof(Matrix3x2)) return new Matrix3x2(1, 2, 3, 4, 5, 6);
    if (type == typeof(Matrix4x4)) return Matrix4x4.Identity;
    if (type == typeof(IntPtr)) return new IntPtr(123456);
    if (type == typeof(UIntPtr)) return new UIntPtr(123456);
    return Activator.CreateInstance(type);
}

static Type CloseGenericDefinition(Type definition)
{
    var arguments = definition.GetGenericArguments();
    var types = new Type[arguments.Length];
    for (var i = 0; i < types.Length; i++)
    {
        types[i] = i == 0 && arguments.Length >= 2 ? typeof(string) : typeof(int);
    }

    if (arguments.Length == 8)
    {
        types[7] = definition.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal)
            ? typeof(ValueTuple<int>)
            : typeof(Tuple<int>);
    }

    return definition.MakeGenericType(types);
}

static object? CreateShapeValue(Type type)
{
    if (type.IsValueType)
    {
        return Activator.CreateInstance(type);
    }

    if (type.IsInterface)
    {
        return null;
    }

    try
    {
        return Activator.CreateInstance(type);
    }
    catch
    {
        return null;
    }
}

static bool IsDeterministic(Type type)
    => !type.FullName!.Contains("HashSet", StringComparison.Ordinal)
       && !type.FullName.Contains("Dictionary", StringComparison.Ordinal)
       && !type.FullName.Contains("Lookup", StringComparison.Ordinal)
       && !type.FullName.Contains("Concurrent", StringComparison.Ordinal)
       && !type.FullName.Contains("Frozen", StringComparison.Ordinal);

static string FriendlyTypeName(Type type)
    => type.AssemblyQualifiedName
       ?? throw new InvalidOperationException($"Type has no assembly-qualified name: {type}");

static string Describe(object? value)
{
    if (value is null) return "null";
    if (value is Array array) return $"array:{array.Rank}:{array.Length}";
    if (value is ICollection collection)
    {
        try
        {
            return $"collection:{collection.Count}";
        }
        catch (InvalidOperationException)
        {
            return "collection:default";
        }
    }
    return value.ToString() ?? value.GetType().FullName ?? value.GetType().Name;
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
    string Decoded);

public sealed record CurrentPayloadDocument(
    string SourceCommit,
    IReadOnlyList<CurrentPayloadEntry> Entries);

public sealed record CurrentPayloadEntry(
    string Type,
    string Configuration,
    string PayloadHex,
    string Decoded);

[MemoryPackable]
public partial class BaselineObject
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int[]? Values { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BaselineVersionTolerant
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }

    [MemoryPackOrder(1)]
    public string? Name { get; set; }
}

[MemoryPackable(GenerateType.CircularReference)]
public partial class BaselineCircular
{
    [MemoryPackOrder(0)]
    public string? Name { get; set; }

    [MemoryPackOrder(1)]
    public BaselineCircular? Next { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(3, typeof(BaselineUnionA))]
[MemoryPackUnion(9, typeof(BaselineUnionB))]
public partial interface IBaselineUnion
{
}

[MemoryPackable]
public partial class BaselineUnionA : IBaselineUnion
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class BaselineUnionB : IBaselineUnion
{
    public string? Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial class BaselineDynamicBase
{
}

[MemoryPackable]
public partial class BaselineDynamicA : BaselineDynamicBase
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class BaselineDynamicB : BaselineDynamicBase
{
    public string? Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IBaselineExternalUnion;

[MemoryPackable]
public partial class BaselineExternalUnionA : IBaselineExternalUnion
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class BaselineExternalUnionB : IBaselineExternalUnion
{
    public string? Value { get; set; }
}

[MemoryPackUnionFormatter(typeof(IBaselineExternalUnion))]
[MemoryPackUnion(5, typeof(BaselineExternalUnionA))]
[MemoryPackUnion(6, typeof(BaselineExternalUnionB))]
public partial class BaselineExternalUnionFormatter;

public sealed class BaselineCustom
{
    public int Value { get; set; }
}

public sealed class BaselineCustomFormatter : MemoryPackFormatter<BaselineCustom>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref BaselineCustom? value)
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
        scoped ref BaselineCustom? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        reader.ReadUnmanaged(out int decoded);
        value = new BaselineCustom { Value = decoded };
    }
}

public sealed class BaselineGeneric<T>
{
    public T? Value { get; set; }
}

public sealed class BaselineGenericFormatter<T>
    : MemoryPackFormatter<BaselineGeneric<T>>
{
    public override void Serialize<TBufferWriter>(
        ref MemoryPackWriter<TBufferWriter> writer,
        scoped ref BaselineGeneric<T>? value)
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
        scoped ref BaselineGeneric<T>? value)
    {
        if (!reader.TryReadObjectHeader(out _))
        {
            value = null;
            return;
        }

        value = new BaselineGeneric<T> { Value = reader.ReadValue<T>() };
    }
}

public sealed class BaselineList : List<int>;

[MemoryPackable]
public partial class BaselineCompression
{
    [BitPackFormatter]
    public bool[]? Bits { get; set; }

    [BrotliFormatter]
    public byte[]? Bytes { get; set; }

    [BrotliStringFormatter]
    public string? Text { get; set; }

    [BrotliFormatter<BaselineObject>]
    public BaselineObject? Object { get; set; }
}
