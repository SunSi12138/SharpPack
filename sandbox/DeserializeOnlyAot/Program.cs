using DeserializeOnlyAot.Contracts;
using SharpPack;
using SharpPack.Formatters;

if (args is ["--performance"])
{
    ReadOnlyPerformance.Run();
    return;
}

// No Serialize calls or generated-model construction. Ordinary/same-assembly
// consumers rely on deserialization roots; cross-assembly union uses its
// required explicit context registration.
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
Check("closed generic", () => SharpPackSerializer.Deserialize<GenericModel<int>>(model)?.Value == 42);
Check("closed generic context", () => SharpPackSerializer.Deserialize<GenericContextModel<int>>(model, context)?.Value == 43);
Check("external union", () => SharpPackSerializer.Deserialize<IExternalUnion>(new byte[] { 3, 1, 42, 0, 0, 0 }) is ExternalUnionItem { Value: 42 });
Check("closed external union", () => SharpPackSerializer.Deserialize<IClosedExternalUnion<int>>(new byte[] { 4, 1, 42, 0, 0, 0 }) is ClosedExternalUnionItem<int> { Value: 42 });
Check("member custom formatter", () => SharpPackSerializer.Deserialize<CustomMemberModel>(model)?.Value == 43);
Check("nested namespaced generic", () => SharpPackSerializer.Deserialize<AotNames.Outer<int>.Inner<long>>(model)?.Value == 42);
var remoteContext = new SharpPackSerializerContextBuilder()
    .Register(new PlusOneFormatter())
    .RegisterFactory<IRemoteUnion, RemoteUnionFormatter>()
    .Build();
Check("cross-assembly registered union", () => SharpPackSerializer.Deserialize<IRemoteUnion>(new byte[] { 5, 1, 42, 0, 0, 0 }, remoteContext) is RemoteUnionItem { Value: 43 });
// Literal wire bytes avoid any Type or model Serialize call in this program.
byte[] typeId = [250, 255, 255, 255, 5, 0, 0, 0, 107, 110, 111, 119, 110]; // "known"
var typeContext = new SharpPackSerializerContextBuilder().EnableTypeSerialization().RegisterType<int>("known").Build();
Check("Type registered", () => SharpPackSerializer.Deserialize<Type>(typeId, typeContext) == typeof(int));
Check("Type disabled", () => Reject(() => SharpPackSerializer.Deserialize<Type>(typeId)));
Check("Type unknown", () => Reject(() => SharpPackSerializer.Deserialize<Type>(typeId, new SharpPackSerializerContextBuilder().EnableTypeSerialization().Build())));
if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
Console.WriteLine("Deserialize-only AOT: all fifteen cases passed.");
static bool Reject(Action action)
{
    try { action(); return false; }
    catch (SharpPackSerializationException) { return true; }
}
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

[SharpPackable] public partial class GenericModel<T> { public T? Value { get; set; } }
[SharpPackable] public partial class GenericContextModel<T> { public T? Value { get; set; } }
[SharpPackable] public partial class CustomMemberModel { [PlusOne] public int Value { get; set; } }
public sealed class PlusOneAttribute : SharpPackCustomFormatterAttribute<PlusOneFormatter, int>
{
    public override PlusOneFormatter GetFormatter() => new();
}
[SharpPackable(GenerateType.NoGenerate)] public partial interface IExternalUnion
{
    // A generated root must coexist with an application-owned static ctor.
    static IExternalUnion() { _ = __SharpPackPreserveFactoryValue; }
    private static bool __SharpPackPreserveFactory() => false;
    private static readonly bool __SharpPackPreserveFactoryValue = false;
}
[SharpPackable] public partial class ExternalUnionItem : IExternalUnion { public int Value { get; set; } }
[SharpPackUnionFormatter(typeof(IExternalUnion))]
[SharpPackUnion(3, typeof(ExternalUnionItem))]
public partial class ExternalUnionFormatter;
[SharpPackable(GenerateType.NoGenerate)] public partial interface IClosedExternalUnion<T>;
[SharpPackable] public partial class ClosedExternalUnionItem<T> : IClosedExternalUnion<T> { public T? Value { get; set; } }
[SharpPackUnionFormatter(typeof(IClosedExternalUnion<int>))]
[SharpPackUnion(4, typeof(ClosedExternalUnionItem<int>))]
public partial class ClosedExternalUnionFormatter;

namespace AotNames
{
    public partial class Outer<T>
    {
        [SharpPackable] public partial class Inner<U> { public int Value { get; set; } }
    }
}

[SharpPackable] public partial class RemoteUnionItem : IRemoteUnion { public int Value { get; set; } }
[SharpPackUnionFormatter(typeof(IRemoteUnion))]
[SharpPackUnion(5, typeof(RemoteUnionItem))]
public partial class RemoteUnionFormatter;
