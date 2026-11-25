using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace SharpPack.Generator;

public partial class SharpPackGenerator
{
    static void RegisterExternalFormatters(IncrementalGeneratorInitializationContext context)
    {
        var plan = context.CompilationProvider.Select(static (compilation, token) =>
        {
            var rootAttribute = compilation.GetTypeByMetadataName("SharpPack.SharpPackExternalTypeAttribute");
            if (rootAttribute is null) return null;

            var roots = ImmutableArray.CreateBuilder<SharpPackSidecarRoot>();
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, rootAttribute)) continue;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(token).GetLocation();
                if (attribute.ConstructorArguments is { Length: 1 } arguments && arguments[0].Value is ITypeSymbol type)
                    roots.Add(new(type, location));
                else
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedSidecarRoot, location,
                        "assembly root", "SharpPackExternalType", "the attribute must specify a closed type with typeof"));
            }
            if (roots.Count == 0 && diagnostics.Count == 0) return null;

            var result = SharpPackSidecarGenerator.Generate(compilation, roots.ToImmutable(),
                "SharpPackExternalFormatters", cancellationToken: token);
            return diagnostics.Count == 0 ? result :
                new SharpPackSidecarResult(result.HintName, null, result.Diagnostics.AddRange(diagnostics), result.Roots);
        });
        context.RegisterSourceOutput(plan, static (output, result) =>
        {
            if (result is null) return;
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);
            if (result.Diagnostics.IsEmpty && result.Source is { } source)
                output.AddSource(result.HintName, source);
        });
    }
}
