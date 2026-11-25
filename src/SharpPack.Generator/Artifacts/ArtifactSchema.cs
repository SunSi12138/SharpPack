using System.Text;

namespace SharpPack.Generator;

internal sealed record ProjectArtifactPlan(TypeScriptArtifactPlan? TypeScript, EquatableArray<SerializationInfoFile> SerializationInfo);

internal sealed record SerializationInfoFile(string FileName, string Text);

// The compiler writes this comment-only C# file into its generated output
// directory. It contains plans, not destinations or executable source.
internal static class ArtifactSchema
{
    internal const string FileName = "SharpPack.ToolingArtifacts.g.cs";
    const string Header = "// sharp-pack-artifacts:1";
    const string Comment = "// ";
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static string WriteSource(ProjectArtifactPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            writer.Write(plan.TypeScript is not null);
            if (plan.TypeScript is { } script)
            {
                writer.Write(script.ImportExtension);
                WriteArray(writer, script.Models, WriteModel);
                WriteArray(writer, script.Enums, static (output, model) =>
                {
                    output.Write(model.Name);
                    WriteArray(output, model.Fields, static (fieldOutput, field) =>
                    {
                        fieldOutput.Write(field.Name);
                        WriteOptionalString(fieldOutput, field.Constant);
                    });
                });
            }

            WriteArray(writer, plan.SerializationInfo, static (output, file) =>
            {
                output.Write(file.FileName);
                output.Write(file.Text);
            });
        }

        return Header + "\n" + Comment + Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length)) + "\n";
    }

    internal static ProjectArtifactPlan ReadSource(TextReader source)
    {
        if (source.ReadLine() != Header || source.ReadLine() is not { } data ||
            !data.StartsWith(Comment, StringComparison.Ordinal) || source.ReadLine() is not null)
            throw new InvalidDataException("The SharpPack artifact plan format is not supported. Rebuild with the matching generator.");

        using var stream = new MemoryStream(Convert.FromBase64String(data.Substring(Comment.Length)), writable: false);
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
        var script = reader.ReadBoolean() ? new TypeScriptArtifactPlan(reader.ReadString(), ReadArray(reader, ReadModel),
            ReadArray(reader, static input => new TypeScriptEnumPlan(input.ReadString(),
                ReadArray(input, static field => new TypeScriptEnumField(field.ReadString(), ReadOptionalString(field)))))) : null;
        var info = ReadArray(reader, static input => new SerializationInfoFile(input.ReadString(), input.ReadString()));
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected data after the SharpPack artifact plan.");
        return new(script, info);
    }

    static void WriteModel(BinaryWriter writer, TypeScriptModelPlan model)
    {
        writer.Write(model.Name);
        WriteArray(writer, model.Imports, static (output, name) => output.Write(name));
        writer.Write(model.UnionCases.HasValue);
        if (model.UnionCases is { } cases)
        {
            WriteArray(writer, cases, static (output, item) =>
            {
                output.Write(item.Tag);
                output.Write(item.Name);
            });
        }
        else
        {
            WriteOptionalString(writer, model.UnionBase);
            WriteArray(writer, model.Members, static (output, member) =>
            {
                output.Write(member.MemberName);
                output.Write(member.TypeName);
                output.Write(member.DefaultValue);
                output.Write(member.WriteMethodTemplate);
                output.Write(member.ReadMethodTemplate);
            });
        }
    }

    static TypeScriptModelPlan ReadModel(BinaryReader reader)
    {
        var name = reader.ReadString();
        var imports = ReadArray(reader, static input => input.ReadString());
        if (reader.ReadBoolean())
            return new(name, imports, null, default,
                ReadArray(reader, static input => new TypeScriptUnionCase(input.ReadUInt16(), input.ReadString())));

        return new(name, imports, ReadOptionalString(reader),
            ReadArray(reader, static input => new TypeScriptMemberShape(
                input.ReadString(), input.ReadString(), input.ReadString(), input.ReadString(), input.ReadString())), null);
    }

    static void WriteOptionalString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null) writer.Write(value);
    }

    static string? ReadOptionalString(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;

    static void WriteArray<T>(BinaryWriter writer, EquatableArray<T> values, Action<BinaryWriter, T> write)
        where T : IEquatable<T>
    {
        writer.Write(values.Length);
        foreach (var value in values) write(writer, value);
    }

    static EquatableArray<T> ReadArray<T>(BinaryReader reader, Func<BinaryReader, T> read) where T : IEquatable<T>
    {
        var count = reader.ReadInt32();
        // Every item takes at least one byte. Reject nonsensical sizes before allocation.
        if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid SharpPack artifact list size.");
        var values = new T[count];
        for (var index = 0; index < count; index++) values[index] = read(reader);
        return new(values);
    }
}
