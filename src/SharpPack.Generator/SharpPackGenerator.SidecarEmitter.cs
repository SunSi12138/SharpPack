using System.Text;

namespace SharpPack.Generator;

internal sealed partial class CSharpEmitter
{
    // The declaration shell is different; the object wire body is the same.
    internal static GeneratedSource EmitObjectSidecar(SerializationPlan plan, string formatterName)
    {
        var emitter = new CSharpEmitter(plan, plan.Shape.TargetName);
        var members = emitter.WireMembers();
        var typeName = emitter.TypeName;
        var nullable = emitter.IsValueType ? "" : "?";

        var source = new StringBuilder();
        AppendPreamble(source);
        source.AppendLine($$"""
namespace SharpPack.Generated
{
    internal sealed class {{formatterName}} : global::SharpPack.SharpPackFormatter<{{typeName}}>
    {
        public override void Serialize<TBufferWriter>(
            ref global::SharpPack.SharpPackWriter<TBufferWriter> writer, scoped ref {{typeName}}{{nullable}} value)
        {
{{emitter.EmitSerializeBody(members)}}
        END:
            return;
        }

        public override void Deserialize(ref global::SharpPack.SharpPackReader reader, scoped ref {{typeName}}{{nullable}} value)
        {
{{emitter.EmitDeserializeBody(members, honorFormatterOverrides: false)}}
        END:
            return;
        }
    }
}
""");
        return new(formatterName + ".g.cs", source.ToString());
    }
}
