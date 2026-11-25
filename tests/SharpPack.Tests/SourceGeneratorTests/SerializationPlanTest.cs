using SharpPack.Generator;

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
}
