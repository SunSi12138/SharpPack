using SharpPack.Internal;

namespace SharpPack.Formatters;

[Preserve]
public sealed class TupleFormatter<T1> : SharpPackFormatter<Tuple<T1?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(1);
        writer.WriteValue(value.Item1);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 1) SharpPackSerializationException.ThrowInvalidPropertyCount(1, count);

        value = new Tuple<T1?>(
            reader.ReadValue<T1>()
        );
    }
}
[Preserve]
public sealed class TupleFormatter<T1, T2> : SharpPackFormatter<Tuple<T1?, T2?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(2);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 2) SharpPackSerializationException.ThrowInvalidPropertyCount(2, count);

        value = new Tuple<T1?, T2?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3> : SharpPackFormatter<Tuple<T1?, T2?, T3?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(3);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 3) SharpPackSerializationException.ThrowInvalidPropertyCount(3, count);

        value = new Tuple<T1?, T2?, T3?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3, T4> : SharpPackFormatter<Tuple<T1?, T2?, T3?, T4?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?, T4?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(4);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?, T4?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 4) SharpPackSerializationException.ThrowInvalidPropertyCount(4, count);

        value = new Tuple<T1?, T2?, T3?, T4?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3, T4, T5> : SharpPackFormatter<Tuple<T1?, T2?, T3?, T4?, T5?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(5);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 5) SharpPackSerializationException.ThrowInvalidPropertyCount(5, count);

        value = new Tuple<T1?, T2?, T3?, T4?, T5?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3, T4, T5, T6> : SharpPackFormatter<Tuple<T1?, T2?, T3?, T4?, T5?, T6?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(6);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 6) SharpPackSerializationException.ThrowInvalidPropertyCount(6, count);

        value = new Tuple<T1?, T2?, T3?, T4?, T5?, T6?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3, T4, T5, T6, T7> : SharpPackFormatter<Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>>
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(7);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 7) SharpPackSerializationException.ThrowInvalidPropertyCount(7, count);

        value = new Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>(),
            reader.ReadValue<T7>()
        );
    }
}

[Preserve]
public sealed class TupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest> : SharpPackFormatter<Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>>
    where TRest : notnull
{
    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(8);
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
        writer.WriteValue(value.Rest);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>? value)
    {
        if (!reader.TryReadObjectHeader(out var count))
        {
            value = null;
            return;
        }

        if (count != 8) SharpPackSerializationException.ThrowInvalidPropertyCount(8, count);

        value = new Tuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>(),
            reader.ReadValue<T7>(),
            reader.ReadValue<TRest>()!
        );
    }
}


[Preserve]
public sealed class ValueTupleFormatter<T1> : SharpPackFormatter<ValueTuple<T1?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?>>() &&
            !writer.OptionalState.HasFormatterOverride<T1>())
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?>>() &&
            !reader.OptionalState.HasFormatterOverride<T1>())
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?>(
            reader.ReadValue<T1>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2> : SharpPackFormatter<ValueTuple<T1?, T2?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3, T4> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?, T4?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>() ||
           graph.HasFormatterOverride<T4>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?, T4?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>() ||
              writer.OptionalState.HasFormatterOverride<T4>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?, T4?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>() ||
              reader.OptionalState.HasFormatterOverride<T4>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?, T4?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3, T4, T5> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?, T4?, T5?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>() ||
           graph.HasFormatterOverride<T4>() ||
           graph.HasFormatterOverride<T5>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>() ||
              writer.OptionalState.HasFormatterOverride<T4>() ||
              writer.OptionalState.HasFormatterOverride<T5>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>() ||
              reader.OptionalState.HasFormatterOverride<T4>() ||
              reader.OptionalState.HasFormatterOverride<T5>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?, T4?, T5?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>() ||
           graph.HasFormatterOverride<T4>() ||
           graph.HasFormatterOverride<T5>() ||
           graph.HasFormatterOverride<T6>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>() ||
              writer.OptionalState.HasFormatterOverride<T4>() ||
              writer.OptionalState.HasFormatterOverride<T5>() ||
              writer.OptionalState.HasFormatterOverride<T6>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>() ||
              reader.OptionalState.HasFormatterOverride<T4>() ||
              reader.OptionalState.HasFormatterOverride<T5>() ||
              reader.OptionalState.HasFormatterOverride<T6>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>>
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>() ||
           graph.HasFormatterOverride<T4>() ||
           graph.HasFormatterOverride<T5>() ||
           graph.HasFormatterOverride<T6>() ||
           graph.HasFormatterOverride<T7>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>() ||
              writer.OptionalState.HasFormatterOverride<T4>() ||
              writer.OptionalState.HasFormatterOverride<T5>() ||
              writer.OptionalState.HasFormatterOverride<T6>() ||
              writer.OptionalState.HasFormatterOverride<T7>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>() ||
              reader.OptionalState.HasFormatterOverride<T4>() ||
              reader.OptionalState.HasFormatterOverride<T5>() ||
              reader.OptionalState.HasFormatterOverride<T6>() ||
              reader.OptionalState.HasFormatterOverride<T7>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>(),
            reader.ReadValue<T7>()
        );
    }
}

[Preserve]
public sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest> : SharpPackFormatter<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>>
    where TRest : struct
{
    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T1>() ||
           graph.HasFormatterOverride<T2>() ||
           graph.HasFormatterOverride<T3>() ||
           graph.HasFormatterOverride<T4>() ||
           graph.HasFormatterOverride<T5>() ||
           graph.HasFormatterOverride<T6>() ||
           graph.HasFormatterOverride<T7>() ||
           graph.HasFormatterOverride<TRest>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>>() &&
            !(writer.OptionalState.HasFormatterOverride<T1>() ||
              writer.OptionalState.HasFormatterOverride<T2>() ||
              writer.OptionalState.HasFormatterOverride<T3>() ||
              writer.OptionalState.HasFormatterOverride<T4>() ||
              writer.OptionalState.HasFormatterOverride<T5>() ||
              writer.OptionalState.HasFormatterOverride<T6>() ||
              writer.OptionalState.HasFormatterOverride<T7>() ||
              writer.OptionalState.HasFormatterOverride<TRest>()))
        {
            writer.DangerousWriteUnmanaged(value);
            return;
        }

        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
        writer.WriteValue(value.Rest);
    }

    [Preserve]
    public override void Deserialize(ref SharpPackReader reader, scoped ref ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest> value)
    {
        if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>>() &&
            !(reader.OptionalState.HasFormatterOverride<T1>() ||
              reader.OptionalState.HasFormatterOverride<T2>() ||
              reader.OptionalState.HasFormatterOverride<T3>() ||
              reader.OptionalState.HasFormatterOverride<T4>() ||
              reader.OptionalState.HasFormatterOverride<T5>() ||
              reader.OptionalState.HasFormatterOverride<T6>() ||
              reader.OptionalState.HasFormatterOverride<T7>() ||
              reader.OptionalState.HasFormatterOverride<TRest>()))
        {
            reader.DangerousReadUnmanaged(out value);
            return;
        }

        value = new ValueTuple<T1?, T2?, T3?, T4?, T5?, T6?, T7?, TRest>(
            reader.ReadValue<T1>(),
            reader.ReadValue<T2>(),
            reader.ReadValue<T3>(),
            reader.ReadValue<T4>(),
            reader.ReadValue<T5>(),
            reader.ReadValue<T6>(),
            reader.ReadValue<T7>(),
            reader.ReadValue<TRest>()!
        );
    }
}
