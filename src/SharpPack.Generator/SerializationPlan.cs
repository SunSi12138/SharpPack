namespace SharpPack.Generator;

internal enum RootFastPath
{
    General,
    FixedSize,
    ExactSize,
}

internal readonly record struct WireSlot(int Position, int? DeclaredMemberIndex);

internal readonly record struct ScalarBatch(int Count, bool UseDangerous);

internal enum ObjectWriteStrategy { Plain, VersionedInlineLengths, VersionedBufferedLengths }

internal sealed record ContextDependencySet(
    EquatableArray<string> FactoryLookupTypes,
    bool HasOverridableWireMembers);

internal sealed record SerializationPlan(
    TypeShape Shape,
    RootFastPath RootFastPath,
    EquatableArray<WireSlot> WireSlots,
    EquatableArray<ScalarBatch> ScalarBatches,
    ObjectWriteStrategy ObjectWriteStrategy,
    ContextDependencySet ContextDependencies);

internal static class SerializationPlanner
{
    const int MaximumScalarBatchArity = 15; // Core's unmanaged reader/writer overload family.

    internal static SerializationPlan Plan(TypeShape shape)
    {
        var slots = PlanWireSlots(shape);
        var kinds = slots.Select(slot => slot.DeclaredMemberIndex is int i ? shape.Members[i].Kind : MemberKind.Blank).ToArray();
        return new(shape, ChooseRootFastPath(shape), slots, PlanScalarBatches(kinds),
            ChooseObjectWriteStrategy(shape, kinds), PlanContextDependencies(shape, slots));
    }

    static ContextDependencySet PlanContextDependencies(TypeShape shape, EquatableArray<WireSlot> slots)
        => new(
            new EquatableArray<string>(shape.Members.SelectMany(static member => member.ContextDependencyTypes)
                .Distinct(StringComparer.Ordinal)),
            slots.Any(slot => slot.DeclaredMemberIndex is int index &&
                shape.Members[index].ContextDependencyTypes.Length != 0));

    static RootFastPath ChooseRootFastPath(TypeShape shape)
    {
        if (shape.GenerateType != GenerateType.Object || shape.IsUnion ||
            shape.IsValueType || shape.Callbacks.Length != 0)
        {
            return RootFastPath.General;
        }

        if (shape.Members.All(static member => IsFixedWidthScalar(member.Kind)))
            return RootFastPath.FixedSize;

        if (shape.Members.All(static member => member.HasExactSizeSafeAccessor &&
            (IsFixedWidthScalar(member.Kind) || member.Kind is MemberKind.String or MemberKind.UnmanagedArray)))
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

    static ObjectWriteStrategy ChooseObjectWriteStrategy(TypeShape shape, MemberKind[] kinds)
    {
        if (shape.IsUnmanaged || shape.GenerateType is not (GenerateType.VersionTolerant or GenerateType.CircularReference))
            return ObjectWriteStrategy.Plain;

        return kinds.All(static kind => IsFixedWidthScalar(kind) ||
                kind is MemberKind.String or MemberKind.UnmanagedArray or MemberKind.Blank)
            ? ObjectWriteStrategy.VersionedInlineLengths
            : ObjectWriteStrategy.VersionedBufferedLengths;
    }

    // Only a batch's first slot has a nonzero Count. A nullable scalar in the
    // batch selects the existing Dangerous unmanaged overload for the batch.
    internal static EquatableArray<ScalarBatch> PlanScalarBatches(MemberKind[] kinds)
    {
        if (kinds.Length == 0) return default;
        var starts = new ScalarBatch[kinds.Length];
        for (var start = 0; start < kinds.Length;)
        {
            if (!IsFixedWidthScalar(kinds[start]))
            {
                start++;
                continue;
            }

            var end = start;
            var limit = Math.Min(kinds.Length, start + MaximumScalarBatchArity);
            var dangerous = false;
            while (end < limit && IsFixedWidthScalar(kinds[end]))
            {
                dangerous |= kinds[end] == MemberKind.UnmanagedNullable;
                end++;
            }
            starts[start] = new ScalarBatch(end - start, dangerous);
            start = end;
        }
        return new EquatableArray<ScalarBatch>(starts);
    }

    static bool IsFixedWidthScalar(MemberKind kind)
        => kind is MemberKind.Unmanaged or MemberKind.Enum or MemberKind.UnmanagedNullable;
}
