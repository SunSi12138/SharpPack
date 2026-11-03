using System.Text.Json;
using SharpPack;

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
var fixtureEntryCount = 0;
var rejectedLegacyTypeCount = 0;

foreach (var entry in original.Entries)
{
    var type = Type.GetType(entry.Type, throwOnError: false);
    if (type is null)
    {
        // Only the pre-existing GoldenCorpus-local fixtures may be skipped here.
        // Their exact payload tests live in OriginalHeadCompatibilityTest.
        if (!entry.Type.Contains(", GoldenCorpus,", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected unresolved non-fixture type: {entry.Type}");
        }
        fixtureEntryCount++;
        continue;
    }

    var invoker = PayloadInvoker.Create(type);
    var context = entry.Configuration == "Utf16"
        ? new SharpPackSerializerContext(
            SharpPackSerializerConfiguration.Utf16)
        : null;
    var originalPayload = Convert.FromHexString(entry.PayloadHex);
    if (type == typeof(Type))
    {
        // The original corpus stays immutable. This one wire contract deliberately changed.
        try
        {
            _ = invoker.Deserialize(originalPayload, context);
        }
        catch (SharpPackSerializationException)
        {
            rejectedLegacyTypeCount++;
            Console.Error.WriteLine("Verified intentional legacy System.Type payload rejection; excluded from reverse wire compatibility.");
            continue;
        }
        throw new InvalidOperationException("Legacy System.Type payload unexpectedly resolved.");
    }
    var value = invoker.Deserialize(originalPayload, context);
    var currentPayload = invoker.Serialize(value, context);

    currentEntries.Add(
        new CurrentPayloadEntry(
            entry.Type,
            entry.Configuration,
            Convert.ToHexString(currentPayload),
            entry.Decoded));
}

// Pinned corpus ledger: 203 total = 11 original fixture-local entries +
// 191 non-Type reverse-compatible runtime entries + 1 rejected legacy Type.
// No additional Type exemptions or unresolved runtime types may disappear silently.
if (original.Entries.Count != 203 || fixtureEntryCount != 11 ||
    rejectedLegacyTypeCount != 1 || currentEntries.Count != 191)
{
    throw new InvalidOperationException(
        $"Unexpected compatibility coverage: total={original.Entries.Count}, " +
        $"fixture={fixtureEntryCount}, legacy-Type={rejectedLegacyTypeCount}, reverse={currentEntries.Count}.");
}
Console.Error.WriteLine(
    $"Compatibility coverage: {currentEntries.Count} reverse-compatible, " +
    $"{rejectedLegacyTypeCount} legacy Type rejected, {fixtureEntryCount} fixture-local entries tested separately.");

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
        SharpPackSerializerContext? context);

    internal abstract byte[] Serialize(
        object? value,
        SharpPackSerializerContext? context);

    internal static PayloadInvoker Create(Type type)
        => (PayloadInvoker)Activator.CreateInstance(
            typeof(PayloadInvoker<>).MakeGenericType(type))!;
}

sealed class PayloadInvoker<T> : PayloadInvoker
{
    internal override object? Deserialize(
        byte[] payload,
        SharpPackSerializerContext? context)
        => context is null
            ? SharpPackSerializer.Deserialize<T>(payload)
            : SharpPackSerializer.Deserialize<T>(payload, context);

    internal override byte[] Serialize(
        object? value,
        SharpPackSerializerContext? context)
    {
        var typedValue = (T?)value;
        return context is null
            ? SharpPackSerializer.Serialize(typedValue)
            : SharpPackSerializer.Serialize(typedValue, context);
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
