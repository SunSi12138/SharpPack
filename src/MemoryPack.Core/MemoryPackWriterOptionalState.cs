using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MemoryPack;

public static class MemoryPackWriterOptionalStatePool
{
    static readonly ConcurrentQueue<MemoryPackWriterOptionalState> queue = new();

    public static MemoryPackWriterOptionalState Rent(
        MemoryPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new MemoryPackWriterOptionalState();
        }

        if (context is null)
        {
            state.InitDefault();
        }
        else
        {
            state.Init(context);
        }
        return state;
    }

    internal static void Return(MemoryPackWriterOptionalState state)
    {
        state.Reset();
        queue.Enqueue(state);
    }
}

public sealed class MemoryPackWriterOptionalState : IDisposable
{
    const int MaxRetainedReferenceCount = 4096;

    internal static readonly MemoryPackWriterOptionalState NullState =
        new(MemoryPackSerializerConfiguration.Default);

    uint nextId;
    bool isInUse;
    Dictionary<object, uint>? objectToRef;

    public MemoryPackSerializerConfiguration Configuration { get; private set; }
    internal MemoryPackSerializerContext? SerializerContext { get; private set; }
    internal FormatterGraph? FormatterGraph { get; private set; }

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
        => MemoryPackWriterOptionalStatePool.Return(this);

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(object obj)
            => RuntimeHelpers.GetHashCode(obj);
    }
}
