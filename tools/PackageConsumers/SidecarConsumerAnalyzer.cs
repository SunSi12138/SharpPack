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
            if (result.Source is not null)
                output.AddSource(result.HintName, result.Source);
        });
    }
}
