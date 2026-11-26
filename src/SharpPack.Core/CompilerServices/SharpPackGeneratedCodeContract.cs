using System.ComponentModel;

namespace SharpPack.CompilerServices;

/// <summary>
/// Identifies the runtime contract understood by the SharpPack source generator.
/// This is generated-code infrastructure, not an application configuration API.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SharpPackGeneratedCodeContract
{
    /// <summary>
    /// The current generated-code ABI. Package versions and wire-format versions
    /// are independent of this value. Existing 1.x runtime helpers remain available.
    /// </summary>
    public const int Version = 1;
}
