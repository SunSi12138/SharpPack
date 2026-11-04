using SharpPack;

#if STATIC_FACTORY_ROOTS
SharpPackSerializer.PreserveFormatterFactory<HandwrittenFactoryModel>();
SharpPackSerializer.PreserveContextFormatterFactory<HandwrittenFactoryModel>();
SharpPackSerializer.PreserveFormatterFactory<HandwrittenGenericFactoryModel<long>>();
SharpPackSerializer.PreserveContextFormatterFactory<HandwrittenGenericFactoryModel<long>>();
SharpPackSerializer.PreserveFormatterFactory<DefaultOnlyModel>();
SharpPackSerializer.PreserveContextFormatterFactory<ContextOnlyModel>();
if (FactoryCalls.Default != 0 || FactoryCalls.Context != 0 || FactoryCalls.Initialization != 0)
    throw new InvalidOperationException("Preservation executed a factory.");
#endif

byte[] payload = [42, 0, 0, 0];
var failures = 0;
#if REGISTER_DEFAULT_FACTORY
var registered = new SharpPackSerializerContextBuilder().RegisterFactory<HandwrittenFactoryModel, HandwrittenFactoryModel>().Build();
Check("registered handwritten default factory", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, registered)?.Value == 42);
#else
var context = new SharpPackSerializerContextBuilder().Register(new MarkerIntFormatter()).Build();
Check("handwritten explicit default", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload)?.Value == 42);
Check("handwritten explicit context", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, context)?.Value == 43);
#if STATIC_FACTORY_ROOTS
Check("closed generic explicit default", () => SharpPackSerializer.Deserialize<HandwrittenGenericFactoryModel<long>>(payload)?.Value == 42);
Check("closed generic explicit context", () => SharpPackSerializer.Deserialize<HandwrittenGenericFactoryModel<long>>(payload, context)?.Value == 43);
Check("default-only factory", () => SharpPackSerializer.Deserialize<DefaultOnlyModel>(payload)?.Value == 42);
Check("context-only factory", () => SharpPackSerializer.Deserialize<ContextOnlyModel>(payload, context)?.Value == 43);
if (FactoryCalls.Default != 3 || FactoryCalls.Context != 3)
    throw new InvalidOperationException($"Factory invocation counts were {FactoryCalls.Default}/{FactoryCalls.Context}");
// Repeated reads must use existing slots, not execute a startup root or factory.
for (int i = 0; i < 100; i++)
{
    _ = SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload);
    _ = SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, context);
}
if (FactoryCalls.Default != 3 || FactoryCalls.Context != 3)
    throw new InvalidOperationException("Warm reads invoked a factory again.");
#endif
#endif
Environment.ExitCode = failures == 0 ? 0 : 1;
void Check(string name, Func<bool> read)
{
    try { if (!read()) throw new InvalidOperationException("Incorrect value"); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
}
public static class FactoryCalls { public static int Default; public static int Context; public static int Initialization; }
// No generator, explicit metadata names or early construction. The application cctor must not run during preservation.
public sealed class HandwrittenFactoryModel : ISharpPackFormatterFactory<HandwrittenFactoryModel>, ISharpPackContextFormatterFactory<HandwrittenFactoryModel>
{
    static HandwrittenFactoryModel() => FactoryCalls.Initialization++;
    public int Value { get; set; }
    static SharpPackFormatter<HandwrittenFactoryModel> ISharpPackFormatterFactory<HandwrittenFactoryModel>.CreateFormatter() { FactoryCalls.Default++; return new HandwrittenFormatter(0); }
    static SharpPackFormatter<HandwrittenFactoryModel> ISharpPackContextFormatterFactory<HandwrittenFactoryModel>.CreateFormatter(SharpPackSerializerContext context) { FactoryCalls.Context++; return new HandwrittenFormatter(1); }
}
public sealed class HandwrittenFormatter(int offset) : SharpPackFormatter<HandwrittenFactoryModel>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref HandwrittenFactoryModel? value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref HandwrittenFactoryModel? value) { reader.ReadUnmanaged(out int raw); value = new HandwrittenFactoryModel { Value = raw + offset }; }
}
public sealed class HandwrittenGenericFactoryModel<T> : ISharpPackFormatterFactory<HandwrittenGenericFactoryModel<T>>, ISharpPackContextFormatterFactory<HandwrittenGenericFactoryModel<T>>
{
    public int Value { get; set; }
    static SharpPackFormatter<HandwrittenGenericFactoryModel<T>> ISharpPackFormatterFactory<HandwrittenGenericFactoryModel<T>>.CreateFormatter() { FactoryCalls.Default++; return new GenericFormatter<T>(0); }
    static SharpPackFormatter<HandwrittenGenericFactoryModel<T>> ISharpPackContextFormatterFactory<HandwrittenGenericFactoryModel<T>>.CreateFormatter(SharpPackSerializerContext context) { FactoryCalls.Context++; return new GenericFormatter<T>(1); }
}
public sealed class GenericFormatter<T>(int offset) : SharpPackFormatter<HandwrittenGenericFactoryModel<T>>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref HandwrittenGenericFactoryModel<T>? value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref HandwrittenGenericFactoryModel<T>? value) { reader.ReadUnmanaged(out int raw); value = new HandwrittenGenericFactoryModel<T> { Value = raw + offset }; }
}
public sealed class DefaultOnlyModel : ISharpPackFormatterFactory<DefaultOnlyModel>
{
    public int Value { get; set; }
    static SharpPackFormatter<DefaultOnlyModel> ISharpPackFormatterFactory<DefaultOnlyModel>.CreateFormatter() { FactoryCalls.Default++; return new DefaultOnlyFormatter(); }
}
public sealed class DefaultOnlyFormatter : SharpPackFormatter<DefaultOnlyModel>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref DefaultOnlyModel? value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref DefaultOnlyModel? value) { reader.ReadUnmanaged(out int raw); value = new DefaultOnlyModel { Value = raw }; }
}
public sealed class ContextOnlyModel : ISharpPackContextFormatterFactory<ContextOnlyModel>
{
    public int Value { get; set; }
    static SharpPackFormatter<ContextOnlyModel> ISharpPackContextFormatterFactory<ContextOnlyModel>.CreateFormatter(SharpPackSerializerContext context) { FactoryCalls.Context++; return new ContextOnlyFormatter(); }
}
public sealed class ContextOnlyFormatter : SharpPackFormatter<ContextOnlyModel>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ContextOnlyModel? value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref ContextOnlyModel? value) { reader.ReadUnmanaged(out int raw); value = new ContextOnlyModel { Value = raw + 1 }; }
}
public sealed class MarkerIntFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref int value) => reader.ReadUnmanaged(out value);
}
