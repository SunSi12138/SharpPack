using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryPack;

public static class MemoryPackWriterOptionalStatePool
{
    static readonly ConcurrentQueue<MemoryPackWriterOptionalState> queue = new();

    public static MemoryPackWriterOptionalStateLease Rent(
        MemoryPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new MemoryPackWriterOptionalState();
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
        return new MemoryPackWriterOptionalStateLease(state, leaseId);
    }

    internal static void Return(
        MemoryPackWriterOptionalState state,
        long leaseId)
    {
        if (!state.TryDeactivateLease(leaseId))
        {
            throw new InvalidOperationException(
                "The writer state lease was already returned or belongs to another rental.");
        }
        state.Reset();
        queue.Enqueue(state);
    }
}

public readonly struct MemoryPackWriterOptionalStateLease : IDisposable
{
    readonly MemoryPackWriterOptionalState? state;
    readonly long leaseId;

    internal MemoryPackWriterOptionalStateLease(
        MemoryPackWriterOptionalState state,
        long leaseId)
    {
        this.state = state;
        this.leaseId = leaseId;
    }

    public MemoryPackWriterOptionalState State
        => state is not null && state.IsLeaseActive(leaseId)
            ? state
            : throw new ObjectDisposedException(
                nameof(MemoryPackWriterOptionalStateLease));

    public static implicit operator MemoryPackWriterOptionalState(
        MemoryPackWriterOptionalStateLease lease)
        => lease.State;

    public (bool existsReference, uint id) GetOrAddReference(object value)
        => State.GetOrAddReference(value);

    public void Reset()
        => State.Reset();

    public void Dispose()
    {
        if (state is null)
        {
            return;
        }
        MemoryPackWriterOptionalStatePool.Return(state, leaseId);
    }
}

public sealed class MemoryPackWriterOptionalState : IDisposable
{
    const int MaxRetainedReferenceCount = 4096;

    internal static readonly MemoryPackWriterOptionalState NullState =
        new(MemoryPackSerializerConfiguration.Default);

    uint nextId;
    bool isInUse;
    long leaseGeneration;
    int poolLeaseState;
    Dictionary<object, uint>? objectToRef;

    public MemoryPackSerializerConfiguration Configuration { get; private set; }
    internal MemoryPackSerializerContext? SerializerContext { get; private set; }
    internal FormatterGraph? FormatterGraph { get; private set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasFormatterOverrides => FormatterGraph is not null;

    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFormatterOverride<T>()
        => FormatterGraph is { } graph &&
           graph.HasExplicitRegistration<T>();

    internal MemoryPackWriterOptionalState()
    {
    }

    MemoryPackWriterOptionalState(
        MemoryPackSerializerConfiguration configuration)
    {
        Configuration = configuration;
        SerializerContext = null;
        FormatterGraph = null;
    }

    internal void InitDefault()
    {
        Configuration = MemoryPackSerializerConfiguration.Default;
        SerializerContext = null;
        FormatterGraph = null;
    }

    internal void Init(MemoryPackSerializerContext context)
    {
        Configuration = context.Configuration;
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
                "The writer state is already leased.");
        }
        return leaseId;
    }

    internal bool TryDeactivateLease(long leaseId)
        => Volatile.Read(ref leaseGeneration) == leaseId &&
           Interlocked.CompareExchange(ref poolLeaseState, 0, 1) == 1;

    internal bool IsLeaseActive(long leaseId)
        => Volatile.Read(ref leaseGeneration) == leaseId &&
           Volatile.Read(ref poolLeaseState) == 1;

    public void Reset()
    {
        if (objectToRef is { Count: > MaxRetainedReferenceCount })
        {
            objectToRef = null;
        }
        else
        {
            objectToRef?.Clear();
        }

        Configuration = default;
        SerializerContext = null;
        FormatterGraph = null;
        nextId = 0;
    }

    public (bool existsReference, uint id) GetOrAddReference(object value)
    {
        objectToRef ??= new Dictionary<object, uint>(
            ReferenceEqualityComparer.Instance);
        ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(
            objectToRef,
            value,
            out var exists);
        if (exists)
        {
            return (true, id);
        }

        id = nextId++;
        return (false, id);
    }

    void IDisposable.Dispose()
    {
        if (Volatile.Read(ref poolLeaseState) != 0)
        {
            throw new InvalidOperationException(
                "Dispose the writer state lease instead of its pooled state.");
        }
        Reset();
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(object obj)
            => RuntimeHelpers.GetHashCode(obj);
    }
}
