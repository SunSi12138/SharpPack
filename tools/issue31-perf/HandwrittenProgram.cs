using SharpPack;

byte[] payload = [42, 0, 0, 0];
var failures = 0;
Check("handwritten explicit default", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload)?.Value == 42);
Check("handwritten explicit context", () => SharpPackSerializer.Deserialize<HandwrittenFactoryModel>(payload, new SharpPackSerializerContextBuilder().Build())?.Value == 43);
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
