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
    public void GeneratedNamesAvoidMemberAndLexicalCollisions()
    {
        const string source = """
            using SharpPack;
            [SharpPackable]
            public partial class Model(int __SharpPackPreserveSelfFactoryRootsValue)
            {
                public int __sharppackpreserveselffactoryrootsvalue { get; } = __SharpPackPreserveSelfFactoryRootsValue;
                private void __SharpPackContextFormatter() { }
                private void __SharpPackSerializeWithFormatterOverrides() { }
                private void __SharpPackEnsureAotFormatterRoots() { }
            }
            [SharpPackable] public partial class Strict
            {
                private static readonly int seed = 1;
                public int Value { get; set; }
            }
            """;
        var model = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "Model")).Names;
        Assert.Equal("__SharpPackPreserveSelfFactoryRoots_", model.SelfFactoryRoot);
        Assert.Equal("__SharpPackContextFormatter_", model.ContextFormatterType);
        Assert.Equal("__SharpPackSerializeWithFormatterOverrides_", model.WriteWithOverrides);
        Assert.Equal("__SharpPackDeserializeWithFormatterOverrides", model.ReadWithOverrides);
        Assert.Equal("__SharpPackEnsureAotFormatterRoots_", model.EnsureAotRoots);
        Assert.Null(SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "Strict")).Names.SelfFactoryRoot);
    }

    [Fact]
    public void AotRootsSeparateTypedFactoriesFromClosedFormatterInstances()
    {
        const string source = """
            using SharpPack;
            [SharpPackable] public partial class Envelope
            {
                public List<Leaf[]>? Entries;
                public Leaf? Direct;
                public IKind? Kind;
            }
            [SharpPackable] public partial class Leaf { public int Id; }
            [SharpPackable, SharpPackUnion(0, typeof(Item))]
            public partial interface IKind { }
            [SharpPackable] public partial class Item : IKind { }
            """;
        var plan = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "Envelope"));
        Assert.Equal(new[]
        {
            new AotFactoryRoot("global::IKind", false),
            new AotFactoryRoot("global::Leaf", true),
        }, plan.AotRoots.Factories.ToArray());
        Assert.Equal(new[]
        {
            "global::SharpPack.Formatters.ArrayFormatter<global::Leaf>",
            "global::SharpPack.Formatters.ListFormatter<global::Leaf[]>",
        }, plan.AotRoots.ClosedFormatters.ToArray());
    }

    [Fact]
    public void ContextDependencySetTraversesArraysAndKnownGenericsOnlyOnce()
    {
        const string source = """
            using SharpPack;
            [SharpPackable] public partial class Model
            {
                public List<Child[]>? Sequence;
                public Child? Direct;
                [Utf8StringFormatter] public string? Custom;
            }
            [SharpPackable] public partial class Child { public int Number; }
            [SharpPackable] public partial class CustomOnly
            {
                [Utf8StringFormatter] public string? Text;
            }
            """;
        var plan = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "Model"));
        Assert.Equal(new[]
        {
            "global::System.Collections.Generic.List<global::Child[]>",
            "global::Child[]",
            "global::Child",
        }, plan.ContextDependencies.FactoryLookupTypes.ToArray());
        Assert.True(plan.ContextDependencies.HasOverridableWireMembers);

        var custom = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "CustomOnly")).ContextDependencies;
        Assert.Empty(custom.FactoryLookupTypes);
        Assert.False(custom.HasOverridableWireMembers);
    }

    [Fact]
    public void ScalarBatchesStopAtTheRuntimeOverloadArityAndMemberBoundaries()
    {
        var kinds = Enumerable.Repeat(MemberKind.Unmanaged, 16)
            .Concat(new[] { MemberKind.String, MemberKind.Enum, MemberKind.Blank, MemberKind.Unmanaged }).ToArray();
        kinds[4] = MemberKind.UnmanagedNullable;
        var batches = SerializationPlanner.PlanScalarBatches(kinds);
        Assert.Equal(new ScalarBatch(15, true), batches[0]);
        Assert.Equal(default, batches[4]);
        Assert.Equal(new ScalarBatch(1, false), batches[15]);
        Assert.Equal(default, batches[16]);
        Assert.Equal(new ScalarBatch(1, false), batches[17]);
        Assert.Equal(default, batches[18]);
        Assert.Equal(new ScalarBatch(1, false), batches[19]);
    }

    [Fact]
    public void VersionedLengthStrategyUsesTheEffectiveWireSlots()
    {
        const string source = """
            using SharpPack;
            [SharpPackable(GenerateType.VersionTolerant)]
            public partial class InlineLengths
            {
                [SharpPackOrder(0)] public string? Text;
                [SharpPackOrder(2)] public int[]? Values;
            }
            [SharpPackable(GenerateType.VersionTolerant)]
            public partial class BufferedLengths
            {
                [SharpPackOrder(0)] public InlineLengths? Model;
            }
            """;
        var inline = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "InlineLengths"));
        var buffered = SerializationPlanner.Plan(TypeShapeTest.Analyze(source, "BufferedLengths"));
        Assert.Equal(ObjectWriteStrategy.VersionedInlineLengths, inline.ObjectWriteStrategy);
        Assert.Equal(new WireSlot(1, null), inline.WireSlots[1]);
        Assert.Equal(ObjectWriteStrategy.VersionedBufferedLengths, buffered.ObjectWriteStrategy);
    }

    [Fact]
    public void SequentialVersionedDuplicateUsesTheFirstDeclaredMember()
    {
        var shape = TypeShapeTest.Analyze(SparseModel("VersionTolerant", "Sequential", 0), "Sparse");
        Assert.Equal(2, shape.Members.Length);
        Assert.Equal("First", shape.Members[0].Name);
        Assert.Equal(new WireSlot(0, 0), Assert.Single(SerializationPlanner.Plan(shape).WireSlots));
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
    public void UnmanagedCircularReferenceKeepsItsRawWirePath()
    {
        const string source = """
            using SharpPack;
            [SharpPackable(GenerateType.CircularReference)]
            public partial struct RawModel
            {
                [SharpPackOrder(249)] public int Value;
            }
            """;
        var shape = TypeShapeTest.Analyze(source, "RawModel");
        Assert.True(shape.IsUnmanaged);
        Assert.Empty(SerializationPlanner.Plan(shape).WireSlots);
        var (output, diagnostics) = CSharpGeneratorRunner.RunGenerator(source);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith(".SharpPackFormatter.g.cs"));
        Assert.Contains("writer.WriteUnmanaged(value);", generated.ToString());
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
