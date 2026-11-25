using System.ComponentModel;

namespace SharpPack.CompilerServices;

/// <summary>Runtime entry points for compiler-generated external formatter providers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SharpPackSidecarRegistration
{
    /// <summary>Sidecar-provider ABI, independent of the ordinary object generator ABI.</summary>
    public const int Version = 1;

    /// <summary>Installs a typed factory beneath any explicit application registration.</summary>
    public static void Register<T>(SharpPackSerializerContextBuilder builder, Func<SharpPackSerializerContext, SharpPackFormatter<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.RegisterGeneratedFactory(factory);
    }

    /// <summary>Supplies an existing built-in formatter with its ordinary context binding.</summary>
    public static void RegisterBuiltIn<T>(SharpPackSerializerContextBuilder builder, Func<SharpPackFormatter<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.RegisterGeneratedFactory(context => factory().BindContext(context.Graph));
    }
}
