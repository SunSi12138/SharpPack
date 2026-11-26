using System.Runtime.InteropServices;

namespace SharpPack.Internal;

internal static class CollectionsMarshalEx
{
    /// <summary>
    /// similar as AsSpan but modify size to create fixed-size span.
    /// </summary>
    public static Span<T?> CreateSpan<T>(List<T?> list, int length)
    {
        list.Clear();
        list.EnsureCapacity(length);
        CollectionsMarshal.SetCount(list, length);
        return CollectionsMarshal.AsSpan(list);
    }
}
