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

internal sealed record ContextDependencySet(EquatableArray<string> FactoryLookupTypes)
{
    internal bool HasOverridableWireMembers => FactoryLookupTypes.Length != 0;
}

internal sealed record AotRootSet(
    EquatableArray<AotFactoryRoot> Factories,
    EquatableArray<string> ClosedFormatters);

internal sealed record GeneratedNames(
    string ContextFormatterType,
    string WriteWithOverrides,
    string ReadWithOverrides,
    string EnsureAotRoots,
    string? SelfFactoryRoot,
    string CircularReadTag,
    string CircularFormatter,
    string WriteCircular,
    string ReadCircular);

internal sealed record SerializationPlan(
    TypeShape Shape,
    RootFastPath RootFastPath,
    EquatableArray<WireSlot> WireSlots,
    EquatableArray<ScalarBatch> ScalarBatches,
    EquatableArray<int> ConstructorArgumentSlots,
    ObjectWriteStrategy ObjectWriteStrategy,
    ContextDependencySet ContextDependencies,
    AotRootSet AotRoots,
    GeneratedNames Names)
{
    internal bool IsVersioned => ObjectWriteStrategy != ObjectWriteStrategy.Plain;
    internal bool TracksReferences => IsVersioned && Shape.GenerateType == GenerateType.CircularReference;
}

internal static class SerializationPlanner
{
    const int MaximumScalarBatchArity = 15; // Core's unmanaged reader/writer overload family.

    internal static SerializationPlan Plan(TypeShape shape)
    {
        var slots = PlanWireSlots(shape);
        var kinds = slots.Select(slot => slot.DeclaredMemberIndex is int i ? shape.Members[i].Kind : MemberKind.Blank).ToArray();
        return new(shape, ChooseRootFastPath(shape), slots, PlanScalarBatches(kinds), PlanConstructorArguments(shape, slots),
            ChooseObjectWriteStrategy(shape, kinds), PlanContextDependencies(shape, slots),
            PlanAotRoots(shape.AotDependencies), PlanNames(shape.NameFacts));
    }

    static GeneratedNames PlanNames(GeneratedNameFacts facts)
        => new(
            AvailableMember("__SharpPackContextFormatter", facts.MembersAndParameters),
            AvailableMember("__SharpPackSerializeWithFormatterOverrides", facts.MembersAndParameters),
            AvailableMember("__SharpPackDeserializeWithFormatterOverrides", facts.MembersAndParameters),
            AvailableMember("__SharpPackEnsureAotFormatterRoots", facts.MembersAndParameters),
            facts.HasUserStaticInitialization
                ? null
                : AvailableFactoryName(GeneratedNameFacts.SelfRootStem, facts.SelfRootIdentifiers),
            AvailableMember(GeneratedNameFacts.CircularPrefix + "ReadTag", facts.CircularIdentifiers),
            AvailableMember(GeneratedNameFacts.CircularPrefix + "Formatter", facts.CircularIdentifiers),
            AvailableMember(GeneratedNameFacts.CircularPrefix + "Serialize", facts.CircularIdentifiers),
            AvailableMember(GeneratedNameFacts.CircularPrefix + "Deserialize", facts.CircularIdentifiers));

    static string AvailableMember(string stem, EquatableArray<string> names)
    {
        while (names.Contains(stem, StringComparer.Ordinal))
            stem += "_";
        return stem;
    }

    internal static string AvailableFactoryName(string stem, EquatableArray<string> names)
    {
        while (names.Contains(stem, StringComparer.Ordinal) || names.Contains(stem + "Value", StringComparer.Ordinal))
            stem += "_";
        return stem;
    }

    static AotRootSet PlanAotRoots(AotDependencyFacts dependencies)
    {
        var factories = new SortedDictionary<string, AotFactoryRoot>(StringComparer.Ordinal);
        foreach (var factory in dependencies.Factories)
            factories[factory.TypeName] = factory;
        return new(
            new EquatableArray<AotFactoryRoot>(factories.Values),
            new EquatableArray<string>(new SortedSet<string>(dependencies.ClosedFormatters, StringComparer.Ordinal)));
    }

    static ContextDependencySet PlanContextDependencies(TypeShape shape, EquatableArray<WireSlot> slots)
        => new(
            new EquatableArray<string>(slots.SelectMany(slot => slot.DeclaredMemberIndex is int index
                ? shape.Members[index].ContextDependencyTypes : default)
                .Distinct(StringComparer.Ordinal)));

    static RootFastPath ChooseRootFastPath(TypeShape shape)
    {
        if (shape.GenerateType != GenerateType.Object || shape.Union is not null ||
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

    internal static EquatableArray<WireSlot> PlanWireSlots(TypeShape shape)
    {
        if (shape.Union is not null || shape.IsUnmanaged || shape.Collection is not null)
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

    static EquatableArray<int> PlanConstructorArguments(TypeShape shape, EquatableArray<WireSlot> slots)
    {
        if (shape.ConstructorParameters.Length == 0 || slots.Length == 0) return default;

        var arguments = new List<int>(shape.ConstructorParameters.Length);
        foreach (var parameter in shape.ConstructorParameters)
        {
            var matches = Extensions.FindConstructorBindings(slots, GetBinding, parameter.Name);
            if (matches.Length == 1)
                arguments.Add(matches[0].Position);
        }
        return new(arguments);

        string? GetBinding(WireSlot slot) => slot.DeclaredMemberIndex is int index ? shape.Members[index].ConstructorParameter : null;
    }

    static ObjectWriteStrategy ChooseObjectWriteStrategy(TypeShape shape, MemberKind[] kinds)
    {
        if (shape.Union is not null || shape.IsUnmanaged || shape.GenerateType is not (GenerateType.VersionTolerant or GenerateType.CircularReference))
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
