using SharpPack;

#if STATIC_FACTORY_ROOTS
// Static-abstract calls root native code; Delegate.Method requests the metadata
// used by the existing resolver. Nothing is stored in a global registry.
GC.KeepAlive(DefaultFactory<HandwrittenFactoryModel, HandwrittenFactoryModel>().Method);
GC.KeepAlive(ContextFactory<HandwrittenFactoryModel, HandwrittenFactoryModel>().Method);
static Func<SharpPackFormatter<T>> DefaultFactory<T, TFactory>()
    where TFactory : ISharpPackFormatterFactory<T> => TFactory.CreateFormatter;
static Func<SharpPackSerializerContext, SharpPackFormatter<T>> ContextFactory<T, TFactory>()
    where TFactory : ISharpPackContextFormatterFactory<T> => TFactory.CreateFormatter;
#elif DECLARATIVE_FACTORY_ROOTS
PreserveHandwrittenFactories();
[System.Diagnostics.CodeAnalysis.DynamicDependency("SharpPack.ISharpPackFormatterFactory<HandwrittenFactoryModel>.CreateFormatter", typeof(HandwrittenFactoryModel))]
[System.Diagnostics.CodeAnalysis.DynamicDependency("SharpPack.ISharpPackContextFormatterFactory<HandwrittenFactoryModel>.CreateFormatter", typeof(HandwrittenFactoryModel))]
static void PreserveHandwrittenFactories() { }
#endif

byte[] payload = [42, 0, 0, 0];
var failures = 0;
#if REGISTER_DEFAULT_FACTORY
var registered = new SharpPackSerializerContextBuilder().RegisterFactory<HandwrittenFactoryModel, HandwrittenFactoryModel>().Build();
Check("registered handwritten default factory", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, registered)?.Value == 42);
#else
Check("handwritten explicit default", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload)?.Value == 42);
Check("handwritten explicit context", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, new SharpPackSerializerContextBuilder().Register(new MarkerIntFormatter()).Build())?.Value == 43);
#endif
Environment.ExitCode = failures == 0 ? 0 : 1;
void Check(string name, Func<bool> read)
{
    try { if (!read()) throw new InvalidOperationException("Incorrect value"); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
}
// Intentionally no generator, cctor, explicit root or prior model construction.
public sealed class HandwrittenFactoryModel : ISharpPackFormatterFactory<HandwrittenFactoryModel>, ISharpPackContextFormatterFactory<HandwrittenFactoryModel>
{
    public int Value { get; set; }
    static SharpPackFormatter<HandwrittenFactoryModel> ISharpPackFormatterFactory<HandwrittenFactoryModel>.CreateFormatter() => new HandwrittenFormatter(0);
    static SharpPackFormatter<HandwrittenFactoryModel> ISharpPackContextFormatterFactory<HandwrittenFactoryModel>.CreateFormatter(SharpPackSerializerContext context) => new HandwrittenFormatter(1);
}
public sealed class HandwrittenFormatter(int offset) : SharpPackFormatter<HandwrittenFactoryModel>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref HandwrittenFactoryModel? value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref HandwrittenFactoryModel? value)
    {
        reader.ReadUnmanaged(out int raw);
        value = new HandwrittenFactoryModel { Value = raw + offset };
    }
}

public sealed class MarkerIntFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value) => throw new NotSupportedException();
    public override void Deserialize(ref SharpPackReader reader, scoped ref int value) => reader.ReadUnmanaged(out value);
}
