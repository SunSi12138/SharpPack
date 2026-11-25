using SharpPack;
using SharpPack.Generated;
using SidecarFixture;

[assembly: SharpPackExternalType(typeof(List<ExternalNote>))]
[assembly: SharpPackExternalType(typeof(ExternalEnvelope<ExternalNote>))]

var context = SharpPackExternalFormatters.Register(new SharpPackSerializerContextBuilder()).Build();

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
