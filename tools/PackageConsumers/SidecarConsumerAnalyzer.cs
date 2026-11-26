using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpPack.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class SidecarConsumerAnalyzer : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var routed = context.CompilationProvider.Select(static (compilation, token) =>
        {
            var root = compilation.GetTypeByMetadataName("SidecarFixture.ExternalNote");
            return root is null ? null : SharpPackSidecarGenerator.Generate(
                compilation, ImmutableArray.Create(new SharpPackSidecarRoot(root)), "PackageRoute", cancellationToken: token);
        });
        context.RegisterSourceOutput(routed, static (output, result) =>
        {
            if (result is null) return;
            foreach (var diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);
            if (result.Source is not { } source) return;

            var root = result.Roots[0];
            var shape = root.Sidecars[0];
            if (!SymbolEqualityComparer.Default.Equals(shape.Type, root.Type) ||
                shape.Members[0].Type.SpecialType != SpecialType.System_Int32 ||
                shape.Members[1].Type.SpecialType != SpecialType.System_String)
                throw new InvalidOperationException("The packaged planner did not expose the ordered wire type symbols.");

            output.AddSource(result.HintName, source);
        });
    }
}
