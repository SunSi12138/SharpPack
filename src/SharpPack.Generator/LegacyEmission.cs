using Microsoft.CodeAnalysis;
using System.Text;

namespace SharpPack.Generator;

// Keep the public metadata helpers as adapters. Generator entry points use the
// immutable model and plan directly.
public partial class TypeMeta
{
    public void Emit(StringBuilder writer, IGeneratorContext context)
        => EmitAdapter(writer, TypeShape.Create(Symbol, this, reference));

    public void EmitUnionFormatterTemplate(StringBuilder writer, IGeneratorContext context, INamedTypeSymbol formatterSymbol)
        => EmitAdapter(writer, TypeShape.Create(formatterSymbol, this, reference, isExternalFormatter: true));

    static void EmitAdapter(StringBuilder writer, TypeShape shape)
        => new CSharpEmitter(SerializationPlanner.Plan(shape)).EmitTemplate(writer);

    public string EmitSerializeMembers(
        MemberMeta[] members, string indent, bool toTempWriter, bool writeObjectHeader, bool honorFormatterOverrides = false)
        => CSharpEmitter.EmitSerializeMembersCore(Snapshot(members),
            toTempWriter || honorFormatterOverrides ? default : PlanScalarBatches(members),
            indent, toTempWriter, writeObjectHeader, honorFormatterOverrides);

    public string EmitDeserializeMembers(MemberMeta[] members, string indent, bool honorFormatterOverrides)
    {
        var versioned = GenerateType is GenerateType.VersionTolerant or GenerateType.CircularReference;
        return CSharpEmitter.EmitDeserializeMembersCore(Snapshot(members),
            versioned || honorFormatterOverrides ? default : PlanScalarBatches(members),
            indent, versioned, honorFormatterOverrides);
    }

    static MemberShape[] Snapshot(MemberMeta[] members)
        => members.Select(static member => MemberShape.Create(member)).ToArray();

    static EquatableArray<ScalarBatch> PlanScalarBatches(MemberMeta[] members)
        => SerializationPlanner.PlanScalarBatches(members.Select(static member => member.Kind).ToArray());
}

public partial class MethodMeta
{
    public string Emit()
        => CSharpEmitter.EmitCallbackInvocation(Name, IsStatic, IsValueType, UseReaderArgument
            ? "ref reader, ref value" : UseWriterArgument ? "ref writer, ref value" : "");
}

public partial class MemberMeta
{
    public string EmitSerialize(string writer, bool honorFormatterOverrides = false)
        => MemberShape.Create(this).EmitSerialize(writer, honorFormatterOverrides);

    public string EmitVarIntLength() => MemberShape.Create(this).EmitVarIntLength();

    public string EmitReadToDeserialize(int i, bool requireDeltaCheck, bool honorFormatterOverrides = false)
        => MemberShape.Create(this).EmitReadToDeserialize(i, requireDeltaCheck, honorFormatterOverrides);

    public string EmitReadRefDeserialize(int i, bool requireDeltaCheck, bool honorFormatterOverrides = false)
        => MemberShape.Create(this).EmitReadRefDeserialize(i, requireDeltaCheck, honorFormatterOverrides);
}
