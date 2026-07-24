using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MemoryPack;

public static class MemoryPackReaderOptionalStatePool
{
    static readonly ConcurrentQueue<MemoryPackReaderOptionalState> queue = new();

    public static MemoryPackReaderOptionalState Rent(
        MemoryPackSerializerContext? context = null)
    {
        if (!queue.TryDequeue(out var state))
        {
            state = new MemoryPackReaderOptionalState();
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

    internal static void Return(MemoryPackReaderOptionalState state)
    {
        state.Reset();
        queue.Enqueue(state);
    }
}

public sealed class MemoryPackReaderOptionalState : IDisposable
{
    Dictionary<uint, object>? refToObject;
    bool isInUse;

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

    public object GetObjectReference(uint id)
    {
        if (refToObject is not null &&
            refToObject.TryGetValue(id, out var value))
        {
            return value;
        }

        MemoryPackSerializationException.ThrowMessage(
            "Object is not found in this reference id:" + id);
        return null!;
    }

    public void AddObjectReference(uint id, object value)
    {
        refToObject ??= [];
        if (!refToObject.TryAdd(id, value))
        {
            MemoryPackSerializationException.ThrowMessage(
                "Object is already added, id:" + id);
        }
    }

    public void Reset()
    {
        refToObject?.Clear();
        SerializerContext = null;
        FormatterGraph = null;
    }

    void IDisposable.Dispose()
        => MemoryPackReaderOptionalStatePool.Return(this);
}
