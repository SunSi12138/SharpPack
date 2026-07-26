using SharpPack.Formatters;
using SharpPack.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SharpPack;

internal static class FormatterSlot<T>
{
    internal static readonly SharpPackFormatter<T> Formatter =
        FormatterResolver<T>.CreateDefault();
}

internal static class ContextFormatterSlot<T>
{
    static readonly ConditionalWeakTable<FormatterGraph, Holder> cache = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGet(
        FormatterGraph graph,
        [NotNullWhen(true)] out SharpPackFormatter<T>? formatter)
    {
        if (cache.TryGetValue(graph, out var holder))
        {
            formatter = holder.Formatter;
            return true;
        }

        formatter = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static SharpPackFormatter<T> Get(FormatterGraph graph)
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

    internal static void Register(FormatterGraph graph, SharpPackFormatter<T> formatter)
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
        SharpPackFormatter<T> formatter,
        bool isExplicitRegistration)
    {
        internal SharpPackFormatter<T> Formatter { get; } = formatter;
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
    static readonly ConditionalWeakTable<SharpPackSerializerContext, object> registrations = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Ensure(SharpPackSerializerContext context)
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
    internal static SharpPackFormatter<T> CreateDefault()
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
    internal static SharpPackFormatter<T> Create(
        SharpPackSerializerContext? context)
    {
        var type = typeof(T);

        try
        {
            context?.AddType(type);

            if (type == typeof(Type))
            {
                return (SharpPackFormatter<T>)(object)new TypeFormatter();
            }

            if (context is not null &&
                typeof(ISharpPackContextFormatterFactory<T>).IsAssignableFrom(type))
            {
                var factory = FindFactoryMethod(
                    type,
                    typeof(SharpPackSerializerContext))!;
                return (SharpPackFormatter<T>)factory.Invoke(
                    null,
                    [context])!;
            }

            if (typeof(ISharpPackFormatterFactory<T>).IsAssignableFrom(type))
            {
                var factory = FindFactoryMethod(type, parameterType: null)!;
                return (SharpPackFormatter<T>)factory.Invoke(null, null)!;
            }

            if (TypeHelpers.IsAnonymous(type))
            {
                return new ErrorSharpPackFormatter<T>(
                    "Serialize anonymous type is not supported, use record or tuple instead.");
            }

            if (FormatterResolver.CreateWellKnownFormatter(type) is { } wellKnown)
            {
                var formatter = (SharpPackFormatter<T>)wellKnown;
                return context is null
                    ? formatter
                    : formatter.BindContext(context.Graph);
            }

            if (typeof(ISharpPackable<>).MakeGenericType(type).IsAssignableFrom(type))
            {
                return (SharpPackFormatter<T>)Activator.CreateInstance(
                    typeof(SharpPackableFormatter<>).MakeGenericType(type))!;
            }

            var containsReferences = TypeHelpers.IsReferenceOrContainsReferences(type);
            if (FormatterResolver.CreateGenericFormatter(
                    type,
                    containsReferences,
                    preferKnownGenericFormatter: context is not null) is { } generic)
            {
                var formatter = (SharpPackFormatter<T>)generic;
                return context is null
                    ? formatter
                    : formatter.BindContext(context.Graph);
            }

            return new ErrorSharpPackFormatter<T>();
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                throw new SharpPackSerializationException(
                    $"Failed to resolve a formatter for {type.FullName}.",
                    ex);
            }

            return new ErrorSharpPackFormatter<T>(ex);
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Generated formatter factory methods are statically preserved by their implemented factory interface.")]
    static MethodInfo? FindFactoryMethod(
        Type type,
        Type? parameterType)
    {
        var factoryInterface = parameterType is null
            ? typeof(ISharpPackFormatterFactory<T>)
            : typeof(ISharpPackContextFormatterFactory<T>);
        var genericDefinition = factoryInterface.GetGenericTypeDefinition();
        var metadataName = genericDefinition.Name;
        var aritySeparator = metadataName.IndexOf('`');
        var interfaceName = aritySeparator < 0
            ? metadataName
            : metadataName[..aritySeparator];
        var explicitNamePrefix =
            $"{genericDefinition.Namespace}.{interfaceName}<";
        var candidates = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic)
            .Where(method =>
                method.ReturnType == typeof(SharpPackFormatter<T>) &&
                (parameterType is null
                    ? method.GetParameters().Length == 0
                    : method.GetParameters() is
                        [{ ParameterType: var actual }] &&
                      actual == parameterType));

        return candidates.FirstOrDefault(method =>
                    (method.Name.StartsWith(
                         explicitNamePrefix,
                         StringComparison.Ordinal) ||
                     method.Name.StartsWith(
                         $"global::{explicitNamePrefix}",
                         StringComparison.Ordinal)) &&
                    method.Name.EndsWith(
                        ".CreateFormatter",
                        StringComparison.Ordinal))
               ?? candidates.FirstOrDefault(static method =>
                   method.IsPublic && method.Name == "CreateFormatter");
    }
}
