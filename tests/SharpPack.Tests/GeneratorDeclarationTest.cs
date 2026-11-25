using System.Collections.Generic;

namespace SharpPack.Tests.GeneratorDeclarations;

public class GeneratorDeclarationTest
{
    [Fact]
    public void NestedGenericUnionUsesItsContainingInterface()
    {
        UnionScope<string>.IChoice<int> input = new UnionScope<string>.Choice<int> { Label = "outer", Value = 17 };
        var payload = SharpPackSerializer.Serialize(input);

        var result = SharpPackSerializer.Deserialize<UnionScope<string>.IChoice<int>>(payload)
            .Should().BeOfType<UnionScope<string>.Choice<int>>().Which;
        result.Label.Should().Be("outer");
        result.Value.Should().Be(17);

        UnionScope<string>.IPlain plain = new UnionScope<string>.Plain { Label = "outer only" };
        SharpPackSerializer.Deserialize<UnionScope<string>.IPlain>(SharpPackSerializer.Serialize(plain))
            .Should().BeOfType<UnionScope<string>.Plain>().Which.Label.Should().Be("outer only");
    }

    [Fact]
    public void NestedCollectionKeepsItsContainingTypeParameter()
    {
        var input = new @class<string>.Items { "a", "b" };
        var payload = SharpPackSerializer.Serialize(input);

        SharpPackSerializer.Deserialize<@class<string>.Items>(payload).Should().Equal("a", "b");
    }

    [Fact]
    public void FrameworkNamesAndWriterTypeParametersDoNotShadowGeneratedCode()
    {
        var input = new ShadowingScope<int>.Payload { Value = 42, Data = [1, 2, 3] };
        input.Self = input;
        var context = new SharpPackSerializerContextBuilder().Register(new IntOffsetFormatter(19)).Build();
        var payload = SharpPackSerializer.Serialize(input, context);

        var result = SharpPackSerializer.Deserialize<ShadowingScope<int>.Payload>(payload, context);
        result!.Value.Should().Be(42);
        result.Data.Should().Equal(1, 2, 3);
        result.Self.Should().BeSameAs(result);

        var sameAsWriterParameter = new TBufferWriter { Value = 19 };
        SharpPackSerializer.Deserialize<TBufferWriter>(SharpPackSerializer.Serialize(sameAsWriterParameter))!
            .Value.Should().Be(19);
    }
}

public partial interface UnionScope<TOuter>
{
    [SharpPackable, SharpPackUnion(11, typeof(UnionScope<>.Choice<>))]
    public partial interface IChoice<T>;

    [SharpPackable, SharpPackUnion(3, typeof(UnionScope<>.Plain))]
    public partial interface IPlain;

    [SharpPackable]
    public partial class Choice<T> : IChoice<T>
    {
        public TOuter? Label { get; set; }
        public T? Value { get; set; }
    }

    [SharpPackable]
    public partial class Plain : IPlain
    {
        public TOuter? Label { get; set; }
    }
}

public partial class @class<T> where T : global::System.IComparable?
{
    [SharpPackable(GenerateType.Collection)]
    public partial class Items : List<T>;
}

public sealed class SharpPackReader;
public sealed class SharpPackWriter<T>;
public sealed class SharpPackFormatter<T>;
public interface ISharpPackable<T>;
public sealed class SharpPackSerializationException;
public sealed class SharpPackCode;
public sealed class Span<T>;

[SharpPackable]
public partial class TBufferWriter
{
    public int Value { get; set; }
}

public partial class ShadowingScope<TBufferWriter>
{
    [SharpPackable(GenerateType.CircularReference)]
    public partial class Payload
    {
        [SharpPackOrder(0)]
        public TBufferWriter? Value { get; set; }

        [SharpPackOrder(1)]
        public byte[]? Data { get; set; }

        [SharpPackOrder(2)]
        public Payload? Self { get; set; }
    }
}
