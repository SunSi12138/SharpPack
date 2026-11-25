using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace SharpPack.Generator;

partial class SharpPackGenerator
{
    internal static ModelAnalysisResult AnalyzeModel(
        TypeDeclarationSyntax syntax, Compilation compilation, CancellationToken cancellationToken)
    {
        ImmutableArray<Diagnostic>.Builder? diagnostics = null;
        var shape = ExtractModel(syntax, compilation, cancellationToken, Report);
        return new(shape, diagnostics?.ToImmutable() ?? ImmutableArray<Diagnostic>.Empty);

        void Report(Diagnostic diagnostic)
            => (diagnostics ??= ImmutableArray.CreateBuilder<Diagnostic>()).Add(diagnostic);
    }

    static TypeShape? ExtractModel(
        TypeDeclarationSyntax syntax, Compilation compilation, CancellationToken cancellationToken, Action<Diagnostic> reportDiagnostic)
    {
        var symbol = compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax, cancellationToken);
        if (symbol is null) return null;

        if (!IsPartial(syntax))
        {
            reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.MustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
            return null;
        }
        for (var parent = syntax.Parent as TypeDeclarationSyntax; parent is not null; parent = parent.Parent as TypeDeclarationSyntax)
        {
            if (!IsPartial(parent))
            {
                reportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.NestedContainingTypesMustBePartial, syntax.Identifier.GetLocation(), symbol.Name));
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
        if (!metadata.Validate(syntax, reportDiagnostic, unionFormatter: union is not null))
            return null;

        return TypeShape.Create(symbol, metadata, references, isExternalFormatter: union is not null);

        static bool IsPartial(TypeDeclarationSyntax declaration)
            => declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
    }
}

internal readonly record struct ModelAnalysisResult(TypeShape? Shape, ImmutableArray<Diagnostic> Diagnostics);
