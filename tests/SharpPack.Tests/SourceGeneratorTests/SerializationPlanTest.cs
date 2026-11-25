using SharpPack.Generator;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace SharpPack.Tests.SourceGeneratorTests;

public class SerializationPlanTest
{
    const string Models = """
        using SharpPack;
        [SharpPackable] public partial class Empty { }
        [SharpPackable] public partial class Fixed { public int Count { get; set; } }
        [SharpPackable] public partial class Exact { public int Count; public string? Text { get; set; } }
        [SharpPackable] public partial class ExactArray { public long[]? Items { get; set; } }
        [SharpPackable] public partial class Computed
        {
            public string Text { get => "value"; set { } }
        }
        [SharpPackable] public partial class Callbacks
        {
            public int Count;
            [SharpPackOnSerialized] void Finish() { }
        }
        [SharpPackable(GenerateType.VersionTolerant)] public partial class Versioned
        {
            [SharpPackOrder(0)] public int Count;
        }
        [SharpPackable] public partial struct Unmanaged { public int Count; }
        [SharpPackable] public partial class Tagged
        {
            [Utf8StringFormatter] public string? Text { get; set; }
        }
        """;

    [Theory]
    [InlineData("Empty", "FixedSize")]
    [InlineData("Fixed", "FixedSize")]
    [InlineData("Exact", "ExactSize")]
    [InlineData("ExactArray", "ExactSize")]
    [InlineData("Computed", "General")]
    [InlineData("Callbacks", "General")]
    [InlineData("Versioned", "General")]
    [InlineData("Unmanaged", "General")]
    [InlineData("Tagged", "General")]
    public void RootPathSnapshot(string name, string expected)
    {
        var plan = SerializationPlanner.Plan(TypeShapeTest.Analyze(Models, name));
        Assert.Equal(expected, plan.RootFastPath.ToString());
    }

    [Theory]
    [InlineData("VersionTolerant")]
    [InlineData("CircularReference")]
    public void SparseWireLayoutIncludesUnoccupiedSlots(string mode)
    {
        var shape = TypeShapeTest.Analyze(SparseModel(mode, "Explicit", 3), "Sparse");
        var plan = SerializationPlanner.Plan(shape);
        Assert.Equal(new[]
        {
            new WireSlot(0, 0), new WireSlot(1, null), new WireSlot(2, null), new WireSlot(3, 1),
        }, plan.WireSlots.ToArray());
        Assert.Equal(new[] { "First", "Last" }, shape.Members.Select(member => member.Name).ToArray());
    }

    [Fact]
    public void NonVersionedLayoutUsesDenseWirePositions()
    {
        const string source = """
            using SharpPack;
            [SharpPackable(SerializeLayout.Sequential)]
            public partial class Dense
            {
                [SharpPackOrder(7)] public int Value;
            }
            """;
        var shape = TypeShapeTest.Analyze(source, "Dense");
        Assert.Equal(7, Assert.Single(shape.Members).Order);
        Assert.Equal(new WireSlot(0, 0), Assert.Single(SerializationPlanner.Plan(shape).WireSlots));
    }

    [Theory]
    [InlineData("VersionTolerant", "Explicit", -1)]
    [InlineData("VersionTolerant", "Explicit", 249)]
    [InlineData("VersionTolerant", "Sequential", 249)]
    [InlineData("CircularReference", "Explicit", -1)]
    [InlineData("CircularReference", "Sequential", 249)]
    public void InvalidVersionedSlotReportsSharpPackDiagnostic(string mode, string layout, int order)
    {
        var (output, diagnostics) = CSharpGeneratorRunner.RunGenerator(SparseModel(mode, layout, order));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SHARPPACK044", diagnostic.Id);
        Assert.Contains("orders 0 through 248", diagnostic.GetMessage());
        Assert.Equal("Last", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.DoesNotContain(output.SyntaxTrees, tree => tree.FilePath.EndsWith(".SharpPackFormatter.g.cs"));
    }

    [Fact]
    public void HighestRepresentableSparseOrderStillCompiles()
    {
        var source = SparseModel("VersionTolerant", "Explicit", 248);
        var plan = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "Sparse"));
        Assert.Equal(249, plan.WireSlots.Length);
        Assert.Equal(new WireSlot(247, null), plan.WireSlots[247]);
        Assert.Equal(new WireSlot(248, 1), plan.WireSlots[248]);
        var (output, diagnostics) = CSharpGeneratorRunner.RunGenerator(source);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    static string SparseModel(string mode, string layout, int order) => $$"""
        using SharpPack;
        [SharpPackable(GenerateType.{{mode}}, SerializeLayout.{{layout}})]
        public partial class Sparse
        {
            [SharpPackOrder(0)] public int First;
            [SharpPackOrder({{order}})] public int Last;
        }
        """;
}
