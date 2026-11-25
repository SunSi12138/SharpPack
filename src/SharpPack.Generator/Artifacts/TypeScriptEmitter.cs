using System.Text;

namespace SharpPack.Generator;

internal static class TypeScriptEmitter
{
    internal static string EmitModelFile(TypeScriptModelPlan model, string importExtension)
    {
        var writer = new StringBuilder();
        writer.AppendLine($$"""
import { SharpPackWriter } from "./SharpPackWriter{{importExtension}}";
import { SharpPackReader } from "./SharpPackReader{{importExtension}}";
""");
        foreach (var name in model.Imports)
            writer.AppendLine($"import {{ {name} }} from \"./{name}{importExtension}\";");
        writer.AppendLine();

        if (model.UnionCases is { } cases)
            EmitUnion(writer, model.Name, cases, importExtension);
        else
            EmitObject(writer, model.Name, model.UnionBase, model.Members.ToArray());
        return writer.ToString();
    }

    internal static string EmitEnumFile(TypeScriptEnumPlan model)
    {
        var fields = new StringBuilder();
        foreach (var field in model.Fields)
        {
            var value = field.Constant is { } constant ? $" = {constant}" : "";
            fields.AppendLine($"    {field.Name}{value},");
        }
        return $$"""
export const enum {{model.Name}} {
{{fields.ToString()}}
}
""";
    }

    internal static IEnumerable<(string Name, string Source)> RuntimeFiles()
    {
        yield return ("SharpPackWriter.ts", TypeScriptRuntime.SharpPackWriter);
        yield return ("SharpPackReader.ts", TypeScriptRuntime.SharpPackReader);
    }

    internal static void EmitObject(StringBuilder sb, string typeName, string? unionBase, TypeScriptMemberShape[] tsMembers)
    {
        var impl = unionBase is not null ? $"implements {unionBase} " : "";

        var code = $$"""
export class {{typeName}} {{impl}}{
{{EmitTypeScriptMembers(tsMembers)}}
    constructor() {
{{EmitTypeScriptMembersInit(tsMembers)}}
    }

    static serialize(value: {{typeName}} | null): Uint8Array {
        const writer = SharpPackWriter.getSharedInstance();
        this.serializeCore(writer, value);
        return writer.toArray();
    }

    static serializeCore(writer: SharpPackWriter, value: {{typeName}} | null): void {
        if (value == null) {
            writer.writeNullObjectHeader();
            return;
        }

{{EmitTypeScriptSerializeBody(tsMembers)}}
    }

    static serializeArray(value: ({{typeName}} | null)[] | null): Uint8Array {
        const writer = SharpPackWriter.getSharedInstance();
        this.serializeArrayCore(writer, value);
        return writer.toArray();
    }

    static serializeArrayCore(writer: SharpPackWriter, value: ({{typeName}} | null)[] | null): void {
        writer.writeArray(value, (writer, x) => {{typeName}}.serializeCore(writer, x));
    }

    static deserialize(buffer: ArrayBuffer): {{typeName}} | null {
        return this.deserializeCore(new SharpPackReader(buffer));
    }

    static deserializeCore(reader: SharpPackReader): {{typeName}} | null {
        const [ok, count] = reader.tryReadObjectHeader();
        if (!ok) {
            return null;
        }

        const value = new {{typeName}}();
        if (count == {{tsMembers.Length}}) {
{{EmitTypeScriptDeserializeBody(tsMembers, false)}}
        }
        else if (count > {{tsMembers.Length}}) {
            throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
        }
        else {
{{EmitTypeScriptDeserializeBody(tsMembers, true)}}
        }
        return value;
    }

    static deserializeArray(buffer: ArrayBuffer): ({{typeName}} | null)[] | null {
        return this.deserializeArrayCore(new SharpPackReader(buffer));
    }

    static deserializeArrayCore(reader: SharpPackReader): ({{typeName}} | null)[] | null {
        return reader.readArray(reader => {{typeName}}.deserializeCore(reader));
    }
}
""";

        sb.AppendLine(code);
    }

