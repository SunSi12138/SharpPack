using SharpPack.Tests.UnionBinding.Contracts;
using SharpPack.Tests.UnionBinding.Formatters;

namespace SharpPack.Tests
{
    public class ExternalUnionDeclarationBindingTest
    {
        [Fact]
        public void TargetFactoryBindsOpenAndClosedFormattersInTheirDeclaringNamespace()
        {
            var previousCalls = OpenBindingFormatter<int, byte>.SerializeCalls;
            IBindingUnion<int, byte> integer = new BindingItem<int, byte> { Payload = 42, Code = 7 };
            var open = SharpPackSerializer.Serialize(integer);
            Assert.Equal((byte)3, open[0]);
            Assert.Equal(previousCalls + 1, OpenBindingFormatter<int, byte>.SerializeCalls);
            var openValue = Assert.IsType<BindingItem<int, byte>>(SharpPackSerializer.Deserialize<IBindingUnion<int, byte>>(open));
            Assert.Equal((42, (byte)7), (openValue.Payload, openValue.Code));

            IBindingUnion<string, int> text = new BindingItem<string, int> { Payload = "closed", Code = 88 };
            var closed = SharpPackSerializer.Serialize(text);
            Assert.Equal((byte)17, closed[0]);
            var closedValue = Assert.IsType<BindingItem<string, int>>(SharpPackSerializer.Deserialize<IBindingUnion<string, int>>(closed));
            Assert.Equal(("closed", 88), (closedValue.Payload, closedValue.Code));
        }

        [Fact]
        public void RegistrationStillLivesInTargetNamespaceAndUsesTheDeclaredFormatter()
        {
            var builder = new SharpPackSerializerContextBuilder();
            var context = OpenBindingFormatterSharpPackContextExtensions.RegisterOpenBindingFormatter<int, byte>(builder).Build();
            Assert.IsType<OpenBindingFormatter<int, byte>>(context.GetFormatter<IBindingUnion<int, byte>>());

            IBindingUnion<int, byte> value = new BindingItem<int, byte> { Payload = 84, Code = 21 };
            var bytes = SharpPackSerializer.Serialize(value, context);
            Assert.Equal((byte)3, bytes[0]);
            var decoded = Assert.IsType<BindingItem<int, byte>>(SharpPackSerializer.Deserialize<IBindingUnion<int, byte>>(bytes, context));
            Assert.Equal((84, (byte)21), (decoded.Payload, decoded.Code));
        }
    }
}

namespace SharpPack.Tests.UnionBinding.Contracts
{
    [SharpPackable(GenerateType.NoGenerate)] public partial interface IBindingUnion<TPayload, TCode>;

    [SharpPackable]
    public partial class BindingItem<TPayload, TCode> : IBindingUnion<TPayload, TCode>
    {
        public TPayload? Payload { get; set; }
        public TCode? Code { get; set; }
    }
}

namespace SharpPack.Tests.UnionBinding.Formatters
{
    // These unrelated names must not capture types used by the generated formatter.
    public sealed class SharpPackReader;
    public sealed class SharpPackWriter<T>;
    public sealed class SharpPackFormatter<T>;
    public sealed class System;

    [SharpPackUnionFormatter(typeof(IBindingUnion<,>))]
    [SharpPackUnion(3, typeof(BindingItem<,>))]
    public partial class OpenBindingFormatter<TBufferWriter, @class>
    {
        internal static int SerializeCalls { get; private set; }

        [SharpPackOnSerializing]
        static void CountSerialize() => SerializeCalls++;
    }

    [SharpPackUnionFormatter(typeof(IBindingUnion<string, int>))]
    [SharpPackUnion(17, typeof(BindingItem<string, int>))]
    public partial class ClosedBindingFormatter;
}
