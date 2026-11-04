using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpPack.Formatters;

namespace SharpPack.Tests;

public class ReadLimitsBoundaryTest
{
    static SharpPackSerializerContext Context(int maxPayload = 0, int maxCollection = 2)
        => new(SharpPackSerializerConfiguration.Default with
        {
            MaxPayloadBytes = maxPayload,
            ReadLimits = new SharpPackReadLimits { MaxCollectionLength = maxCollection },
        });

    static bool ReadDangerous(byte[] header, SharpPackSerializerContext context)
    {
        using var state = SharpPackReaderOptionalStatePool.Rent(context);
        using var reader = new SharpPackReader(header, state);
        return reader.DangerousTryReadCollectionHeader(out _);
    }

    [Fact]
    public void DangerousHeader_EnforcesCardinality_AndMalformedIsNotLimitFailure()
    {
        var context = Context();
        Assert.True(ReadDangerous(BitConverter.GetBytes(2), context));
        var limit = Assert.Throws<SharpPackSerializationException>(
            () => ReadDangerous(BitConverter.GetBytes(3), context));
        Assert.Equal(SharpPackReadLimitKind.CollectionLength, limit.ReadLimitKind);
        var malformed = Assert.Throws<SharpPackSerializationException>(
            () => ReadDangerous(BitConverter.GetBytes(-2), context));
        Assert.Null(malformed.ReadLimitKind);
        Assert.False(ReadDangerous(BitConverter.GetBytes(-1), context));
        Assert.True(ReadDangerous(BitConverter.GetBytes(2), context));
    }

