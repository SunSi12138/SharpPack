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
    public static bool RequiresFormatterAwareSerialization<T>()
        => FormatterAwareCache<T>.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUnmanagedRawCopyDisabled<T>()
        => RequiresFormatterAwareSerialization<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypeKind TryGetUnmanagedSZArrayElementSizeOrSharpPackableFixedSize<T>(
        out int size)
    {
        size = Cache<T>.ElementSize;
        return Cache<T>.Kind;
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

    internal static bool RequiresFormatterAwareSerialization(Type type)
    {
        if (typeof(ISharpPackUnmanagedRawCopyDisabled)
            .IsAssignableFrom(type))
        {
            return true;
        }

        if (typeof(ISharpPackConditionalFormatterAware)
            .IsAssignableFrom(type))
        {
            if (!RuntimeFeature.IsDynamicCodeSupported ||
                type.IsAbstract ||
                type.IsInterface)
            {
                return true;
            }

            try
            {
                return RuntimeHelpers.GetUninitializedObject(type) is
                        ISharpPackConditionalFormatterAware conditional
                    ? conditional.RequiresFormatterAwareSerialization
                    : true;
            }
            catch (MemberAccessException)
            {
                return true;
            }
        }

        if (type.IsArray && type.GetElementType() is { } element)
        {
            return RequiresFormatterAwareSerialization(element);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition != typeof(Nullable<>) &&
                definition != typeof(KeyValuePair<,>) &&
                definition != typeof(ValueTuple<>) &&
                definition != typeof(ValueTuple<,>) &&
                definition != typeof(ValueTuple<,,>) &&
                definition != typeof(ValueTuple<,,,>) &&
                definition != typeof(ValueTuple<,,,,>) &&
                definition != typeof(ValueTuple<,,,,,>) &&
                definition != typeof(ValueTuple<,,,,,,>) &&
                definition != typeof(ValueTuple<,,,,,,,>))
            {
                return false;
            }

            foreach (var argument in type.GetGenericArguments())
            {
                if (RequiresFormatterAwareSerialization(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsUnmanagedRawCopyDisabled(Type type)
        => RequiresFormatterAwareSerialization(type);

    static class Cache<T>
    {
        public static bool IsReferenceOrNullable;
        public static TypeKind Kind;
        public static int ElementSize;

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
                    if (!containsReference &&
                        !RequiresFormatterAwareSerialization(elementType!))
                    {
                        Kind = TypeKind.UnmanagedSZArray;
                        ElementSize = (int)unsafeSizeOf
                            .MakeGenericMethod(elementType!)
                            .Invoke(null, null)!;
                        return;
                    }
                }
                else if (typeof(IFixedSizeSharpPackable).IsAssignableFrom(type))
                {
                    var prop = type.GetProperty(
                        "global::SharpPack.IFixedSizeSharpPackable.Size",
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Static);
                    if (prop != null)
                    {
                        var fixedSize = (int)prop.GetValue(null)!;
                        if (fixedSize > 0)
                        {
                            Kind = TypeKind.FixedSizeSharpPackable;
                            ElementSize = fixedSize;
                            return;
                        }
                    }
                }

                if (typeof(ISharpPackExactSizeSerializable<T>)
                    .IsAssignableFrom(type))
                {
                    Kind = TypeKind.ExactSizeSharpPackable;
                }
            }
            catch
            {
                Kind = TypeKind.None;
                ElementSize = 0;
            }
        }
    }

    static class FormatterAwareCache<T>
    {
        public static readonly bool Value = Initialize();

        static bool Initialize()
        {
            try
            {
                var type = typeof(T);
                if (typeof(ISharpPackUnmanagedRawCopyDisabled)
                    .IsAssignableFrom(type))
                {
                    return true;
                }

                if (typeof(ISharpPackConditionalFormatterAware)
                    .IsAssignableFrom(type))
                {
                    if (type.IsValueType &&
                        default(T) is
                            ISharpPackConditionalFormatterAware valueTypePolicy)
                    {
                        return valueTypePolicy
                            .RequiresFormatterAwareSerialization;
                    }

                    if (RuntimeFeature.IsDynamicCodeSupported &&
                        RuntimeHelpers.GetUninitializedObject(type) is
                            ISharpPackConditionalFormatterAware conditional)
                    {
                        return conditional.RequiresFormatterAwareSerialization;
                    }

                    return true;
                }

                if (!type.IsArray && !type.IsGenericType)
                {
                    return false;
                }

                return TypeHelpers.RequiresFormatterAwareSerialization(type);
            }
            catch
            {
                // Never allow a failed cold-path classification to enable
                // raw-copy and bypass a formatter contract.
                return true;
            }
        }
    }

    internal enum TypeKind : byte
    {
        None,
        UnmanagedSZArray,
        FixedSizeSharpPackable,
        ExactSizeSharpPackable
    }
}
