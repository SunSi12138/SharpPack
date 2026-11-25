using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPack.Generator;

partial class SharpPackGenerator
{
    internal static TypeShape? AnalyzeModel(
        TypeDeclarationSyntax syntax, Compilation compilation, IGeneratorContext context)
    {
        var symbol = compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax, context.CancellationToken);
        if (symbol is null) return null;

        if (!IsPartial(syntax))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            return null;
        }
        for (var parent = syntax.Parent as TypeDeclarationSyntax; parent is not null; parent = parent.Parent as TypeDeclarationSyntax)
        {
            if (!IsPartial(parent))
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NestedContainingTypesMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
                return null;
            }
        }

        var references = new ReferenceSymbols(compilation);
        INamedTypeSymbol? union = null;
        if (symbol.GetAttribute(references.SharpPackUnionFormatterAttribute) is { } unionFormatter)
        {
            union = unionFormatter.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (union is null) return null;
        }

        var metadata = new TypeMeta(symbol, references);
        if (union is not null) metadata.Symbol = union;
        if (union is null && !metadata.IsUnion && metadata.GenerateType == GenerateType.NoGenerate)
            return null;
        if (!metadata.Validate(syntax, context, unionFormatter: union is not null))
            return null;

        return TypeShape.Create(symbol, metadata, references, isExternalFormatter: union is not null);

        static bool IsPartial(TypeDeclarationSyntax declaration)
            => declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
    }
}
