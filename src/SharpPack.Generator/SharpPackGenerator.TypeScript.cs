using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
                new TypeScriptEnumField(field.Name, field.HasConstantValue ? Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture) : null)))));
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
            return new(shape.ShortName, imports, shape.Union is null ? unionBase?.Name : null, members,
                shape.Union?.ProjectTypeScriptCases());
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
