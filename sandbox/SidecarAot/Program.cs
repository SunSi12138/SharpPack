using SharpPack;
using SharpPack.Generated;
using SidecarFixture;

[assembly: SharpPackExternalType(typeof(List<ExternalNote>))]
[assembly: SharpPackExternalType(typeof(ExternalEnvelope<ExternalNote>))]
[assembly: SharpPackExternalType(typeof(ContextOnlyRoot))]
[assembly: SharpPackExternalType(typeof(IExternalUnion<int>))]

var context = SharpPackExternalFormatters.Register(new SharpPackSerializerContextBuilder()).Build();

if (SharpPackSerializer.Deserialize<ContextOnlyRoot>(new byte[] { 1, 0, 0, 0, 0 }, context)?.Record.Caption != "")
    throw new InvalidOperationException("The typed context-only factory could not resolve its external dependency.");
if (SharpPackSerializer.Deserialize<IExternalUnion<int>>(new byte[] { 3, 1, 42, 0, 0, 0 }, context)
    is not ExternalUnionCase { Value: 42 })
    throw new InvalidOperationException("The generated provider did not reuse the closed union factory.");

// Read the ordinary object format before any serialization. Under AOT the
// generated provider must already contain the closed list and metadata root.
byte[] wire = [1, 0, 0, 0, 2, 42, 0, 0, 0, 0, 0, 0, 0];
var notes = SharpPackSerializer.Deserialize<List<ExternalNote>>(wire, context);
if (notes is not [{ Count: 42, Text: "" }])
    throw new InvalidOperationException("The deserialize-first external list did not match.");
if (!SharpPackSerializer.Serialize(notes, context).AsSpan().SequenceEqual(wire))
    throw new InvalidOperationException("The external list wire bytes changed.");

var message = new ExternalEnvelope<ExternalNote>
{
    Messages = new() { ["plain"] = [notes[0]] },
    Value = new() { Label = "managed struct" },
    Title = new("record"),
};
var frame = SharpPackSerializer.Serialize(message, context);
var roundTrip = SharpPackSerializer.Deserialize<ExternalEnvelope<ExternalNote>>(frame, context);
if (roundTrip is not { Messages: { } messages, Value.Label: "managed struct", Title.Caption: "record" } ||
    messages["plain"] is not [{ Count: 42, Text: "" }])
    throw new InvalidOperationException("The nested closed metadata graph did not round-trip.");

Console.WriteLine("External sidecar deserialize-first and nested wire checks passed.");

[SharpPackable(GenerateType.NoGenerate)] public partial interface IExternalUnion<T>;
[SharpPackable] public partial class ExternalUnionCase : IExternalUnion<int> { public int Value { get; set; } }
[SharpPackUnionFormatter(typeof(IExternalUnion<int>))]
[SharpPackUnion(3, typeof(ExternalUnionCase))]
public partial class ExternalUnionFormatter;

internal sealed class ContextOnlyRoot(ExternalRecord record) : ISharpPackContextFormatterFactory<ContextOnlyRoot>
{
    internal ExternalRecord Record { get; } = record;

    static SharpPackFormatter<ContextOnlyRoot> ISharpPackContextFormatterFactory<ContextOnlyRoot>.CreateFormatter(
        SharpPackSerializerContext context)
        => new Formatter(context.GetFormatter<ExternalRecord>());

    sealed class Formatter(SharpPackFormatter<ExternalRecord> inner) : SharpPackFormatter<ContextOnlyRoot>
    {
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ContextOnlyRoot? value)
            => writer.WriteValueWithFormatter(inner, value!.Record);

        public override void Deserialize(ref SharpPackReader reader, scoped ref ContextOnlyRoot? value)
        {
            ExternalRecord? record = null;
            reader.ReadValueWithFormatter(inner, ref record);
            value = new ContextOnlyRoot(record!);
        }
    }
}
