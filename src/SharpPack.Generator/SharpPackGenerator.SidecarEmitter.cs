using System.Globalization;
using System.Text;

namespace SharpPack.Generator;

internal sealed partial class CSharpEmitter
{
    internal static string EmitSidecarProvider(string providerName, FormatterNode[] nodes)
    {
        const string registration = "global::SharpPack.CompilerServices.SharpPackSidecarRegistration";
        var source = new StringBuilder();
        AppendPreamble(source);
        source.AppendLine($$"""
namespace SharpPack.Generated
{
    internal static class {{providerName}}
    {
        internal static global::SharpPack.SharpPackSerializerContextBuilder Register(
            global::SharpPack.SharpPackSerializerContextBuilder builder)
        {
            global::System.ArgumentNullException.ThrowIfNull(builder);
""");

        var classes = new StringBuilder();
        foreach (var node in nodes)
        {
            var target = node.Type.FullyQualifiedToString();
            if (node.Sidecar is { } plan)
            {
                var name = SidecarName(target);
                var creation = AppendObjectSidecar(classes, plan, name)
                    ? $"static context => new {name}(context)" : $"static _ => new {name}()";
                source.AppendLine($"            {registration}.Register<{target}>(builder, {creation});");
            }
            else if (node.BuiltInFormatter is { } formatter)
            {
                source.AppendLine($"            {registration}.RegisterBuiltIn<{target}>(builder, static () => new {formatter}());");
            }
            else if (node.Factory != TypeOwnedFactory.None)
            {
                var method = node.Factory == TypeOwnedFactory.Context ? "ContextFactory" : "DefaultFactory";
                source.AppendLine($"            {registration}.Register<{target}>(builder, {method}<{target}>);");
            }
        }

        source.AppendLine("""
            return builder;
        }
""");
        if (nodes.Any(static node => node.Factory == TypeOwnedFactory.Context))
            source.AppendLine("""
        static global::SharpPack.SharpPackFormatter<T> ContextFactory<T>(global::SharpPack.SharpPackSerializerContext context)
            where T : global::SharpPack.ISharpPackContextFormatterFactory<T> => T.CreateFormatter(context);
""");
        if (nodes.Any(static node => node.Factory == TypeOwnedFactory.Default))
            source.AppendLine("""
        static global::SharpPack.SharpPackFormatter<T> DefaultFactory<T>(global::SharpPack.SharpPackSerializerContext _)
            where T : global::SharpPack.ISharpPackFormatterFactory<T> => T.CreateFormatter();
""");
        source.Append(classes);
        source.AppendLine("""
    }
}
""");
        return source.ToString();
    }

    // One provider output, with collision-free, type-stable identifiers for
    // the private classes. Escape underscores as well as punctuation.
    static string SidecarName(string typeName)
    {
        var name = new StringBuilder("Formatter_");
        foreach (var c in typeName)
        {
            if (c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                name.Append(c);
            else
                name.Append('_').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
        return name.ToString();
    }

    // The declaration shell is different; the object wire body is the same.
    static bool AppendObjectSidecar(StringBuilder source, SerializationPlan plan, string formatterName)
    {
        var emitter = new CSharpEmitter(plan, plan.Shape.TargetName);
        var members = emitter.WireMembers();
        var typeName = emitter.TypeName;
        var nullable = emitter.IsValueType ? "" : "?";

        var checks = members.Where(static member => member.Kind is
                MemberKind.Unmanaged or MemberKind.Enum or MemberKind.UnmanagedNullable or MemberKind.String or
                MemberKind.UnmanagedArray or MemberKind.Array or MemberKind.SharpPackableArray or
                MemberKind.SharpPackableList or MemberKind.SharpPackableUnmanagedList)
            .SelectMany(static member => member.ContextDependencyTypes)
            .Distinct(StringComparer.Ordinal)
            .Select(static type => $"context.HasFormatterOverrideDependency<{type}>()")
            .ToArray();
        var hasInitializer = checks.Length != 0;
        var initialize = hasInitializer ? $$"""
            private readonly bool useOverrides;
            internal {{formatterName}}(global::SharpPack.SharpPackSerializerContext context)
            {
                useOverrides = {{string.Join(" || ", checks)}};
            }
""" : "";
        var writeOverrides = hasInitializer ? """
            if (useOverrides)
            {
                WriteWithOverrides(ref writer, ref value);
                return;
            }
""" : "";
        var readOverrides = hasInitializer ? """
            if (useOverrides)
            {
                ReadWithOverrides(ref reader, ref value);
                return;
            }
""" : "";

        source.AppendLine($$"""

        private sealed class {{formatterName}} : global::SharpPack.SharpPackFormatter<{{typeName}}>,
            global::SharpPack.ISharpPackContextOverrideFormatter
        {
{{initialize}}
            public override void Serialize<TBufferWriter>(
                ref global::SharpPack.SharpPackWriter<TBufferWriter> writer, scoped ref {{typeName}}{{nullable}} value)
            {
{{writeOverrides}}
{{emitter.EmitSerializeBody(members)}}
            END:
                return;
            }

            public override void Deserialize(ref global::SharpPack.SharpPackReader reader, scoped ref {{typeName}}{{nullable}} value)
            {
{{readOverrides}}
{{emitter.EmitDeserializeBody(members, honorFormatterOverrides: false)}}
            END:
                return;
            }
""");

        if (hasInitializer)
        {
            source.AppendLine($$"""

            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            static void WriteWithOverrides<TBufferWriter>(
                ref global::SharpPack.SharpPackWriter<TBufferWriter> writer, scoped ref {{typeName}}{{nullable}} value)
                where TBufferWriter : global::System.Buffers.IBufferWriter<byte>
            {
{{emitter.EmitObjectSerializeBody(members, plan.ScalarBatches, honorFormatterOverrides: true)}}
            END:
                return;
            }

            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            static void ReadWithOverrides(ref global::SharpPack.SharpPackReader reader, scoped ref {{typeName}}{{nullable}} value)
            {
{{emitter.EmitDeserializeBody(members, honorFormatterOverrides: true)}}
            END:
                return;
            }
""");
        }

        source.AppendLine("        }");
        return hasInitializer;
    }
}
