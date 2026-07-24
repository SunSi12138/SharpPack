using SharpPack.Internal;
using SharpPack.Compression;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class ResourceOwnershipTest
{
    [Fact]
    public void CopiedBrotliOwnersCannotReturnTheSameRentalTwice()
    {
        var compressor = new BrotliCompressor();
        var compressorCopy = compressor;
        compressor.Dispose();

        var activeCompressor = new BrotliCompressor();
        SharpPackSerializer.Serialize(ref activeCompressor, 42);

        Action disposeCompressorCopy = () => compressorCopy.Dispose();
        disposeCompressorCopy.Should().Throw<InvalidOperationException>();

        try
        {
            var compressed = activeCompressor.ToArray();
            using var activeDecompressor = new BrotliDecompressor();
            var decompressed = activeDecompressor.Decompress(compressed);
            SharpPackSerializer.Deserialize<int>(decompressed).Should().Be(42);
        }
        finally
        {
            activeCompressor.Dispose();
        }

        var source = new BrotliCompressor();
        SharpPackSerializer.Serialize(ref source, 42);
        var validCompressed = source.ToArray();
        source.Dispose();

        var decompressor = new BrotliDecompressor();
        _ = decompressor.Decompress(validCompressed);
        var decompressorCopy = decompressor;
        decompressor.Dispose();

        Action disposeDecompressorCopy = () => decompressorCopy.Dispose();
        disposeDecompressorCopy.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OptionalStatePoolRejectsDuplicateReturn()
    {
        var readerState = SharpPackReaderOptionalStatePool.Rent();
        ((IDisposable)readerState).Dispose();
        Action returnReaderTwice = () =>
            ((IDisposable)readerState).Dispose();
        Action useReturnedReader = () => _ = readerState.State;

        var writerState = SharpPackWriterOptionalStatePool.Rent();
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
        _ = SharpPackSerializer.Serialize(
            ref writer,
            new string('x', 200_000));

        var stream = new ThrowAfterFirstWriteStream();
        await Assert.ThrowsAsync<IOException>(
            async () => await writer.WriteToAndResetAsync(
                stream,
                CancellationToken.None));

        writer.TotalWritten.Should().Be(0);

        _ = SharpPackSerializer.Serialize(ref writer, "reused");
        var payload = writer.ToArrayAndReset();
        SharpPackSerializer.Deserialize<string>(payload).Should().Be("reused");
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