    internal static void EmitUnion(StringBuilder sb, string typeName, EquatableArray<TypeScriptUnionCase> cases, string importExt)
    {
        string EmitUnionSerialize()
        {
            var sb = new StringBuilder();
            foreach (var item in cases)
            {
                sb.AppendLine($$"""
        else if (value instanceof {{item.Name}}) {
            writer.writeUnionHeader({{item.Tag}});
            {{item.Name}}.serializeCore(writer, value);
            return;
        }
""");
            }
            return sb.ToString();
        }

        string EmitUnionDeserialize()
        {
            var sb = new StringBuilder();
            foreach (var item in cases)
            {
                sb.AppendLine($$"""
            case {{item.Tag}}:
                return {{item.Name}}.deserializeCore(reader);
""");
            }
            return sb.ToString();
        }

        foreach (var item in cases)
        {
            sb.AppendLine($"import {{ {item.Name} }} from \"./{item.Name}{importExt}\"; ");
        }
        sb.AppendLine();

        var code = $$"""
export abstract class {{typeName}} {
    static serialize(value: {{typeName}} | null): Uint8Array {
        const writer = SharpPackWriter.getSharedInstance();
        this.serializeCore(writer, value);
        return writer.toArray();
    }

    static serializeCore(writer: SharpPackWriter, value: {{typeName}} | null): void {
        if (value == null) {
            writer.writeNullObjectHeader();
            return;
        }
{{EmitUnionSerialize()}}
        else {
            throw new Error("Concrete type is not in SharpPackUnion");
        }
    }

    static serializeArray(value: {{typeName}}[] | null): Uint8Array {
        const writer = SharpPackWriter.getSharedInstance();
        this.serializeArrayCore(writer, value);
        return writer.toArray();
    }

    static serializeArrayCore(writer: SharpPackWriter, value: {{typeName}}[] | null): void {
        writer.writeArray(value, (writer, x) => {{typeName}}.serializeCore(writer, x));
    }

    static deserialize(buffer: ArrayBuffer): {{typeName}} | null {
        return this.deserializeCore(new SharpPackReader(buffer));
    }

    static deserializeCore(reader: SharpPackReader): {{typeName}} | null {
        const [ok, tag] = reader.tryReadUnionHeader();
        if (!ok) {
            return null;
        }

        switch (tag) {
{{EmitUnionDeserialize()}}
            default:
                throw new Error("Tag is not found in this SharpPackUnion");
        }
    }

    static deserializeArray(buffer: ArrayBuffer): ({{typeName}} | null)[] | null {
        return this.deserializeArrayCore(new SharpPackReader(buffer));
    }

    static deserializeArrayCore(reader: SharpPackReader): ({{typeName}} | null)[] | null {
        return reader.readArray(reader => {{typeName}}.deserializeCore(reader));
    }
}
""";
        sb.AppendLine(code);
    }

    internal static string EmitTypeScriptMembers(TypeScriptMemberShape[] members)
    {
        var sb = new StringBuilder();

        foreach (var item in members)
        {
            sb.AppendLine($"    {item.MemberName}: {item.TypeName};");
        }

        return sb.ToString();
    }

    internal static string EmitTypeScriptMembersInit(TypeScriptMemberShape[] members)
    {
        var sb = new StringBuilder();

        foreach (var item in members)
        {
            sb.AppendLine($"        this.{item.MemberName} = {item.DefaultValue};");
        }

        return sb.ToString();
    }

    internal static string EmitTypeScriptSerializeBody(TypeScriptMemberShape[] members)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"        writer.writeObjectHeader({members.Length});");
        foreach (var item in members)
        {
            sb.AppendLine($"        {string.Format(item.WriteMethodTemplate, "value." + item.MemberName)};");
        }

        return sb.ToString();
    }

    internal static string EmitTypeScriptDeserializeBody(TypeScriptMemberShape[] members, bool emitSkip)
    {
        var sb = new StringBuilder();

        if (!emitSkip)
        {
            foreach (var item in members)
            {
                sb.AppendLine($"            value.{item.MemberName} = {item.ReadMethodTemplate};");
            }
        }
        else
        {
            sb.AppendLine("            if (count == 0) return value;");
            for (int i = 0; i < members.Length; i++)
            {
                var item = members[i];
                sb.AppendLine($"            value.{item.MemberName} = {item.ReadMethodTemplate}; if (count == {i + 1}) return value;");
            }
        }


        return sb.ToString();
    }
}
