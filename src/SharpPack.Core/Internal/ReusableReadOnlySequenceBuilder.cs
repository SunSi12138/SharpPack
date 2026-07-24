using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SharpPack.Internal;

internal static class ReusableReadOnlySequenceBuilderPool
{
    static readonly ConcurrentQueue<ReusableReadOnlySequenceBuilder> queue = new();
    static readonly int maximumRetainedBuilders =
        Math.Max(4, Environment.ProcessorCount * 2);
    static int retainedBuilderCount;

    public static ReusableReadOnlySequenceBuilder Rent(out long leaseId)
    {
        if (queue.TryDequeue(out var builder))
        {
            Interlocked.Decrement(ref retainedBuilderCount);
            leaseId = builder.ActivateLease();
            return builder;
        }
        builder = new ReusableReadOnlySequenceBuilder();
        leaseId = builder.ActivateLease();
        return builder;
    }

    public static void Return(
        ReusableReadOnlySequenceBuilder builder,
        long leaseId)
    {
        if (!builder.TryDeactivateLease(leaseId))
        {
            throw new InvalidOperationException(
                "The sequence builder lease was already returned or belongs to another rental.");
        }
        builder.Reset();
        if (Interlocked.Increment(ref retainedBuilderCount) <=
            maximumRetainedBuilders)
        {
            queue.Enqueue(builder);
        }
        else
        {
            Interlocked.Decrement(ref retainedBuilderCount);
        }
    }
}

internal sealed class ReusableReadOnlySequenceBuilder
{
    const int MaximumRetainedSegments = 4096;

    Stack<Segment> segmentPool;
    List<Segment> list;
    long leaseGeneration;
    int leaseState;

    public ReusableReadOnlySequenceBuilder()
    {
        list = new();
        segmentPool = new Stack<Segment>();
        leaseGeneration = 0;
        leaseState = 0;
    }

    public void Add(ReadOnlyMemory<byte> buffer, bool returnToPool)
    {
        if (!segmentPool.TryPop(out var segment))
        {
            segment = new Segment();
        }

        segment.SetBuffer(buffer, returnToPool);
        list.Add(segment);
    }

    public bool TryGetSingleMemory(out ReadOnlyMemory<byte> memory)
    {
        if (list.Count == 1)
        {
            memory = list[0].Memory;
            return true;
        }
        memory = default;
        return false;
    }

    public ReadOnlySequence<byte> Build()
    {
        if (list.Count == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        if (list.Count == 1)
        {
            return new ReadOnlySequence<byte>(list[0].Memory);
        }

        long running = 0;
        var span = CollectionsMarshal.AsSpan(list);
        for (int i = 0; i < span.Length; i++)
        {
            var next = i < span.Length - 1 ? span[i + 1] : null;
            span[i].SetRunningIndexAndNext(running, next);
            running += span[i].Memory.Length;
        }
        var firstSegment = span[0];
        var lastSegment = span[span.Length - 1];
        return new ReadOnlySequence<byte>(firstSegment, 0, lastSegment, lastSegment.Memory.Length);
    }

    public void Reset()
    {
        var span = CollectionsMarshal.AsSpan(list);
        foreach (var item in span)
        {
            item.Reset();
            if (segmentPool.Count < MaximumRetainedSegments)
            {
                segmentPool.Push(item);
            }
        }
        if (list.Capacity > MaximumRetainedSegments)
        {
            list = new List<Segment>();
        }
        else
        {
            list.Clear();
        }
        if (segmentPool.Count > MaximumRetainedSegments)
        {
            segmentPool = new Stack<Segment>(MaximumRetainedSegments);
        }
    }

    internal long ActivateLease()
    {
        var leaseId = Interlocked.Increment(ref leaseGeneration);
        if (Interlocked.Exchange(ref leaseState, 1) != 0)
        {
            throw new InvalidOperationException(
                "The sequence builder is already leased.");
        }
        return leaseId;
    }

    internal bool TryDeactivateLease(long leaseId)
        => Volatile.Read(ref leaseGeneration) == leaseId &&
           Interlocked.CompareExchange(ref leaseState, 0, 1) == 1;

    class Segment : ReadOnlySequenceSegment<byte>
    {
        bool returnToPool;

        public Segment()
        {
            returnToPool = false;
        }

        public void SetBuffer(ReadOnlyMemory<byte> buffer, bool returnToPool)
        {
            Memory = buffer;
            this.returnToPool = returnToPool;
        }

        public void Reset()
        {
            if (returnToPool)
            {
                if (MemoryMarshal.TryGetArray(Memory, out var segment) && segment.Array != null)
                {
                    ArrayPool<byte>.Shared.Return(segment.Array, clearArray: false);
                }
            }
            Memory = default;
            RunningIndex = 0;
            Next = null;
        }

        public void SetRunningIndexAndNext(long runningIndex, Segment? nextSegment)
        {
            RunningIndex = runningIndex;
            Next = nextSegment;
        }
    }
}
