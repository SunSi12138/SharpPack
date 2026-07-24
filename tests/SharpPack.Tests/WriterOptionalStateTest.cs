using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class WriterOptionalStateTest
{
    [Fact]
    public void AddReference()
    {
        var state = SharpPackWriterOptionalStatePool.Rent(null);

        var o0 = new object();
        var o1 = new object();
        var o2 = new object();

        var (exists, id) = state.GetOrAddReference(o0);
        exists.Should().BeFalse();
        id.Should().Be(0);

        (exists, id) = state.GetOrAddReference(o1);
        exists.Should().BeFalse();
        id.Should().Be(1);

        (exists, id) = state.GetOrAddReference(o0);
        exists.Should().BeTrue();
        id.Should().Be(0);

        (exists, id) = state.GetOrAddReference(o2);
        exists.Should().BeFalse();
        id.Should().Be(2);

        (exists, id) = state.GetOrAddReference(o1);
        exists.Should().BeTrue();
        id.Should().Be(1);

        (exists, id) = state.GetOrAddReference(o2);
        exists.Should().BeTrue();
        id.Should().Be(2);

        state.Reset();
    }

    [Fact]
    public void ReaderReferencesSupportSequentialSparseAndOutOfOrderIds()
    {
        var state = new SharpPackReaderOptionalState();
        var zero = new object();
        var one = new object();
        var two = new object();
        var large = new object();

        state.AddObjectReference(0, zero);
        state.AddObjectReference(2, two);
        state.AddObjectReference(1, one);
        state.AddObjectReference(uint.MaxValue, large);

        state.GetObjectReference(0).Should().BeSameAs(zero);
        state.GetObjectReference(1).Should().BeSameAs(one);
        state.GetObjectReference(2).Should().BeSameAs(two);
        state.GetObjectReference(uint.MaxValue).Should().BeSameAs(large);
    }

    [Fact]
    public void ReaderReferencesRejectDuplicateAndUnknownIds()
    {
        var state = new SharpPackReaderOptionalState();
        state.AddObjectReference(0, new object());
        state.AddObjectReference(2, new object());

        var duplicateSequential = () =>
            state.AddObjectReference(0, new object());
        var duplicateSparse = () =>
            state.AddObjectReference(2, new object());
        var unknown = () => state.GetObjectReference(1);

        duplicateSequential.Should()
            .Throw<SharpPackSerializationException>();
        duplicateSparse.Should()
            .Throw<SharpPackSerializationException>();
        unknown.Should()
            .Throw<SharpPackSerializationException>();
    }

}
