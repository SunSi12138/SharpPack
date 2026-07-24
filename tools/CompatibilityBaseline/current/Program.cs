using System.Text.Json;
using MemoryPack;

if (args is not [var originalCorpusPath])
{
    throw new ArgumentException(
        "Usage: CurrentPayloads <original-head-corpus.json>");
}

var original = JsonSerializer.Deserialize<GoldenDocument>(
    File.ReadAllText(originalCorpusPath))
    ?? throw new InvalidOperationException(
        "Could not read the original-head payload corpus.");
var currentEntries = new List<CurrentPayloadEntry>();

foreach (var entry in original.Entries)
{
    var type = Type.GetType(entry.Type, throwOnError: false);
    if (type is null)
    {
        // Fixture-local generated types are covered by exact payload tests.
        continue;
    }

    var invoker = PayloadInvoker.Create(type);
    var context = entry.Configuration == "Utf16"
        ? new MemoryPackSerializerContext(
            MemoryPackSerializerConfiguration.Utf16)
        : null;
    var originalPayload = Convert.FromHexString(entry.PayloadHex);
    var value = invoker.Deserialize(originalPayload, context);
    var currentPayload = invoker.Serialize(value, context);

    currentEntries.Add(
        new CurrentPayloadEntry(
            entry.Type,
            entry.Configuration,
            Convert.ToHexString(currentPayload),
            entry.Decoded));
}

var current = new CurrentPayloadDocument(
    original.SourceCommit,
    currentEntries);
Console.Write(
    JsonSerializer.Serialize(
        current,
        new JsonSerializerOptions { WriteIndented = true }));

abstract class PayloadInvoker
{
    internal abstract object? Deserialize(
        byte[] payload,
        MemoryPackSerializerContext? context);

    internal abstract byte[] Serialize(
        object? value,
        MemoryPackSerializerContext? context);

    internal static PayloadInvoker Create(Type type)
        => (PayloadInvoker)Activator.CreateInstance(
            typeof(PayloadInvoker<>).MakeGenericType(type))!;
}

sealed class PayloadInvoker<T> : PayloadInvoker
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

sealed record GoldenDocument(
    string SourceCommit,
    int WellKnownFormatterCount,
    int GenericShapeCount,
    IReadOnlyList<GoldenEntry> Entries);

sealed record GoldenEntry(
    string Category,
    string Type,
    string Configuration,
    string PayloadHex,
    bool Deterministic,
    string Decoded);

sealed record CurrentPayloadDocument(
    string SourceCommit,
    IReadOnlyList<CurrentPayloadEntry> Entries);

sealed record CurrentPayloadEntry(
    string Type,
    string Configuration,
    string PayloadHex,
    string Decoded);
