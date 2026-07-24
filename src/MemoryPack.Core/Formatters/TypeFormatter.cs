using MemoryPack.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MemoryPack.Formatters;

[Preserve]
public sealed partial class TypeFormatter : MemoryPackFormatter<Type>
{
    // Remove Version, Culture, PublicKeyToken from AssemblyQualifiedName.
    // Result will be "TypeName, Assembly"
    // see:http://msdn.microsoft.com/en-us/library/w3f99sx1.aspx


    [GeneratedRegex(@", Version=\d+.\d+.\d+.\d+, Culture=[\w-]+, PublicKeyToken=(?:null|[a-f0-9]{16})")]
    private static partial Regex ShortTypeNameRegex();


    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Type? value)
    {
        var full = value?.AssemblyQualifiedName;
        if (full == null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }

        writer.OptionalState.SerializerContext?.AddType(value!);
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
    public override void Deserialize(ref MemoryPackReader reader, scoped ref Type? value)
    {
        var typeName = reader.ReadString();
        if (typeName == null)
        {
            value = null;
            return;
        }

        if (reader.OptionalState.SerializerContext is { } context)
        {
            value = Type.GetType(
                typeName,
                context.ResolveAssembly,
                static (assembly, name, ignoreCase) =>
                    assembly?.GetType(name, throwOnError: false, ignoreCase),
                throwOnError: false);
            value ??= Type.GetType(typeName, throwOnError: false);
            if (value is null)
            {
                MemoryPackSerializationException.ThrowMessage(
                    $"Type '{typeName}' is not registered in the active serializer context.");
            }
            context.AddType(value);
            return;
        }

        value = Type.GetType(typeName, throwOnError: true);
    }
}
