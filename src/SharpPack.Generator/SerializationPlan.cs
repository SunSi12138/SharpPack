namespace SharpPack.Generator;

internal enum RootFastPath
{
    General,
    FixedSize,
    ExactSize,
}

internal sealed record SerializationPlan(TypeShape Shape, RootFastPath RootFastPath);

internal static class SerializationPlanner
{
    internal static SerializationPlan Plan(TypeShape shape)
    {
        if (shape.GenerateType != GenerateType.Object || shape.IsUnion ||
            shape.IsValueType || shape.Callbacks.Length != 0)
        {
            return new(shape, RootFastPath.General);
        }

        if (shape.Members.All(static member => HasFixedSize(member.Kind)))
            return new(shape, RootFastPath.FixedSize);

        if (shape.Members.All(static member => member.HasExactSizeSafeAccessor &&
            (HasFixedSize(member.Kind) || member.Kind is MemberKind.String or MemberKind.UnmanagedArray)))
        {
            return new(shape, RootFastPath.ExactSize);
        }

        return new(shape, RootFastPath.General);
    }

    static bool HasFixedSize(MemberKind kind)
        => kind is MemberKind.Unmanaged or MemberKind.Enum or MemberKind.UnmanagedNullable;
}
