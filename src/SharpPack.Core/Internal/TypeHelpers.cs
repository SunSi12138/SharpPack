using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpPack.Internal;

internal static class TypeHelpers
{
    static readonly MethodInfo isReferenceOrContainsReferences = typeof(RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!;
    static readonly MethodInfo unsafeSizeOf = typeof(Unsafe).GetMethod("SizeOf")!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsReferenceOrNullable<T>()
    {
        return Cache<T>.IsReferenceOrNullable;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypeKind TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(out int size)
    {
        if (Cache<T>.IsUnmanagedSZArray)
        {
            size = Cache<T>.UnmanagedSZArrayElementSize;
            return TypeKind.UnmanagedSZArray;
        }
        else
        {
            if (Cache<T>.IsFixedSizeSharpPackable)
            {
                size = Cache<T>.SharpPackableFixedSize;
                return TypeKind.FixedSizeSharpPackable;
            }
        }

        size = 0;
        return TypeKind.None;
    }

    public static bool IsAnonymous(Type type)
    {
        return type.Namespace == null
               && type.IsSealed
               && (type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal)
                   || type.Name.StartsWith("<>__AnonType", StringComparison.Ordinal)
                   || type.Name.StartsWith("VB$AnonymousType_", StringComparison.Ordinal))
               && type.IsDefined(typeof(CompilerGeneratedAttribute), false);
    }

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Used only by the cold reflection compatibility fallback; generated and explicitly registered formatter paths do not use it.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification = "Used only by the cold reflection compatibility fallback; NativeAOT callers must directly reference their closed formatter graph.")]
    public static bool IsReferenceOrContainsReferences(Type type)
    {
        return (bool)isReferenceOrContainsReferences.MakeGenericMethod(type).Invoke(null, null)!;
    }

    static class Cache<T>
    {
        public static bool IsReferenceOrNullable;
        public static bool IsUnmanagedSZArray;
        public static int UnmanagedSZArrayElementSize;
        public static bool IsFixedSizeSharpPackable = false;
        public static int SharpPackableFixedSize = 0;

        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050",
            Justification = "This optional array/fixed-size fast-path probe safely falls back to the regular generated formatter path when a reflected generic instantiation is unavailable.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2060",
            Justification = "This optional array fast-path probe safely falls back to the regular generated formatter path when the reflected generic method is unavailable.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2090",
            Justification = "The reflected explicit static Size property is only an optimization; if trimmed, serialization uses the regular generated formatter path.")]
        static Cache()
        {
            try
            {
                var type = typeof(T);
                IsReferenceOrNullable = !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

                if (type.IsSZArray)
                {
                    var elementType = type.GetElementType();
                    bool containsReference = (bool)(isReferenceOrContainsReferences.MakeGenericMethod(elementType!).Invoke(null, null)!);
                    if (!containsReference)
                    {
                        IsUnmanagedSZArray = true;
                        UnmanagedSZArrayElementSize = (int)unsafeSizeOf.MakeGenericMethod(elementType!).Invoke(null, null)!;
                    }
                }
                else
                {
                    if (typeof(IFixedSizeSharpPackable).IsAssignableFrom(type))
                    {
                        var prop = type.GetProperty("global::SharpPack.IFixedSizeSharpPackable.Size", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (prop != null)
                        {
                            IsFixedSizeSharpPackable = true;
                            SharpPackableFixedSize = (int)prop.GetValue(null)!;
                        }
                    }
                }
            }
            catch
            {
                IsUnmanagedSZArray = false;
                IsFixedSizeSharpPackable = false;
            }
        }
    }

    internal enum TypeKind : byte
    {
        None,
        UnmanagedSZArray,
        FixedSizeSharpPackable
    }
}
