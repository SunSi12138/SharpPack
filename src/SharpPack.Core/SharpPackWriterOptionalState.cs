using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPack;

public static class SharpPackWriterOptionalStatePool
{
    static readonly ConcurrentQueue<SharpPackWriterOptionalState> queue = new();

    public static SharpPackWriterOptionalStateLease Rent(
        SharpPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new SharpPackWriterOptionalState();
        }
        var leaseId = state.ActivateLease();

        // New and returned states are already in the default configuration.
        if (context is not null)
        {
            state.Init(context);
        }
        return new SharpPackWriterOptionalStateLease(state, leaseId);
    }

    internal static void Return(
        SharpPackWriterOptionalState state,
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

public readonly struct SharpPackWriterOptionalStateLease : IDisposable
{
    readonly SharpPackWriterOptionalState? state;
    readonly long leaseId;

    internal SharpPackWriterOptionalStateLease(
        SharpPackWriterOptionalState state,
        long leaseId)
    {
        this.state = state;
        this.leaseId = leaseId;
    }

    public SharpPackWriterOptionalState State
        => state is not null && state.IsLeaseActive(leaseId)
            ? state
            : throw new ObjectDisposedException(
                nameof(SharpPackWriterOptionalStateLease));

    public static implicit operator SharpPackWriterOptionalState(
        SharpPackWriterOptionalStateLease lease)
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
        SharpPackWriterOptionalStatePool.Return(state, leaseId);
    }
}

public sealed class SharpPackWriterOptionalState : IDisposable
{
    const int MaxRetainedReferenceCount = 4096;

    internal static readonly SharpPackWriterOptionalState NullState =
        new(SharpPackSerializerConfiguration.Default);

    uint nextId;
    bool isInUse;
    bool requiresReset;
    long leaseGeneration;
    int poolLeaseState;
    Dictionary<object, uint>? objectToRef;

    public SharpPackSerializerConfiguration Configuration { get; private set; }
    internal SharpPackSerializerContext? SerializerContext { get; private set; }
    internal FormatterGraph? FormatterGraph { get; private set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasFormatterOverrides => FormatterGraph is not null;

    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFormatterOverride<T>()
        => FormatterGraph is { } graph &&
           graph.HasFormatterOverride<T>();

    internal SharpPackWriterOptionalState()
    {
    }

    SharpPackWriterOptionalState(
        SharpPackSerializerConfiguration configuration)
    {
        Configuration = configuration;
        SerializerContext = null;
        FormatterGraph = null;
    }

    internal void Init(SharpPackSerializerContext context)
    {
        requiresReset = true;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ResetAndExit()
    {
        if (requiresReset)
        {
            Reset();
        }

        isInUse = false;
    }

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
        requiresReset = false;
    }

    public (bool existsReference, uint id) GetOrAddReference(object value)
    {
        requiresReset = true;
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
