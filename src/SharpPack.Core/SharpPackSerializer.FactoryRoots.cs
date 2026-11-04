using System;

namespace SharpPack;

public static partial class SharpPackSerializer
{
    /// <summary>
    /// Preserves a handwritten model's default formatter factory for trimming
    /// and NativeAOT without executing the factory or registering a formatter.
    /// Call once during application startup for models not covered by generated roots.
    /// </summary>
    public static void PreserveFormatterFactory<T>()
        where T : ISharpPackFormatterFactory<T>
    {
        Func<SharpPackFormatter<T>> factory = T.CreateFormatter;
        GC.KeepAlive(factory.Method);
    }

    /// <summary>
    /// Preserves a handwritten model's context formatter factory for trimming
    /// and NativeAOT without executing the factory or retaining a context.
    /// This complements PreserveFormatterFactory for models implementing both interfaces.
    /// </summary>
    public static void PreserveContextFormatterFactory<T>()
        where T : ISharpPackContextFormatterFactory<T>
    {
        Func<SharpPackSerializerContext, SharpPackFormatter<T>> factory = T.CreateFormatter;
        GC.KeepAlive(factory.Method);
    }
}
