using System.Collections.Generic;

namespace SharpPack.Tests.GeneratorDeclarations;

public class GeneratorDeclarationTest
{
    [Fact]
    public void NestedGenericUnionUsesItsContainingInterface()
    {
        UnionScope.IChoice<int> input = new Choice<int> { Value = 17 };
        var payload = SharpPackSerializer.Serialize(input);

        SharpPackSerializer.Deserialize<UnionScope.IChoice<int>>(payload).Should().BeOfType<Choice<int>>()
            .Which.Value.Should().Be(17);
    }

    [Fact]
    public void NestedCollectionKeepsItsContainingTypeParameter()
    {
        var input = new CollectionScope<string>.Items { "a", "b" };
        var payload = SharpPackSerializer.Serialize(input);

        SharpPackSerializer.Deserialize<CollectionScope<string>.Items>(payload).Should().Equal("a", "b");
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
    }
}

public partial interface UnionScope
{
    [SharpPackable, SharpPackUnion(11, typeof(Choice<>))]
    public partial interface IChoice<T>;
}

[SharpPackable]
public partial class Choice<T> : UnionScope.IChoice<T>
{
    public T? Value { get; set; }
}

public partial class CollectionScope<T> where T : class
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
