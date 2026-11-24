internal static class TupleFormatters
{
    const int RestArity = 8;

    public static string Render()
    {
        var arities = Enumerable.Range(1, RestArity);
        return "using SharpPack.Internal;\n\nnamespace SharpPack.Formatters;\n\n"
            + string.Join("\n\n", arities.Select(RenderReferenceTuple).Concat(arities.Select(RenderValueTuple)))
            + "\n";
    }

    static string RenderReferenceTuple(int arity)
    {
        var type = $"Tuple<{TypeParameters(arity, nullable: true)}>";
        var constraint = arity == RestArity ? "    where TRest : notnull\n" : "";
        return $$"""
            [Preserve]
            public sealed class TupleFormatter<{{TypeParameters(arity)}}> : SharpPackFormatter<{{type}}>
            {{constraint}}{
                [Preserve]
                public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref {{type}}? value)
                {
                    if (value == null)
                    {
                        writer.WriteNullObjectHeader();
                        return;
                    }

                    writer.WriteObjectHeader({{arity}});
            {{WriteMembers(arity)}}
                }

                [Preserve]
                public override void Deserialize(ref SharpPackReader reader, scoped ref {{type}}? value)
                {
                    if (!reader.TryReadObjectHeader(out var count))
                    {
                        value = null;
                        return;
                    }

                    if (count != {{arity}}) SharpPackSerializationException.ThrowInvalidPropertyCount({{arity}}, count);

                    value = new {{type}}(
            {{ReadMembers(arity)}}
                    );
                }
            }
            """;
    }

    static string RenderValueTuple(int arity)
    {
        var type = $"ValueTuple<{TypeParameters(arity, nullable: true)}>";
        var constraint = arity == RestArity ? "    where TRest : struct\n" : "";
        return $$"""
            [Preserve]
            public sealed class ValueTupleFormatter<{{TypeParameters(arity)}}> : SharpPackFormatter<{{type}}>
            {{constraint}}{
                internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
                    => {{HasOverride(arity, "graph", 11)}};

                [Preserve]
                public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref {{type}} value)
                {
                    if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<{{type}}>() &&
                        !{{StateHasOverride(arity, "writer.OptionalState")}})
                    {
                        writer.DangerousWriteUnmanaged(value);
                        return;
                    }

            {{WriteMembers(arity)}}
                }

                [Preserve]
                public override void Deserialize(ref SharpPackReader reader, scoped ref {{type}} value)
                {
                    if (!System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<{{type}}>() &&
                        !{{StateHasOverride(arity, "reader.OptionalState")}})
                    {
                        reader.DangerousReadUnmanaged(out value);
                        return;
                    }

                    value = new {{type}}(
            {{ReadMembers(arity)}}
                    );
                }
            }
            """;
    }

    static string TypeParameter(int index) => index == RestArity ? "TRest" : $"T{index}";

    static string TypeParameters(int arity, bool nullable = false) => Join(arity, ", ", i =>
        TypeParameter(i) + (nullable && i != RestArity ? "?" : ""));

    static string WriteMembers(int arity) => Join(arity, "\n", i =>
        $"        writer.WriteValue(value.{(i == RestArity ? "Rest" : $"Item{i}")});");

    static string ReadMembers(int arity) => Join(arity, ",\n", i =>
        $"            reader.ReadValue<{TypeParameter(i)}>(){(i == RestArity ? "!" : "")}");

    static string HasOverride(int arity, string source, int indent) => Join(arity, " ||\n" + new string(' ', indent),
        i => $"{source}.HasFormatterOverride<{TypeParameter(i)}>()");

    static string StateHasOverride(int arity, string source)
    {
        var expression = HasOverride(arity, source, 14);
        return arity == 1 ? expression : $"({expression})";
    }

    static string Join(int count, string separator, Func<int, string> item) =>
        string.Join(separator, Enumerable.Range(1, count).Select(item));
}
