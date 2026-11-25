using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SharpPack.Generator;

public record TypeScriptGenerateOptions
{
    public string OutputDirectory { get; set; } = default!;
    public string ImportExtension { get; set; } = default!;
    public bool ConvertPropertyName { get; set; } = true;
    public bool EnableNullableTypes { get; set; } = false;
    public bool IsDesignTimeBuild { get; set; } = false;
}

partial class SharpPackGenerator
{
    internal static TypeScriptArtifactPlan? PlanTypeScript(
        IEnumerable<TypeDeclarationSyntax> declarations, Compilation compilation, TypeScriptGenerateOptions options,
        CancellationToken cancellationToken, Action<Diagnostic> reportDiagnostic)
    {
        var inputs = declarations.Select(syntax => (Syntax: syntax,
            Symbol: compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax, cancellationToken))).ToArray();
        if (!inputs.Any(input => input.Symbol is not null)) return null;

        var reference = new ReferenceSymbols(compilation);
        var unionMap = new Dictionary<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (_, symbol) in inputs)
        {
            if (symbol is null || !symbol.ContainsAttribute(reference.SharpPackUnionAttribute)) continue;
            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, reference.SharpPackUnionAttribute) &&
                    attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[1].Value is INamedTypeSymbol variant)
                    unionMap[variant] = symbol;
            }
        }

        var models = new List<TypeScriptModelPlan>();
        var enumCollector = new TypeCollector();
        foreach (var (syntax, symbol) in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is null) continue;
            var model = PlanTypeScriptModel(syntax, symbol, options, reference, unionMap, enumCollector, reportDiagnostic);
            if (model is not null) models.Add(model);
        }

        var enums = enumCollector.GetEnums().OfType<INamedTypeSymbol>().Select(static symbol => new TypeScriptEnumPlan(
            symbol.Name, new EquatableArray<TypeScriptEnumField>(symbol.GetMembers().OfType<IFieldSymbol>().Select(static field =>
                new TypeScriptEnumField(field.Name, field.HasConstantValue ? $"{field.ConstantValue}" : null)))));
        return new(options.ImportExtension, new(models), new(enums));
    }

    static TypeScriptModelPlan? PlanTypeScriptModel(TypeDeclarationSyntax syntax, INamedTypeSymbol typeSymbol,
        TypeScriptGenerateOptions options, ReferenceSymbols reference, IReadOnlyDictionary<ITypeSymbol, ITypeSymbol> unionMap,
        TypeCollector enumCollector, Action<Diagnostic> reportDiagnostic)
    {
        // require [SharpPackable]
        if (!typeSymbol.ContainsAttribute(reference.SharpPackableAttribute))
        {
            reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptMustBeSharpPackable, syntax.Identifier.GetLocation(), typeSymbol.Name));
            return null;
        }

        var typeMeta = new TypeMeta(typeSymbol, reference);

        if (typeMeta.GenerateType is not (GenerateType.Object or GenerateType.Union))
        {
            reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptOnlyAllowsGenerateTypeObject, syntax.Identifier.GetLocation(), typeSymbol.Name));
            return null;
        }

        if (!ValidateTypeScript(typeMeta, syntax, reportDiagnostic))
        {
            return null;
        }

        var collector = new TypeCollector();
        collector.Visit(typeMeta, true);

        // validate invalid enum
        foreach (var item in collector.GetEnums())
        {
            if (item.TypeKind == TypeKind.Enum && item is INamedTypeSymbol nts)
            {
                if (nts.EnumUnderlyingType!.SpecialType is SpecialType.System_Int64 or SpecialType.System_UInt64)
                {
                    reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptDoesNotAllowLongEnum, syntax.Identifier.GetLocation(), typeSymbol.Name, item.FullyQualifiedToString()));
                    return null;
                }
            }
        }

        var imports = new EquatableArray<string>(collector.GetEnums().Concat(collector.GetSharpPackableTypes(reference)
            .Where(type => !SymbolEqualityComparer.Default.Equals(type, typeSymbol) && !type.IsSharpPackableNoGenerate(reference)))
            .Select(static type => type.Name));

        try
        {
            var shape = TypeShape.Create(typeSymbol, typeMeta, reference);
            var members = shape.Union is null ? new EquatableArray<TypeScriptMemberShape>(SerializationPlanner.PlanWireSlots(shape)
                .Select(slot => TypeScriptMember.Project(typeMeta.Members[slot.DeclaredMemberIndex!.Value], reference, options))) : default;
            unionMap.TryGetValue(typeSymbol, out var unionBase);
            enumCollector.Visit(typeMeta, false);
            return new(shape.ShortName, imports, unionBase?.Name, members, shape.Union);
        }
        catch (NotSupportedTypeException ex)
        {
            reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptNotSupportedType, ex.MemberMeta!.GetLocation(syntax),
                typeMeta.Symbol.Name, ex.MemberMeta.Name, ex.MemberMeta.MemberType.FullyQualifiedToString()));
            return null;
        }
    }

    static void WriteLegacyTypeScript(TypeScriptArtifactPlan plan, string directory)
    {
        var encoding = new UTF8Encoding(false);
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        foreach (var model in plan.Models)
        {
            try
            {
                File.WriteAllText(Path.Combine(directory, $"{model.Name}.ts"), TypeScriptEmitter.EmitModelFile(model, plan.ImportExtension), encoding);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }
        foreach (var model in plan.Enums)
            File.WriteAllText(Path.Combine(directory, $"{model.Name}.ts"), TypeScriptEmitter.EmitEnumFile(model), encoding);

        foreach (var (name, code) in TypeScriptEmitter.RuntimeFiles())
        {
            var file = Path.Combine(directory, name);
            if (!File.Exists(file)) File.WriteAllText(file, code, encoding);
        }
    }

    static bool ValidateTypeScript(TypeMeta type, TypeDeclarationSyntax syntax, Action<Diagnostic> reportDiagnostic)
    {
        var typeSymbol = type.Symbol;

        if (type.Symbol.IsGenericType)
        {
            reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptDoesNotAllowGenerics, syntax.Identifier.GetLocation(), typeSymbol.Name));
            return false;
        }

        foreach (var item in type.Members)
        {
            if (item.Kind == MemberKind.CustomFormatter)
            {
                reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.GenerateTypeScriptNotSupportedCustomFormatter, item.GetLocation(syntax), typeSymbol.Name));
                return false;
            }
        }

        return true;
    }
}

