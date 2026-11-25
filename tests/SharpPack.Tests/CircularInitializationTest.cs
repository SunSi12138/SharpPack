namespace SharpPack.Tests;

public partial class CircularInitializationTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitMembersAreHydratedWithoutLosingReferenceIdentity(bool useOverride)
    {
        var child = new InitNode { Label = "child" };
        var input = new InitNode { Label = "root", Children = [child, child] };
        input.Self = input;
        child.Self = input;
        var previous = new InitNode { Label = "previous" };
        InitNode? result = previous;

        var payload = Serialize(input, useOverride);
        Deserialize(payload, ref result, useOverride).Should().Be(payload.Length);

        result.Should().NotBeSameAs(previous);
        previous.Label.Should().Be("previous");
        result!.Label.Should().Be("root");
        result.InitWrites.Should().Be(1);
        result.Self.Should().BeSameAs(result);
        result.Children![0].Label.Should().Be("child");
        result.Children[0].Self.Should().BeSameAs(result);
        result.Children[0].Should().BeSameAs(result.Children[1]);
    }

    [Fact]
    public void CircularVersionsSkipUnknownSlotsAndKeepAbsentInitDefaults()
    {
        var input = new OlderNode();
        input.Self = input;
        var payload = SharpPackSerializer.Serialize(input);

        var result = SharpPackSerializer.Deserialize<InitNode>(payload)!;

        result.Label.Should().Be("constructor default");
        result.InitWrites.Should().Be(0);
        result.Self.Should().BeSameAs(result);

        var newerPayload = SharpPackSerializer.Serialize(result);
        OlderNode? older = null;
        SharpPackSerializer.Deserialize(newerPayload, ref older).Should().Be(newerPayload.Length);
        older!.Self.Should().BeSameAs(older);
    }

    [Fact]
    public void ConstructorDefaultsDoNotCollapseDistinctInitReferences()
    {
        var input = new Pair
        {
            Left = new RequiredOverride { Key = 1 },
            Right = new RequiredOverride { Key = 2 },
        };

        var result = SharpPackSerializer.Deserialize<Pair>(SharpPackSerializer.Serialize(input))!;

        result.Left.Should().NotBeSameAs(result.Right);
        result.Left.Key.Should().Be(1);
        result.Right.Key.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritableRequiredMembersAreSetOnNewAndReusedInstances(bool useOverride)
    {
        var input = new RequiredNode { Label = "updated", Number = 42 };
        input.Self = input;
        var payload = Serialize(input, useOverride);

        RequiredNode? created = null;
        Deserialize(payload, ref created, useOverride).Should().Be(payload.Length);
        created!.Label.Should().Be("updated");
        created.Number.Should().Be(42);
        created.Self.Should().BeSameAs(created);

        var previous = new RequiredNode { Label = "previous", Number = 1 };
        RequiredNode? reused = previous;
        Deserialize(payload, ref reused, useOverride).Should().Be(payload.Length);
        reused.Should().BeSameAs(previous);
        reused!.Label.Should().Be("updated");
        reused.Number.Should().Be(42);
        reused.Self.Should().BeSameAs(reused);
    }

    [Fact]
    public void RequiredInitWorksOnGenericRecordWithImplicitConstructor()
    {
        var input = new RequiredInitNode<string> { Label = "value" };
        input.Self = input;

        var result = SharpPackSerializer.Deserialize<RequiredInitNode<string>>(SharpPackSerializer.Serialize(input))!;

        result.Label.Should().Be("value");
        result.Self.Should().BeSameAs(result);
    }

    [Fact]
    public void RequiredOverrideUsesTheExistingInheritedSlot()
    {
        var input = new RequiredOverride { Key = 42 };
        var bytes = SharpPackSerializer.Serialize(input);

        SharpPackSerializer.Deserialize<RequiredOverride>(bytes)!.Key.Should().Be(42);
    }

    static readonly SharpPackSerializerContext OverrideContext = new SharpPackSerializerContextBuilder()
        .Register(new PrefixStringFormatter()).Build();

    static byte[] Serialize<T>(T value, bool useOverride)
        => useOverride ? SharpPackSerializer.Serialize(value, OverrideContext) : SharpPackSerializer.Serialize(value);

    static int Deserialize<T>(byte[] payload, ref T? value, bool useOverride)
        => useOverride ? SharpPackSerializer.Deserialize(payload, ref value, OverrideContext) : SharpPackSerializer.Deserialize(payload, ref value);

    [SharpPackable(GenerateType.CircularReference)]
    public partial class InitNode
    {
        string label;

        public InitNode() => label = "constructor default";

        [SharpPackOrder(0)]
        public InitNode? Self { get; set; }

        [SharpPackOrder(1), SharpPackInclude]
        public string Label
        {
            get => label;
            init { label = value; InitWrites++; }
        }

        [SharpPackOrder(2)]
        public InitNode[]? Children { get; init; }

        [SharpPackIgnore]
        public int InitWrites { get; private set; }
    }

    [SharpPackable(GenerateType.CircularReference)]
    public partial class OlderNode
    {
        [SharpPackOrder(0)]
        public OlderNode? Self { get; set; }
    }

    public class RequiredBase
    {
        public required string Label { get; set; } = "default";
    }

    [SharpPackable(GenerateType.CircularReference, SerializeLayout.Sequential)]
    public partial class RequiredNode : RequiredBase
    {
        public RequiredNode? Self { get; set; }
        public required int Number;
    }

    [SharpPackable(GenerateType.CircularReference, SerializeLayout.Sequential)]
    public partial record RequiredInitNode<T> where T : class
    {
        public required T Label { get; init; }
        public RequiredInitNode<T>? Self { get; set; }
    }

    public class OverrideBase
    {
        public virtual int Key { get; set; }
    }

    [SharpPackable(GenerateType.CircularReference, SerializeLayout.Sequential)]
    public partial class RequiredOverride : OverrideBase
    {
        public required override int Key { get; set; }
    }

    [SharpPackable(GenerateType.CircularReference, SerializeLayout.Sequential)]
    public partial class Pair
    {
        public Pair() => Left = Right = new RequiredOverride { Key = 0 };

        public RequiredOverride Left { get; init; }
        public RequiredOverride Right { get; init; }
    }
}
