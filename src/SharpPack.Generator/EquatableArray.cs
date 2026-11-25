using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SharpPack.Generator;

public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    readonly ImmutableArray<T> array;
    ImmutableArray<T> Items => array.IsDefault ? ImmutableArray<T>.Empty : array;

    public EquatableArray() // for collection literal []
    {
        array = ImmutableArray<T>.Empty;
    }

    public EquatableArray(T[] array) : this((IEnumerable<T>)array)
    {
    }

    internal EquatableArray(IEnumerable<T> items)
    {
        array = items.ToImmutableArray();
    }

    public static implicit operator EquatableArray<T>(T[] array)
    {
        return new EquatableArray<T>(array);
    }

    public ref readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref array.ItemRef(index);
    }

    public int Length => Items.Length;

    public ReadOnlySpan<T> AsSpan()
    {
        return Items.AsSpan();
    }

    public ReadOnlySpan<T>.Enumerator GetEnumerator()
    {
        return AsSpan().GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return ((IEnumerable<T>)Items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)Items).GetEnumerator();
    }

    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsSpan())
        {
            hash = unchecked(31 * hash + EqualityComparer<T>.Default.GetHashCode(item));
        }
        return hash;
    }
}