internal sealed record TypeScriptArtifactPlan(
    string ImportExtension, EquatableArray<TypeScriptModelPlan> Models, EquatableArray<TypeScriptEnumPlan> Enums);

internal sealed record TypeScriptModelPlan(
    string Name, EquatableArray<string> Imports, string? UnionBase, EquatableArray<TypeScriptMemberShape> Members, UnionShape? Union);

internal sealed record TypeScriptEnumPlan(string Name, EquatableArray<TypeScriptEnumField> Fields);

internal sealed record TypeScriptEnumField(string Name, string? Constant);

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

        if (model.Union is { } union)
            EmitUnion(writer, model.Name, union.Cases, importExtension);
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

    internal static void EmitUnion(StringBuilder sb, string typeName, EquatableArray<UnionCaseShape> cases, string importExt)
    {
        string EmitUnionSerialize()
        {
            var sb = new StringBuilder();
            foreach (var item in cases)
            {
                sb.AppendLine($$"""
        else if (value instanceof {{item.SimpleName}}) {
            writer.writeUnionHeader({{item.Tag}});
            {{item.SimpleName}}.serializeCore(writer, value);
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
                return {{item.SimpleName}}.deserializeCore(reader);
""");
            }
            return sb.ToString();
        }

        foreach (var item in cases)
        {
            sb.AppendLine($"import {{ {item.SimpleName} }} from \"./{item.SimpleName}{importExt}\"; ");
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