    [Fact]
    public void BrotliString_UsesStructuralLength_NotCollectionCardinality()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Configure(SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits { MaxCollectionLength = 1 },
            })
            .Register(new BrotliStringFormatter())
            .Build();
        const string value = "a string is not a collection budget";
        Assert.Equal(value, SharpPackSerializer.Deserialize<string>(
            SharpPackSerializer.Serialize(value, context), context));
    }

    [Fact]
    public void BitArray_LogicalCardinalityCannotBypassWordArrayLimit()
    {
        var payload = SharpPackSerializer.Serialize(new BitArray(3));
        var failure = Assert.Throws<SharpPackSerializationException>(
            () => SharpPackSerializer.Deserialize<BitArray>(payload, Context()));
        Assert.Equal(SharpPackReadLimitKind.CollectionLength, failure.ReadLimitKind);
        Assert.Equal(2, SharpPackSerializer.Deserialize<BitArray>(
            SharpPackSerializer.Serialize(new BitArray(2)), Context())!.Length);
    }

    [Fact]
    public void SparseReferenceLimit_PreservesDuplicateClassification_AndLeaseReset()
    {
        var context = new SharpPackSerializerContext(
            SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits { MaxReferenceCount = 1 },
            });
        var value = new object();
        using (var lease = SharpPackReaderOptionalStatePool.Rent(context))
        {
            lease.AddObjectReference(uint.MaxValue, value);
            Assert.Same(value, lease.GetObjectReference(uint.MaxValue));
            var duplicate = Assert.Throws<SharpPackSerializationException>(
                () => lease.AddObjectReference(uint.MaxValue, new object()));
            Assert.Null(duplicate.ReadLimitKind);
            var overflow = Assert.Throws<SharpPackSerializationException>(
                () => lease.AddObjectReference(uint.MaxValue - 1, new object()));
            Assert.Equal(SharpPackReadLimitKind.ReferenceCount, overflow.ReadLimitKind);
            Assert.Same(value, lease.GetObjectReference(uint.MaxValue));
        }
        using var next = SharpPackReaderOptionalStatePool.Rent(context);
        next.AddObjectReference(uint.MaxValue, value);
        Assert.Same(value, next.GetObjectReference(uint.MaxValue));
    }

    [Fact]
    public void SequentialAndSparseReferences_ShareCount_WithoutCountingDuplicates()
    {
        var context = new SharpPackSerializerContext(
            SharpPackSerializerConfiguration.Default with
            {
                ReadLimits = new SharpPackReadLimits { MaxReferenceCount = 2 },
            });
        using var lease = SharpPackReaderOptionalStatePool.Rent(context);
        lease.AddObjectReference(0, new object());
        var duplicate = Assert.Throws<SharpPackSerializationException>(
            () => lease.AddObjectReference(0, new object()));
        Assert.Null(duplicate.ReadLimitKind);
        lease.AddObjectReference(uint.MaxValue, new object());
        var sequentialOverflow = Assert.Throws<SharpPackSerializationException>(
            () => lease.AddObjectReference(1, new object()));
        Assert.Equal(SharpPackReadLimitKind.ReferenceCount, sequentialOverflow.ReadLimitKind);
        var sparseOverflow = Assert.Throws<SharpPackSerializationException>(
            () => lease.AddObjectReference(uint.MaxValue - 1, new object()));
        Assert.Equal(SharpPackReadLimitKind.ReferenceCount, sparseOverflow.ReadLimitKind);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task UnknownStream_ExactPayloadSucceeds_WithoutProbeOverflow(int maximum)
    {
        using var stream = new ObservedStream(SharpPackSerializer.Serialize(123));
        Assert.Equal(123, await SharpPackSerializer.DeserializeAsync<int>(stream, Context(maximum)));
        Assert.True(stream.LargestRequest <= Math.Min(65536L, (long)maximum + 1));
    }

    [Fact]
    public async Task UnknownStream_StopsAtBudgetPlusOne_AndNextReadWorks()
    {
        using var stream = new ObservedStream(new byte[100]);
        var failure = await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackSerializer.DeserializeAsync<int>(stream, Context(4)));
        Assert.Equal(SharpPackReadLimitKind.PayloadBytes, failure.ReadLimitKind);
        Assert.Equal(5, stream.BytesRead);
        Assert.Equal(5, stream.LargestRequest);
        using var valid = new ObservedStream(SharpPackSerializer.Serialize(456));
        Assert.Equal(456, await SharpPackSerializer.DeserializeAsync<int>(valid, Context(4)));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 65_536)]
    [InlineData(true, 65_536)]
    public async Task UnknownStream_ReadFailurePreservesFailure(bool cancel, int failAfter)
    {
        using var stream = new ObservedStream(new byte[100_000], failAfter: failAfter, cancel: cancel);
        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await SharpPackSerializer.DeserializeAsync<int[]>(stream, Context(100_000)));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(
                async () => await SharpPackSerializer.DeserializeAsync<int[]>(stream, Context(100_000)));
        }
        using var valid = new ObservedStream(SharpPackSerializer.Serialize(789));
        Assert.Equal(789, await SharpPackSerializer.DeserializeAsync<int>(valid, Context(4)));
    }

    [Fact]
    public async Task UnknownStream_LimitFailureAfterSegmentTransfer_IsClassified()
    {
        using var stream = new ObservedStream(new byte[70_000]);
        var failure = await Assert.ThrowsAsync<SharpPackSerializationException>(
            async () => await SharpPackSerializer.DeserializeAsync<int[]>(stream, Context(65_536)));
        Assert.Equal(SharpPackReadLimitKind.PayloadBytes, failure.ReadLimitKind);
        Assert.Equal(65_537, stream.BytesRead);
    }

    sealed class ObservedStream(byte[] bytes, int failAfter = int.MaxValue, bool cancel = false) : Stream
    {
        public int BytesRead { get; private set; }
        public int LargestRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LargestRequest = Math.Max(LargestRequest, buffer.Length);
            if (BytesRead >= failAfter)
            {
                if (cancel) throw new OperationCanceledException();
                throw new IOException("Expected read failure.");
            }
            var count = Math.Min(buffer.Length, Math.Min(bytes.Length - BytesRead, failAfter - BytesRead));
            bytes.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
