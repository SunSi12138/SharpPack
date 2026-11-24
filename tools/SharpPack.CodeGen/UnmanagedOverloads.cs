using System.Text;

internal static class UnmanagedOverloads
{
    const int MaxArity = 15;

    public static string RenderReader()
    {
        var source = BeginFile("SharpPackReader");
        foreach (var prefix in new[] { "", "Dangerous" })
        {
            for (var arity = 1; arity <= MaxArity; arity++)
            {
                var arguments = Join(arity, i => $"out T{i} value{i}");
                AppendSignature(source, $"{prefix}ReadUnmanaged", arity, arguments, prefix.Length == 0);
                source.AppendLine($"        var size = {Size(arity)};");
                source.AppendLine("        ref var spanRef = ref GetSpanReference(size);");
                for (var i = 1; i <= arity; i++)
                {
                    source.AppendLine($"        value{i} = Unsafe.ReadUnaligned<T{i}>(ref {Address(i - 1, false)});");
                }
                AppendMethodEnd(source);
            }
        }

        return source.AppendLine("}").ToString();
    }

    public static string RenderWriter()
    {
        var source = BeginFile("SharpPackWriter<TBufferWriter>");
        foreach (var prefix in new[] { "", "Dangerous" })
        {
            for (var arity = 1; arity <= MaxArity; arity++)
            {
                AppendWriterMethod(source, prefix, arity, withHeader: false);
                AppendWriterMethod(source, prefix, arity, withHeader: true);
            }
        }

        return source.AppendLine("}").ToString();
    }

    static void AppendWriterMethod(StringBuilder source, string prefix, int arity, bool withHeader)
    {
        var name = prefix + (withHeader ? "WriteUnmanagedWithObjectHeader" : "WriteUnmanaged");
        var arguments = (withHeader ? "byte propertyCount, " : "") + Join(arity, i => $"scoped in T{i} value{i}");
        AppendSignature(source, name, arity, arguments, prefix.Length == 0);
        source.AppendLine($"        var size = {Size(arity)}{(withHeader ? " + 1" : "")};");
        source.AppendLine("        ref var spanRef = ref GetSpanReference(size);");
        if (withHeader)
        {
            source.AppendLine("        Unsafe.WriteUnaligned(ref spanRef, propertyCount);");
        }
        for (var i = 1; i <= arity; i++)
        {
            source.AppendLine($"        Unsafe.WriteUnaligned(ref {Address(i - 1, withHeader)}, value{i});");
        }
        AppendMethodEnd(source);
    }

    static StringBuilder BeginFile(string type) => new StringBuilder().AppendLine($$"""
        using System.Buffers;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        namespace SharpPack;

        public ref partial struct {{type}}
        {
        """);

    static void AppendSignature(StringBuilder source, string name, int arity, string arguments, bool constrained)
    {
        source.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        source.AppendLine($"    public void {name}<{Join(arity, i => $"T{i}")}>({arguments})");
        if (constrained)
        {
            for (var i = 1; i <= arity; i++)
            {
                source.AppendLine($"        where T{i} : unmanaged");
            }
        }
        source.AppendLine("    {");
    }

    static void AppendMethodEnd(StringBuilder source) => source.AppendLine("        Advance(size);").AppendLine("    }").AppendLine();

    static string Address(int previousValues, bool withHeader) => previousValues == 0
        ? (withHeader ? "Unsafe.Add(ref spanRef, 1)" : "spanRef")
        : $"Unsafe.Add(ref spanRef, {Size(previousValues)}{(withHeader ? " + 1" : "")})";

    static string Size(int count) => string.Join(" + ", Enumerable.Range(1, count).Select(i => $"Unsafe.SizeOf<T{i}>()"));

    static string Join(int count, Func<int, string> item) => string.Join(", ", Enumerable.Range(1, count).Select(item));
}
