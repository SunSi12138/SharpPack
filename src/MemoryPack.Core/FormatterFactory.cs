using MemoryPack.Formatters;
using MemoryPack.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace MemoryPack;

internal static class FormatterSlot<T>
{
    internal static readonly MemoryPackFormatter<T> Formatter =
        FormatterResolver<T>.CreateDefault();
}

internal static class ContextFormatterSlot<T>
{
    static readonly ConditionalWeakTable<FormatterGraph, Holder> cache = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static MemoryPackFormatter<T> Get(FormatterGraph graph)
    {
        if (cache.TryGetValue(graph, out var holder))
        {
            return holder.Formatter;
        }

        return cache.GetValue(
            graph,
            static graph => new Holder(
                FormatterResolver<T>.Create(graph.Owner),
                isExplicitRegistration: false)).Formatter;
    }

    internal static void Register(FormatterGraph graph, MemoryPackFormatter<T> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!cache.TryAdd(
                graph,
                new Holder(formatter, isExplicitRegistration: true)))
        {
            throw new InvalidOperationException(
                $"A formatter for {typeof(T).FullName} is already registered or resolved.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasExplicitRegistration(FormatterGraph graph)
        => cache.TryGetValue(graph, out var holder) &&
           holder.IsExplicitRegistration;

    sealed class Holder(
        MemoryPackFormatter<T> formatter,
        bool isExplicitRegistration)
    {
        internal MemoryPackFormatter<T> Formatter { get; } = formatter;
        internal bool IsExplicitRegistration { get; } = isExplicitRegistration;
    }
}

internal static class FormatterTypeTraits<T>
{
    internal static readonly bool ContainsCollectibleType =
        ContainsCollectibleTypeCore(typeof(T));

    static bool ContainsCollectibleTypeCore(Type type)
    {
        if (AssemblyLoadContext.GetLoadContext(type.Assembly)?.IsCollectible == true)
        {
            return true;
        }

        if (type.HasElementType &&
            type.GetElementType() is { } element &&
            ContainsCollectibleTypeCore(element))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (ContainsCollectibleTypeCore(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

internal static class ContextRootTypeRegistration<T>
{
    static readonly ConditionalWeakTable<MemoryPackSerializerContext, object> registrations = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Ensure(MemoryPackSerializerContext context)
    {
        if (!registrations.TryGetValue(context, out _))
        {
            _ = registrations.GetValue(
                context,
                static context =>
                {
                    context.AddType(typeof(T));
                    return new object();
                });
        }
    }
}

internal static class FormatterResolver<T>
{
    internal static MemoryPackFormatter<T> CreateDefault()
        => Create(context: null);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2055",
        Justification = "Reflection fallback is a cold-path compatibility mechanism for built-in generic shapes.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Generated formatter factories preserve their static factory method.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2090",
        Justification = "Generated formatter factories directly implement the statically referenced factory interface.")]
    internal static MemoryPackFormatter<T> Create(
        MemoryPackSerializerContext? context)
    {
        var type = typeof(T);

        try
        {
            context?.AddType(type);

            if (type == typeof(Type))
            {
                return (MemoryPackFormatter<T>)(object)new TypeFormatter();
            }

            if (typeof(IMemoryPackFormatterFactory<T>).IsAssignableFrom(type))
            {
                var factory = FindFactoryMethod(type)!;
                return (MemoryPackFormatter<T>)factory.Invoke(null, null)!;
            }

            if (TypeHelpers.IsAnonymous(type))
            {
                return new ErrorMemoryPackFormatter<T>(
                    "Serialize anonymous type is not supported, use record or tuple instead.");
            }

            if (FormatterResolver.CreateWellKnownFormatter(type) is { } wellKnown)
            {
                return (MemoryPackFormatter<T>)wellKnown;
            }

            if (typeof(IMemoryPackable<>).MakeGenericType(type).IsAssignableFrom(type))
            {
                return (MemoryPackFormatter<T>)Activator.CreateInstance(
                    typeof(MemoryPackableFormatter<>).MakeGenericType(type))!;
            }

            var containsReferences = TypeHelpers.IsReferenceOrContainsReferences(type);
            if (FormatterResolver.CreateGenericFormatter(type, containsReferences) is { } generic)
            {
                return (MemoryPackFormatter<T>)generic;
            }

            if (TryCreateExternalGeneratedFormatter(type) is { } external)
            {
                return external;
            }

            return new ErrorMemoryPackFormatter<T>();
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                throw new MemoryPackSerializationException(
                    $"Failed to resolve a formatter for {type.FullName}.",
                    ex);
            }

            return new ErrorMemoryPackFormatter<T>(ex);
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Cold compatibility fallback; generated external formatter registration extensions are the trimming-safe path.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2055",
        Justification = "Cold compatibility fallback; generated external formatter registration extensions are the trimming-safe path.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2065",
        Justification = "Cold compatibility fallback; generated external formatter registration extensions are the trimming-safe path.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Cold compatibility fallback; generated external formatter registration extensions are the NativeAOT path.")]
    static MemoryPackFormatter<T>? TryCreateExternalGeneratedFormatter(
        Type targetType)
    {
        Type?[] candidates;
        try
        {
            candidates = targetType.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            candidates = ex.Types;
        }

        var factoryInterface = typeof(IMemoryPackFormatterFactory<T>);
        foreach (var candidateDefinition in candidates)
        {
            if (candidateDefinition is null)
            {
                continue;
            }

            var candidate = candidateDefinition;
            if (candidate.ContainsGenericParameters)
            {
                if (!targetType.IsGenericType ||
                    candidate.GetGenericArguments().Length !=
                    targetType.GetGenericArguments().Length)
                {
                    continue;
                }

                try
                {
                    candidate = candidate.MakeGenericType(targetType.GetGenericArguments());
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            if (!factoryInterface.IsAssignableFrom(candidate))
            {
                continue;
            }

            var factory = candidate
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(static method =>
                    method.GetParameters().Length == 0 &&
                    (method.Name == "CreateFormatter" ||
                     method.Name.EndsWith(".CreateFormatter", StringComparison.Ordinal)));
            if (factory is not null)
            {
                return (MemoryPackFormatter<T>)factory.Invoke(null, null)!;
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Generated formatter factory methods are statically preserved by their implemented factory interface.")]
    static MethodInfo? FindFactoryMethod(Type type)
        => type
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
                method.GetParameters().Length == 0 &&
                (method.Name == "CreateFormatter" ||
                 method.Name.EndsWith(".CreateFormatter", StringComparison.Ordinal)) &&
                method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() ==
                    typeof(MemoryPackFormatter<>));
}
