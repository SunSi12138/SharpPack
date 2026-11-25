using SharpPack.Tests.UnionBinding.Contracts;
using SharpPack.Tests.UnionBinding.Formatters;

namespace SharpPack.Tests
{
    public class ExternalUnionDeclarationBindingTest
    {
        [Fact]
        public void TargetFactoryBindsOpenAndClosedFormattersInTheirDeclaringNamespace()
        {
            var previousCalls = OpenBindingFormatter<int>.SerializeCalls;
            IBindingUnion<int> integer = new BindingItem<int> { Payload = 42 };
            var open = SharpPackSerializer.Serialize(integer);
            Assert.Equal((byte)3, open[0]);
            Assert.Equal(previousCalls + 1, OpenBindingFormatter<int>.SerializeCalls);
            Assert.Equal(42, Assert.IsType<BindingItem<int>>(SharpPackSerializer.Deserialize<IBindingUnion<int>>(open)).Payload);

            IBindingUnion<string> text = new BindingItem<string> { Payload = "closed" };
            var closed = SharpPackSerializer.Serialize(text);
            Assert.Equal((byte)17, closed[0]);
            Assert.Equal("closed", Assert.IsType<BindingItem<string>>(SharpPackSerializer.Deserialize<IBindingUnion<string>>(closed)).Payload);
        }

        [Fact]
        public void RegistrationStillLivesInTargetNamespaceAndUsesTheDeclaredFormatter()
        {
            var builder = new SharpPackSerializerContextBuilder();
            var context = OpenBindingFormatterSharpPackContextExtensions.RegisterOpenBindingFormatter<int>(builder).Build();
            Assert.IsType<OpenBindingFormatter<int>>(context.GetFormatter<IBindingUnion<int>>());

            IBindingUnion<int> value = new BindingItem<int> { Payload = 84 };
            var bytes = SharpPackSerializer.Serialize(value, context);
            Assert.Equal((byte)3, bytes[0]);
            Assert.Equal(84, Assert.IsType<BindingItem<int>>(SharpPackSerializer.Deserialize<IBindingUnion<int>>(bytes, context)).Payload);
        }
    }
}

namespace SharpPack.Tests.UnionBinding.Contracts
{
    [SharpPackable(GenerateType.NoGenerate)] public partial interface IBindingUnion<T>;

    [SharpPackable]
    public partial class BindingItem<T> : IBindingUnion<T>
    {
        public T? Payload { get; set; }
    }
}

namespace SharpPack.Tests.UnionBinding.Formatters
{
    [SharpPackUnionFormatter(typeof(IBindingUnion<>))]
    [SharpPackUnion(3, typeof(BindingItem<>))]
    public partial class OpenBindingFormatter<TValue>
    {
        internal static int SerializeCalls { get; private set; }

        [SharpPackOnSerializing]
        static void CountSerialize() => SerializeCalls++;
    }

    [SharpPackUnionFormatter(typeof(IBindingUnion<string>))]
    [SharpPackUnion(17, typeof(BindingItem<string>))]
    public partial class ClosedBindingFormatter;
}
