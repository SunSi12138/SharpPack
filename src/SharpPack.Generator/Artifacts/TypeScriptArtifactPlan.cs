namespace SharpPack.Generator;

internal sealed record TypeScriptArtifactPlan(
    string ImportExtension, EquatableArray<TypeScriptModelPlan> Models, EquatableArray<TypeScriptEnumPlan> Enums);

internal sealed record TypeScriptModelPlan(
    string Name, EquatableArray<string> Imports, string? UnionBase, EquatableArray<TypeScriptMemberShape> Members,
    EquatableArray<TypeScriptUnionCase>? UnionCases);

internal sealed record TypeScriptUnionCase(ushort Tag, string Name);

internal sealed record TypeScriptMemberShape(
    string MemberName, string TypeName, string DefaultValue, string WriteMethodTemplate, string ReadMethodTemplate);

internal sealed record TypeScriptEnumPlan(string Name, EquatableArray<TypeScriptEnumField> Fields);

internal sealed record TypeScriptEnumField(string Name, string? Constant);
