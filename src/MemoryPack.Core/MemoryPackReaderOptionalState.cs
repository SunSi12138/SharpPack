using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MemoryPack;

public static class MemoryPackReaderOptionalStatePool
{
    static readonly ConcurrentQueue<MemoryPackReaderOptionalState> queue = new();

    public static MemoryPackReaderOptionalStateLease Rent(
        MemoryPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new MemoryPackReaderOptionalState();
        }
        var leaseId = state.ActivateLease();

        if (context is null)
        {
            state.InitDefault();
        }
        else
        {
            state.Init(context);
        }
        return new MemoryPackReaderOptionalStateLease(state, leaseId);
    }

    internal static void Return(
        MemoryPackReaderOptionalState state,
        long leaseId)
    {
        if (!state.TryDeactivateLease(leaseId))
        {
            throw new InvalidOperationException(
                "The reader state lease was already returned or belongs to another rental.");
        }
        state.Reset();
        queue.Enqueue(state);
    }
}

public readonly struct MemoryPackReaderOptionalStateLease : IDisposable
{
    readonly MemoryPackReaderOptionalState? state;
    readonly long leaseId;

    internal MemoryPackReaderOptionalStateLease(
        MemoryPackReaderOptionalState state,
        long leaseId)
    {
        this.state = state;
        this.leaseId = leaseId;
    }

    public MemoryPackReaderOptionalState State
        => state is not null && state.IsLeaseActive(leaseId)
            ? state
            : throw new ObjectDisposedException(
                nameof(MemoryPackReaderOptionalStateLease));

    public static implicit operator MemoryPackReaderOptionalState(
        MemoryPackReaderOptionalStateLease lease)
        => lease.State;

    public object GetObjectReference(uint id)
        => State.GetObjectReference(id);

    public void AddObjectReference(uint id, object value)
        => State.AddObjectReference(id, value);

    public void Reset()
        => State.Reset();

    public void Dispose()
    {
        if (state is null)
        {
            return;
        }
        MemoryPackReaderOptionalStatePool.Return(state, leaseId);
    }
}

public sealed class MemoryPackReaderOptionalState : IDisposable
{
    const int MaxRetainedReferenceCount = 4096;

    List<object>? sequentialReferences;
    Dictionary<uint, object>? sparseReferences;
    bool isInUse;
    long leaseGeneration;
    int poolLeaseState;

    internal MemoryPackSerializerContext? SerializerContext { get; private set; }
    internal FormatterGraph? FormatterGraph { get; private set; }

    internal void InitDefault()
    {
        SerializerContext = null;
        FormatterGraph = null;
    }

    internal void Init(MemoryPackSerializerContext context)
    {
        SerializerContext = context;
        FormatterGraph = context.OverrideGraph;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryEnter()
    {
        if (isInUse)
        {
            return false;
        }

        isInUse = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Exit()
        => isInUse = false;

    internal long ActivateLease()
    {
        var leaseId = Interlocked.Increment(ref leaseGeneration);
        if (Interlocked.Exchange(ref poolLeaseState, 1) != 0)
        {
            throw new InvalidOperationException(
                "The reader state is already leased.");
        }
        return leaseId;
    }

    internal bool TryDeactivateLease(long leaseId)
        => Volatile.Read(ref leaseGeneration) == leaseId &&
           Interlocked.CompareExchange(ref poolLeaseState, 0, 1) == 1;

    internal bool IsLeaseActive(long leaseId)
        => Volatile.Read(ref leaseGeneration) == leaseId &&
           Volatile.Read(ref poolLeaseState) == 1;

    public object GetObjectReference(uint id)
    {
        if (sequentialReferences is { } sequential &&
            id < (uint)sequential.Count)
        {
            return sequential[(int)id];
        }

        if (sparseReferences is not null &&
            sparseReferences.TryGetValue(id, out var value))
        {
            return value;
        }

        MemoryPackSerializationException.ThrowMessage(
            "Object is not found in this reference id:" + id);
        return null!;
    }

    public void AddObjectReference(uint id, object value)
    {
        sequentialReferences ??= [];
        if (id == (uint)sequentialReferences.Count)
        {
            if (sparseReferences?.ContainsKey(id) == true)
            {
                MemoryPackSerializationException.ThrowMessage(
                    "Object is already added, id:" + id);
            }

            sequentialReferences.Add(value);
            return;
        }

        if (id < (uint)sequentialReferences.Count ||
            !(sparseReferences ??= []).TryAdd(id, value))
        {
            MemoryPackSerializationException.ThrowMessage(
                "Object is already added, id:" + id);
        }
    }

    public void Reset()
    {
        if (sequentialReferences is { Count: > MaxRetainedReferenceCount })
        {
            sequentialReferences = null;
        }
        else
        {
            sequentialReferences?.Clear();
        }

        if (sparseReferences is { Count: > MaxRetainedReferenceCount })
        {
            sparseReferences = null;
        }
        else
        {
            sparseReferences?.Clear();
        }

        SerializerContext = null;
        FormatterGraph = null;
    }

    void IDisposable.Dispose()
    {
        if (Volatile.Read(ref poolLeaseState) != 0)
        {
            throw new InvalidOperationException(
                "Dispose the reader state lease instead of its pooled state.");
        }
        Reset();
    }
}
