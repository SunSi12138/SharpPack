using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;

namespace SharpPack.Tests;

public class ReaderResetRegressionTest
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static T Field<T>(SharpPackReaderOptionalState state, string name)
        => (T)typeof(SharpPackReaderOptionalState).GetField(name, PrivateInstance)!.GetValue(state)!;

    static object? Property(SharpPackReaderOptionalState state, string name)
        => typeof(SharpPackReaderOptionalState).GetProperty(name, PrivateInstance)!.GetValue(state);

    static SharpPackSerializerContext Context(int referenceLimit = 10000)
        => new SharpPackSerializerContextBuilder()
            .Configure(SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits
                {
                    MaxDepth = 7,
                    MaxCollectionLength = 17,
                    MaxReferenceCount = referenceLimit,
                },
            })
            .Register(new ResetBytesFormatter())
            .Build();

    static void Initialize(SharpPackReaderOptionalState state)
    {
        var context = Context();
        typeof(SharpPackReaderOptionalState).GetMethod("Init", PrivateInstance)!
            .Invoke(state, new object[] { context });
        Assert.Same(context, Property(state, "SerializerContext"));
        Assert.True(state.HasFormatterOverrides);
        Assert.Equal(7, (int)Property(state, "MaxDepth")!);
        Assert.Equal(17, (int)Property(state, "MaxCollectionLength")!);
    }

    static void AssertCleared(SharpPackReaderOptionalState state)
    {
        Assert.Null(Property(state, "SerializerContext"));
        Assert.Null(Property(state, "FormatterGraph"));
        Assert.False(state.HasFormatterOverrides);
        Assert.Equal(0, Field<int>(state, "referenceCount"));
        Assert.Equal(int.MaxValue, Field<int>(state, "maxReferenceCount"));
        Assert.Equal(999, (int)Property(state, "MaxDepth")!);
        Assert.Equal(int.MaxValue, (int)Property(state, "MaxCollectionLength")!);
        Assert.False(Field<bool>(state, "requiresReset"));
    }

    [Fact]
    public void Reset_WithNoReferenceTables_StillClearsContextAndLimits()
    {
        var state = new SharpPackReaderOptionalState();
        Initialize(state);
        Assert.Null(Field<List<object>?>(state, "sequentialReferences"));
        Assert.Null(Field<Dictionary<uint, object>?>(state, "sparseReferences"));
        state.Reset();
        AssertCleared(state);
        state.Reset();
        AssertCleared(state);
    }

    [Theory]
    [InlineData(4096, 0)]
    [InlineData(4097, 0)]
    [InlineData(0, 4096)]
    [InlineData(0, 4097)]
    [InlineData(4096, 4097)]
    [InlineData(4097, 4096)]
    public void Reset_HandlesEachReferenceTableAtItsRetentionBoundary(int denseCount, int sparseCount)
    {
        var state = new SharpPackReaderOptionalState();
        Initialize(state);
        var value = new object();
        for (uint i = 0; i < denseCount; ++i) state.AddObjectReference(i, value);
        for (uint i = 0; i < sparseCount; ++i) state.AddObjectReference(100000 + i, value);
        var dense = Field<List<object>?>(state, "sequentialReferences");
        var sparse = Field<Dictionary<uint, object>?>(state, "sparseReferences");
        state.Reset();
        AssertCleared(state);
        if (denseCount > 4096) Assert.Null(Field<List<object>?>(state, "sequentialReferences"));
        else
        {
            Assert.Same(dense, Field<List<object>?>(state, "sequentialReferences"));
            if (dense is not null) Assert.Empty(dense);
        }
        if (sparseCount > 4096) Assert.Null(Field<Dictionary<uint, object>?>(state, "sparseReferences"));
        else
        {
            Assert.Same(sparse, Field<Dictionary<uint, object>?>(state, "sparseReferences"));
            if (sparse is not null) Assert.Empty(sparse);
        }
        var retainedDense = Field<List<object>?>(state, "sequentialReferences");
        var retainedSparse = Field<Dictionary<uint, object>?>(state, "sparseReferences");
        state.Reset();
        AssertCleared(state);
        Assert.Same(retainedDense, Field<List<object>?>(state, "sequentialReferences"));
        Assert.Same(retainedSparse, Field<Dictionary<uint, object>?>(state, "sparseReferences"));
        state.AddObjectReference(0, value);
        state.AddObjectReference(uint.MaxValue, value);
        Assert.Same(value, state.GetObjectReference(0));
        Assert.Same(value, state.GetObjectReference(uint.MaxValue));
        state.Reset();
        AssertCleared(state);
    }

    [Fact]
    public void Reset_RetainsEmptyAllocatedTablesEvenWithLargeCapacity()
    {
        var state = new SharpPackReaderOptionalState();
        state.AddObjectReference(0, new object());
        state.AddObjectReference(uint.MaxValue, new object());
        var dense = Field<List<object>>(state, "sequentialReferences");
        var sparse = Field<Dictionary<uint, object>>(state, "sparseReferences");
        dense.EnsureCapacity(8192);
        sparse.EnsureCapacity(8192);
        state.Reset();
        Assert.Same(dense, Field<List<object>>(state, "sequentialReferences"));
        Assert.Same(sparse, Field<Dictionary<uint, object>>(state, "sparseReferences"));
        Assert.Empty(dense);
        Assert.Empty(sparse);
        Initialize(state);
        state.Reset();
        AssertCleared(state);
        Assert.Same(dense, Field<List<object>>(state, "sequentialReferences"));
        Assert.Same(sparse, Field<Dictionary<uint, object>>(state, "sparseReferences"));
    }

    [Fact]
    public void Reset_AfterLimitFailure_PreservesActiveLeaseAndAllowsReuse()
    {
        var lease = SharpPackReaderOptionalStatePool.Rent(Context(referenceLimit: 1));
        var state = lease.State;
        try
        {
            state.AddObjectReference(0, new object());
            var failure = Assert.Throws<SharpPackSerializationException>(
                () => state.AddObjectReference(uint.MaxValue, new object()));
            Assert.Equal(SharpPackReadLimitKind.ReferenceCount, failure.ReadLimitKind);
            lease.Reset();
            AssertCleared(state);
            Assert.Same(state, lease.State);
            state.AddObjectReference(0, new object());
            state.AddObjectReference(uint.MaxValue, new object());
            lease.Reset();
            AssertCleared(state);
            Assert.Same(state, lease.State);
            Assert.Throws<InvalidOperationException>(() => ((IDisposable)state).Dispose());
        }
        finally
        {
            lease.Dispose();
        }
        Assert.Throws<ObjectDisposedException>(() => { _ = lease.State; });
        Assert.Throws<InvalidOperationException>(() => lease.Dispose());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedLimitFailure_PreservesOuterState_AndNextReadWorks(bool failOuter)
    {
        var formatter = new NestedFailureFormatter { FailOuter = failOuter };
        var context = new SharpPackSerializerContextBuilder()
            .Configure(SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits
                {
                    MaxDepth = 7, MaxCollectionLength = 17, MaxReferenceCount = 4,
                },
            })
            .Register(formatter)
            .Build();
        var payload = SharpPackSerializer.Serialize(new ResetEnvelope { Value = 123 }, context);
        if (failOuter)
            Assert.Same(formatter.OuterFailure, Assert.Throws<InvalidOperationException>(
                () => SharpPackSerializer.Deserialize<ResetEnvelope>(payload, context)));
        else
            Assert.Equal(123, SharpPackSerializer.Deserialize<ResetEnvelope>(payload, context)!.Value);
        AssertCleared(formatter.OuterState!);
        formatter.FailOuter = false;
        Assert.Equal(123, SharpPackSerializer.Deserialize<ResetEnvelope>(payload, context)!.Value);
        AssertCleared(formatter.OuterState!);
        Assert.Equal(456, SharpPackSerializer.Deserialize<int>(SharpPackSerializer.Serialize(456)));
    }

    sealed class ResetEnvelope { public int Value; }

    sealed class NestedFailureFormatter : SharpPackFormatter<ResetEnvelope>
    {
        readonly byte[] innerPayload = SharpPackSerializer.Serialize(new byte[] { 1, 2 });
        readonly SharpPackSerializerContext innerContext = new(
            SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits { MaxCollectionLength = 1 },
            });
        public bool FailOuter;
        public SharpPackReaderOptionalState? OuterState;
        public readonly InvalidOperationException OuterFailure = new("Injected outer failure");

        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ResetEnvelope? value)
            => writer.WriteUnmanaged(value!.Value);

        public override void Deserialize(ref SharpPackReader reader, scoped ref ResetEnvelope? value)
        {
            OuterState = reader.OptionalState;
            var sentinel = new object();
            OuterState.AddObjectReference(0, sentinel);
            var failure = Assert.Throws<SharpPackSerializationException>(
                () => SharpPackSerializer.Deserialize<byte[]>(innerPayload, innerContext));
            Assert.Equal(SharpPackReadLimitKind.CollectionLength, failure.ReadLimitKind);
            Assert.Same(sentinel, OuterState.GetObjectReference(0));
            Assert.Equal(17, (int)Property(OuterState, "MaxCollectionLength")!);
            Assert.True(OuterState.HasFormatterOverrides);
            if (FailOuter) throw OuterFailure;
            reader.ReadUnmanaged(out int decoded);
            value = new ResetEnvelope { Value = decoded };
        }
    }

    sealed class ResetBytesFormatter : SharpPackFormatter<byte[]>
    {
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref byte[]? value)
            => writer.WriteUnmanagedArray(value);

        public override void Deserialize(ref SharpPackReader reader, scoped ref byte[]? value)
            => reader.ReadUnmanagedArray(ref value);
    }
}
