using SharpPack.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SharpPack.Formatters;

[Preserve]
public sealed partial class TypeFormatter : SharpPackFormatter<Type>
{
    // Remove Version, Culture, PublicKeyToken from AssemblyQualifiedName.
    // Result will be "TypeName, Assembly"
    // see:http://msdn.microsoft.com/en-us/library/w3f99sx1.aspx


    [GeneratedRegex(@", Version=\d+.\d+.\d+.\d+, Culture=[\w-]+, PublicKeyToken=(?:null|[a-f0-9]{16})")]
    private static partial Regex ShortTypeNameRegex();


    [Preserve]
    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref Type? value)
    {
        var context = writer.OptionalState.SerializerContext;
        context?.ThrowIfTypePayloadDisabled();

        var full = value?.AssemblyQualifiedName;
        if (full == null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }

        context?.ObserveSerializedType(value!);
        var shortName = ShortTypeNameRegex().Replace(full, "");
        writer.WriteString(shortName);
    }

    [Preserve]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    public override void Deserialize(ref SharpPackReader reader, scoped ref Type? value)
    {
        var context = reader.OptionalState.SerializerContext;
        context?.ThrowIfTypePayloadDisabled();

        var typeName = reader.ReadString();
        if (typeName == null)
        {
            value = null;
            return;
        }

        value = ResolveType(typeName, context);
        if (value is null &&
            typeName.Contains("MemoryPack", StringComparison.Ordinal))
        {
            var sharpPackTypeName = typeName.Replace(
                "MemoryPack",
                "SharpPack",
                StringComparison.Ordinal);
            value = ResolveType(sharpPackTypeName, context);
        }

        if (value is null)
        {
            SharpPackSerializationException.ThrowMessage(
                $"Type '{typeName}' could not be resolved.");
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Serialized Type values are inherently data-driven; applications must preserve the types they exchange.")]
    static Type? ResolveType(
        string typeName,
        SharpPackSerializerContext? context)
    {
        return context is null
            ? Type.GetType(typeName, throwOnError: false)
            : context.ResolveType(typeName);
    }
}
