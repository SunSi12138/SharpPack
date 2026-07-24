using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpPack;

public static class SharpPackReaderOptionalStatePool
{
    static readonly ConcurrentQueue<SharpPackReaderOptionalState> queue = new();

    public static SharpPackReaderOptionalStateLease Rent(
        SharpPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new SharpPackReaderOptionalState();
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
        return new SharpPackReaderOptionalStateLease(state, leaseId);
    }

    internal static void Return(
        SharpPackReaderOptionalState state,
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

public readonly struct SharpPackReaderOptionalStateLease : IDisposable
{
    readonly SharpPackReaderOptionalState? state;
    readonly long leaseId;

    internal SharpPackReaderOptionalStateLease(
        SharpPackReaderOptionalState state,
        long leaseId)
    {
        this.state = state;
        this.leaseId = leaseId;
    }

    public SharpPackReaderOptionalState State
        => state is not null && state.IsLeaseActive(leaseId)
            ? state
            : throw new ObjectDisposedException(
                nameof(SharpPackReaderOptionalStateLease));

    public static implicit operator SharpPackReaderOptionalState(
        SharpPackReaderOptionalStateLease lease)
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
        SharpPackReaderOptionalStatePool.Return(state, leaseId);
    }
}

public sealed class SharpPackReaderOptionalState : IDisposable
{
    const int MaxRetainedReferenceCount = 4096;

    List<object>? sequentialReferences;
    Dictionary<uint, object>? sparseReferences;
    bool isInUse;
    long leaseGeneration;
    int poolLeaseState;

    internal SharpPackSerializerContext? SerializerContext { get; private set; }
    internal FormatterGraph? FormatterGraph { get; private set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasFormatterOverrides => FormatterGraph is not null;

    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFormatterOverride<T>()
        => FormatterGraph is { } graph &&
           graph.HasFormatterOverride<T>();

    internal void InitDefault()
    {
        SerializerContext = null;
        FormatterGraph = null;
    }

    internal void Init(SharpPackSerializerContext context)
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

        SharpPackSerializationException.ThrowMessage(
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
                SharpPackSerializationException.ThrowMessage(
                    "Object is already added, id:" + id);
            }

            sequentialReferences.Add(value);
            return;
        }

        if (id < (uint)sequentialReferences.Count ||
            !(sparseReferences ??= []).TryAdd(id, value))
        {
            SharpPackSerializationException.ThrowMessage(
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
