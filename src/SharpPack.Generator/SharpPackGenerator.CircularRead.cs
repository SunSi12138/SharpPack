using System.Text;

namespace SharpPack.Generator;

internal sealed partial class CSharpEmitter
{
    string EmitCircularReadConstructor(MemberShape[] members, CircularReadShape construction)
    {
        var tag = plan.Names.CircularReadTag;
        var name = "@" + Shape.SimpleName;
        // An additional constructor would otherwise remove the user's implicit one.
        var defaultConstructor = construction.NeedsImplicitConstructor ? $"    public {name}() {{ }}" : "";
        // Required describes construction in C#, not which slots an older payload must carry.
        var requiredAttribute = construction.SetsRequiredMembers ? """
    #pragma warning disable CS8618 // Absent wire members keep constructor defaults.
    [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
""" : "";
        return $$"""

    private enum {{tag}} { }
{{defaultConstructor}}
{{requiredAttribute}}
    private {{name}}({{tag}} _, scoped ref global::SharpPack.SharpPackReader reader,
        uint id, scoped global::System.ReadOnlySpan<int> deltas, bool useFormatterOverrides) : this()
    {
        reader.OptionalState.AddObjectReference(id, this);
{{EmitCircularReadMembers(members, "this", honorFormatterOverrides: null)}}
    }
{{(construction.SetsRequiredMembers ? "    #pragma warning restore CS8618" : "")}}
""";
    }

    string EmitCircularReadBody(MemberShape[] members, CircularReadShape construction, bool honorFormatterOverrides)
    {
        var instantiate = $"value = new {TypeName}(default({plan.Names.CircularReadTag}), ref reader, id, deltas, " +
            (honorFormatterOverrides ? "true);" : "false);");
        if (construction.ReplacesExistingValue)
        {
            return $$"""
        id = reader.ReadVarIntUInt32();
        {{instantiate}}
""";
        }

        return $$"""
        id = reader.ReadVarIntUInt32();
        if (value == null)
        {
            {{instantiate}}
            goto END;
        }
        reader.OptionalState.AddObjectReference(id, value);
{{EmitCircularReadMembers(members, "value", honorFormatterOverrides)}}
""";
    }

    string EmitCircularReadMembers(MemberShape[] members, string target, bool? honorFormatterOverrides)
    {
        var snapshots = new StringBuilder();
        var read = new StringBuilder();
        var assign = new StringBuilder();
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            var hasPayload = $"deltas.Length > {i} && deltas[{i}] != 0";
            var normalRead = member.EmitReadRefDeserialize(i, requireDeltaCheck: false);
            if (member.Kind == MemberKind.Blank)
            {
                read.AppendLine($"        if ({hasPayload}) {normalRead}");
                continue;
            }

            var name = member.Name;
            // Present init slots hydrate a new value. Seeding them from constructor defaults
            // could register the same child for two distinct wire IDs.
            var initialValue = member.IsInitOnly ? "default!" : $"{hasPayload} ? {target}.@{name} : default!";
            snapshots.AppendLine($"        {member.TypeName} __{name} = {initialValue};");
            read.AppendLine($$"""
        if ({{hasPayload}})
        {
""");

            var overrideRead = plan.ContextDependencies.HasOverridableWireMembers
                ? member.EmitReadRefDeserialize(i, requireDeltaCheck: false, honorFormatterOverrides: true)
                : normalRead;
            if (honorFormatterOverrides is null && overrideRead != normalRead)
            {
                read.AppendLine($"            if (useFormatterOverrides) {{ {overrideRead} }}");
                read.AppendLine($"            else {{ {normalRead} }}");
            }
            else
            {
                read.AppendLine("            " + (honorFormatterOverrides == true ? overrideRead : normalRead));
            }
            read.AppendLine("        }");

            if (member.IsSettable)
                assign.AppendLine($"        if ({hasPayload}) {target}.@{name} = __{name}!;");
        }
        // Snapshot mutable reuse targets before nested callbacks, then stage assignments as on the ordinary path.
        return snapshots.Append(read).Append(assign).AppendLine($$"""
        for (int i = {{members.Length}}; i < deltas.Length; i++)
        {
            reader.Advance(deltas[i]);
        }
""").ToString();
    }
}
