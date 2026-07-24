using SharpPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class StreamSegmentGrowthBenchmark
{
    byte[] payload = null!;
    NonMemoryReadStream stream = null!;

    [Params(64 * 1024, 1024 * 1024, 64 * 1024 * 1024)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        payload = SharpPackSerializer.Serialize(new byte[PayloadSize]);
        stream = new NonMemoryReadStream(payload);
    }

    [Benchmark]
    public async ValueTask<byte[]?> Deserialize()
    {
        stream.Reset();
        return await SharpPackSerializer.DeserializeAsync<byte[]>(stream);
    }

    sealed class NonMemoryReadStream(byte[] source) : Stream
    {
        int position;

        public void Reset() => position = 0;

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, source.Length - position);
            source.AsSpan(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Read(buffer.Span));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
    }
}
