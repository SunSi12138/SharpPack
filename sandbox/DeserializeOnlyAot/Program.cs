using SharpPack;
using SharpPack.Formatters;

// No Serialize calls, model construction, factory registrations, or factory
// references: these consumers must survive on their deserialization roots alone.
var failures = new List<string>();
byte[] model = [1, 42, 0, 0, 0];
byte[] nested = [1, 1, 42, 0, 0, 0];
byte[] list = [1, 0, 0, 0, 1, 42, 0, 0, 0];
var context = new SharpPackSerializerContextBuilder()
    .Register(new PlusOneFormatter()).Build();
Check("ordinary", () => SharpPackSerializer.Deserialize<OrdinaryModel>(model)?.Value == 42);
Check("context override", () => SharpPackSerializer.Deserialize<ContextModel>(model, context)?.Value == 43);
Check("nested", () => SharpPackSerializer.Deserialize<NestedModel>(nested)?.Item?.Value == 42);
Check("nested context", () => SharpPackSerializer.Deserialize<ContextNestedModel>(nested, context)?.Item?.Value == 43);
// Closed collection formatter rooting is the existing documented NativeAOT
// requirement. It must not incidentally root the element's generated factory.
GC.KeepAlive(new ListFormatter<CollectionItem>());
Check("collection root", () => SharpPackSerializer.Deserialize<List<CollectionItem>>(list) is [{ Value: 42 }]);
if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
Console.WriteLine("Deserialize-only AOT: all five cases passed.");
void Check(string name, Func<bool> check)
{
    try
    {
        if (!check()) throw new InvalidOperationException("Unexpected value");
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception error)
    {
        Console.WriteLine($"FAIL: {name}: {error.GetType().Name}: {error.Message}");
        failures.Add(name);
    }
}
[SharpPackable] public partial class OrdinaryModel { public int Value { get; set; } }
[SharpPackable] public partial class ContextModel { public int Value { get; set; } }
[SharpPackable] public partial class NestedModel { public NestedItem? Item { get; set; } }
[SharpPackable] public partial class NestedItem { public int Value { get; set; } }
[SharpPackable] public partial class ContextNestedModel { public ContextNestedItem? Item { get; set; } }
[SharpPackable] public partial class ContextNestedItem { public int Value { get; set; } }
[SharpPackable] public partial class CollectionItem { public int Value { get; set; } }
public sealed class PlusOneFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value)
        => throw new NotSupportedException("Deserialize-only fixture");
    public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
    {
        reader.ReadUnmanaged(out int raw);
        value = raw + 1;
    }
}
