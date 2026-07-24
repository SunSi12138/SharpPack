using MemoryPack.Internal;
using MemoryPack.Compression;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

public class ResourceOwnershipTest
{
    [Fact]
    public void CopiedBrotliOwnersCannotReturnTheSameRentalTwice()
    {
        var compressor = new BrotliCompressor();
        var compressorCopy = compressor;
        compressor.Dispose();

        Action disposeCompressorCopy = () => compressorCopy.Dispose();
        disposeCompressorCopy.Should().Throw<InvalidOperationException>();

        var source = new BrotliCompressor();
        byte[] compressed;
        try
        {
            MemoryPackSerializer.Serialize(ref source, 42);
            compressed = source.ToArray();
        }
        finally
        {
            source.Dispose();
        }

        var decompressor = new BrotliDecompressor();
        _ = decompressor.Decompress(compressed);
        var decompressorCopy = decompressor;
        decompressor.Dispose();

        Action disposeDecompressorCopy = () => decompressorCopy.Dispose();
        disposeDecompressorCopy.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OptionalStatePoolRejectsDuplicateReturn()
    {
        var readerState = MemoryPackReaderOptionalStatePool.Rent();
        ((IDisposable)readerState).Dispose();
        Action returnReaderTwice = () =>
            ((IDisposable)readerState).Dispose();
        Action useReturnedReader = () => _ = readerState.State;

        var writerState = MemoryPackWriterOptionalStatePool.Rent();
        ((IDisposable)writerState).Dispose();
        Action returnWriterTwice = () =>
            ((IDisposable)writerState).Dispose();
        Action useReturnedWriter = () => _ = writerState.State;

        returnReaderTwice.Should().Throw<InvalidOperationException>();
        returnWriterTwice.Should().Throw<InvalidOperationException>();
        useReturnedReader.Should().Throw<ObjectDisposedException>();
        useReturnedWriter.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void BufferWriterPoolRequiresTheOriginalLeaseToken()
    {
        var writer = ReusableLinkedArrayBufferWriterPool.Rent(
            out var writerLeaseId);
        ReusableLinkedArrayBufferWriterPool.Return(writer, writerLeaseId);
        Action returnWriterTwice = () =>
            ReusableLinkedArrayBufferWriterPool.Return(
                writer,
                writerLeaseId);

        returnWriterTwice.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AsyncWriterResetsAllSegmentsWhenStreamThrows()
    {
        var writer = new ReusableLinkedArrayBufferWriter(
            useFirstBuffer: true,
            pinned: false);
        _ = MemoryPackSerializer.Serialize(
            ref writer,
            new string('x', 200_000));

        var stream = new ThrowAfterFirstWriteStream();
        await Assert.ThrowsAsync<IOException>(
            async () => await writer.WriteToAndResetAsync(
                stream,
                CancellationToken.None));

        writer.TotalWritten.Should().Be(0);

        _ = MemoryPackSerializer.Serialize(ref writer, "reused");
        var payload = writer.ToArrayAndReset();
        MemoryPackSerializer.Deserialize<string>(payload).Should().Be("reused");
    }

    sealed class ThrowAfterFirstWriteStream : Stream
    {
        int writeCount;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref writeCount) > 1)
            {
                return ValueTask.FromException(new IOException("Injected failure."));
            }
            return ValueTask.CompletedTask;
        }
    }
}
