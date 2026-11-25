namespace SharpPack.Generator;

internal enum RootFastPath
{
    General,
    FixedSize,
    ExactSize,
}

internal readonly record struct WireSlot(int Position, int? DeclaredMemberIndex);

internal sealed record SerializationPlan(TypeShape Shape, RootFastPath RootFastPath, EquatableArray<WireSlot> WireSlots);

internal static class SerializationPlanner
{
    internal static SerializationPlan Plan(TypeShape shape)
        => new(shape, ChooseRootFastPath(shape), PlanWireSlots(shape));

    static RootFastPath ChooseRootFastPath(TypeShape shape)
    {
        if (shape.GenerateType != GenerateType.Object || shape.IsUnion ||
            shape.IsValueType || shape.Callbacks.Length != 0)
        {
            return RootFastPath.General;
        }

        if (shape.Members.All(static member => HasFixedSize(member.Kind)))
            return RootFastPath.FixedSize;

        if (shape.Members.All(static member => member.HasExactSizeSafeAccessor &&
            (HasFixedSize(member.Kind) || member.Kind is MemberKind.String or MemberKind.UnmanagedArray)))
        {
            return RootFastPath.ExactSize;
        }

        return RootFastPath.General;
    }

    static EquatableArray<WireSlot> PlanWireSlots(TypeShape shape)
    {
        if (shape.IsUnion || shape.IsUnmanaged || shape.GenerateType == GenerateType.Collection)
            return default;

        var members = shape.Members;
        if (members.Length == 0) return default;
        if (shape.GenerateType is not (GenerateType.VersionTolerant or GenerateType.CircularReference))
        {
            return new EquatableArray<WireSlot>(
                Enumerable.Range(0, members.Length).Select(static index => new WireSlot(index, index)));
        }

        // Analysis has validated the order range and sorted the members.
        // Unoccupied slots are represented explicitly in the wire header.
        var slots = new WireSlot[members[members.Length - 1].Order + 1];
        for (var order = 0; order < slots.Length; order++)
            slots[order] = new WireSlot(order, null);

        for (var index = 0; index < members.Length; index++)
        {
            var order = members[index].Order;
            // Keep the legacy first match if sequential layout has duplicate
            // annotations; explicit layout rejects duplicates in analysis.
            if (slots[order].DeclaredMemberIndex is null)
                slots[order] = new WireSlot(order, index);
        }
        return new EquatableArray<WireSlot>(slots);
    }

    static bool HasFixedSize(MemberKind kind)
        => kind is MemberKind.Unmanaged or MemberKind.Enum or MemberKind.UnmanagedNullable;
}
