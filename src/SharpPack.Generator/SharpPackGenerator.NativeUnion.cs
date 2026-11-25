using System.Text;

namespace SharpPack.Generator;

internal sealed partial class CSharpEmitter
{
    string EmitNativeUnionTemplate(UnionShape union)
    {
        var code = new StringBuilder();
        foreach (var container in Shape.ContainingTypes)
        {
            code.AppendLine(EmitPartialTypeDeclaration(container));
            code.AppendLine(EmitTypeParameterConstraints(container.TypeParameters));
            code.AppendLine("{");
        }

        var type = union.TypeName;
        var factory = $"global::SharpPack.ISharpPackFormatterFactory<{type}>";
        var packable = $"global::SharpPack.ISharpPackable<{type}>";
        var selfFactoryRoot = plan.Names.SelfFactoryRoot;
        var write = union.Cases.Select(item => $$"""
        if (actual == typeof({{item.RuntimeTypeName}}))
        {
            writer.WriteUnionHeader({{item.Tag}});
            writer.WriteValue<{{item.TypeName}}>(({{item.TypeName}})active);
            return;
        }
""").NewLine();
        var read = union.Cases.Select(item =>
            $"            case {item.Tag}: value = new {type}(reader.ReadValue<{item.TypeName}>()); return;").NewLine();

        code.AppendLine($$"""
partial union {{Shape.ShortName}} : {{packable}}, {{factory}}
{
    static partial void StaticConstructor();
{{EmitSelfFactoryRoots(selfFactoryRoot, includeContextFactory: false)}}
    [global::SharpPack.CompilerServices.Preserve]
    static global::SharpPack.SharpPackFormatter<{{type}}> {{factory}}.CreateFormatter()
    {
{{EmitFactoryRootRead(selfFactoryRoot)}}
{{EmitAotFormatterRoots(plan.AotRoots, "        ")}}
        return new global::SharpPack.Formatters.SharpPackableFormatter<{{type}}>();
    }

    static void {{packable}}.Serialize<TBufferWriter>(
        ref global::SharpPack.SharpPackWriter<TBufferWriter> writer, scoped ref {{type}} value)
    {
        var active = value.Value;
        if (active is null)
        {
            writer.WriteNullUnionHeader();
            return;
        }

        var actual = active.GetType();
{{write}}
        global::SharpPack.SharpPackSerializationException.ThrowNotFoundInUnionType(actual, typeof({{type}}));
    }

    static void {{packable}}.Deserialize(ref global::SharpPack.SharpPackReader reader, scoped ref {{type}} value)
    {
        if (!reader.TryReadUnionHeader(out var tag))
        {
            value = default;
            return;
        }

        switch (tag)
        {
{{read}}
            default:
                global::SharpPack.SharpPackSerializationException.ThrowInvalidTag(tag, typeof({{type}}));
                return;
        }
    }
}
""");
        for (var i = 0; i < Shape.ContainingTypes.Length; i++)
            code.AppendLine("}");
        return code.ToString();
    }
}
