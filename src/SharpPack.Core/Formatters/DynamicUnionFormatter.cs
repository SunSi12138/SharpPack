using System.Buffers;
using System.Collections.Frozen;

namespace SharpPack.Formatters;

/// <summary>
/// Builds a context-owned union formatter without using the non-generic serializer path.
/// </summary>
public sealed class DynamicUnionFormatterBuilder<TBase>
    where TBase : class
{
    readonly Dictionary<Type, DynamicUnionCase<TBase>> typeToCase = [];
    readonly Dictionary<ushort, DynamicUnionCase<TBase>> tagToCase = [];
    bool built;

    public DynamicUnionFormatterBuilder<TBase> Add<TDerived>(ushort tag)
        where TDerived : TBase
    {
        if (built)
        {
            throw new InvalidOperationException("This dynamic union builder has already been built.");
        }

        var type = typeof(TDerived);
        if (typeToCase.ContainsKey(type))
        {
            throw new ArgumentException(
                $"The derived type {type.FullName} is already registered.",
                nameof(TDerived));
        }

        if (tagToCase.ContainsKey(tag))
        {
            throw new ArgumentException(
                $"The union tag {tag} is already registered.",
                nameof(tag));
        }

        var unionCase = new DynamicUnionCase<TBase, TDerived>(tag);
        typeToCase.Add(type, unionCase);
        tagToCase.Add(tag, unionCase);
        return this;
    }

    public DynamicUnionFormatter<TBase> Build()
    {
        if (built)
        {
            throw new InvalidOperationException("This dynamic union builder has already been built.");
        }

        built = true;
        return new DynamicUnionFormatter<TBase>(
            typeToCase.ToFrozenDictionary(),
            tagToCase.ToFrozenDictionary());
    }
}

public sealed class DynamicUnionFormatter<TBase> : SharpPackFormatter<TBase>
    where TBase : class
{
    readonly FrozenDictionary<Type, DynamicUnionCase<TBase>> typeToCase;
    readonly FrozenDictionary<ushort, DynamicUnionCase<TBase>> tagToCase;

    internal DynamicUnionFormatter(
        FrozenDictionary<Type, DynamicUnionCase<TBase>> typeToCase,
        FrozenDictionary<ushort, DynamicUnionCase<TBase>> tagToCase)
    {
        this.typeToCase = typeToCase;
        this.tagToCase = tagToCase;
    }

    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref TBase? value)
    {
        if (value is null)
        {
            writer.WriteNullUnionHeader();
            return;
        }

        var actualType = value.GetType();
        if (!typeToCase.TryGetValue(actualType, out var unionCase))
        {
            SharpPackSerializationException.ThrowNotFoundInUnionType(
                actualType,
                typeof(TBase));
        }

        writer.WriteUnionHeader(unionCase.Tag);
        unionCase.Serialize(ref writer, value);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref TBase? value)
    {
        if (!reader.TryReadUnionHeader(out var tag))
        {
            value = null;
            return;
        }

        if (!tagToCase.TryGetValue(tag, out var unionCase))
        {
            SharpPackSerializationException.ThrowInvalidTag(tag, typeof(TBase));
        }

        value = unionCase.Deserialize(ref reader, value);
    }
}

internal abstract class DynamicUnionCase<TBase>(ushort tag)
    where TBase : class
{
    internal ushort Tag { get; } = tag;

    internal abstract void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        TBase value)
        where TBufferWriter : IBufferWriter<byte>;

    internal abstract TBase? Deserialize(
        ref SharpPackReader reader,
        TBase? value);
}

internal sealed class DynamicUnionCase<TBase, TDerived>(ushort tag)
    : DynamicUnionCase<TBase>(tag)
    where TBase : class
    where TDerived : TBase
{
    internal override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        TBase value)
    {
        var typedValue = (TDerived)value;
        writer.WriteValue(typedValue);
    }

    internal override TBase? Deserialize(
        ref SharpPackReader reader,
        TBase? value)
    {
        var typedValue = value is TDerived existing ? existing : default;
        reader.ReadValue(ref typedValue);
        return typedValue;
    }
}
