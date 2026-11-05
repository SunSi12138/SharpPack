using System.ComponentModel;

namespace SharpPack.CompilerServices;

/// <summary>Preservation marker used by generated serializer declarations.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PreserveAttribute : Attribute
{
}
