using SharpPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public partial class GeneratorDiagnosticsTest
{
    void Compile2(int id, string code, bool allowMultipleError = false)
    {
        var (_, diagnostics) = CSharpGeneratorRunner.RunGenerator(code, options: new TypeScriptOptionProvider());
        if (!allowMultipleError)
        {
            diagnostics.Length.Should().Be(1);
            diagnostics[0].Id.Should().Be("SHARPPACK" + id.ToString("000"));
        }
        else
        {
            diagnostics.Select(x => x.Id).Should().Contain("SHARPPACK" + id.ToString("000"));
        }
    }

    string CompileAndRead(string code, string fileName, bool enableNullableTypes = true, bool convertPropertyName = true)
    {
        var directory = Directory.CreateTempSubdirectory("sharppack-ts-");
        var optionProvider = new TypeScriptOptionProvider();

        optionProvider["build_property.SharpPackGenerator_TypeScriptOutputDirectory"] = directory.FullName;
        optionProvider["build_property.SharpPackGenerator_TypeScriptEnableNullableTypes"] = enableNullableTypes ? "true" : "false";
        optionProvider["build_property.SharpPackGenerator_TypeScriptConvertPropertyName"] = convertPropertyName ? "true" : "false";

        try
        {
            var (_, diagnostics) = CSharpGeneratorRunner.RunGenerator(code, options: optionProvider);
            Assert.Empty(diagnostics);
            return File.ReadAllText(Path.Combine(directory.FullName, fileName));
        }
        finally
        {
            foreach (var file in directory.EnumerateFiles()) file.Delete();
            directory.Delete();
        }
    }

    [Fact]
    public void SHARPPACK027_GenerateTypeScriptMustBeSharpPackable()
    {
        Compile2(27, """
using SharpPack;

[GenerateTypeScript]
public class Hoge
{
}
""");
    }

    [Fact]
    public void SHARPPACK028_GenerateTypeScriptOnlyAllowsGenerateTypeObject()
    {
        Compile2(28, """
using SharpPack;

[SharpPackable(GenerateType.Collection)]
[GenerateTypeScript]
public partial class Hoge : System.Collections.Generic.List<int>
{
}
""");
    }

    [Fact]
    public void SHARPPACK029_GenerateTypeScriptDoesNotAllowGenerics()
    {
        Compile2(29, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge<T>
{
}
""", true);
    }

    [Fact]
    public void SHARPPACK030_GenerateTypeScriptDoesNotAllowLongEnum()
    {
        Compile2(30, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);

        Compile2(30, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum[] MyProperty { get; set; }
}

public enum LongEnum : ulong
{

}
""", true);

        Compile2(30, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum? MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);

        Compile2(30, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public System.Collections.Generic.List<LongEnum> MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);
    }

    [Fact]
    public void SHARPPACK031_GenerateTypeScriptNotSupportedType()
    {
        Compile2(31, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public System.Version MyProperty { get; set; }
}
""", true);
    }

    [Fact]
    public void SHARPPACK032_GenerateTypeScriptNotSupportedType()
    {
        Compile2(32, """
using SharpPack;

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    [Utf8StringFormatter]
    public string MyProperty { get; set; }
}
""", true);
    }

    [Fact]
    public void GenerateTypeScriptNullableReferenceTypes()
    {
        var generatedCode = CompileAndRead(
            """
            using SharpPack;

            [SharpPackable]
            [GenerateTypeScript]
            public partial record FullName
            {
                public string FirstName { get; init; }
                public string? MiddleName { get; init; }
                public string LastName { get; init; }
            }
            """,
            "FullName.ts");

        generatedCode.Should().Contain(
            """
            export class FullName {
                firstName: string;
                middleName: string | null;
                lastName: string;

                constructor() {
                    this.firstName = "";
                    this.middleName = null;
                    this.lastName = "";
            """);
    }

    [Fact]
    public void GenerateTypeScriptCanPreservePropertyNames()
    {
        var code = CompileAndRead("""
            [SharpPackable, GenerateTypeScript] public partial class Counters
            {
                public int URLCount { get; set; }
            }
            """, "Counters.ts", convertPropertyName: false);

        Assert.Contains("URLCount: number;", code);
        Assert.Contains("writer.writeInt32(value.URLCount);", code);
        Assert.Contains("value.URLCount = reader.readInt32();", code);
    }


    [Fact]
    public void CompilerArtifactPlanRendersTheLegacyOutputsWithoutWritingThem()
    {
        const string source = """
            [SharpPackable, GenerateTypeScript, SharpPackUnion(19, typeof(Update))]
            public partial interface IEvent;

            [SharpPackable, GenerateTypeScript]
            public partial class Update : IEvent
            {
                public string? Description { get; set; }
                public State Status { get; set; }
            }

            public enum State : byte { Active = 1, Closed = 4 }
            """;

        var root = Directory.CreateTempSubdirectory("SharpPack artifact plan 测试 ");
        var legacyDirectory = Path.Combine(root.FullName, "legacy");
        var toolDirectory = Path.Combine(root.FullName, "tool");
        try
        {
            var (_, legacyDiagnostics) = CSharpGeneratorRunner.RunGenerator(source, options: Options(legacyDirectory));
            Assert.Empty(legacyDiagnostics);

            var tooling = Options(toolDirectory);
            tooling["build_property.SharpPackGenerator_ArtifactMode"] = "plan";
            var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator(source, options: tooling);
            Assert.Empty(diagnostics);
            Assert.False(Directory.Exists(toolDirectory));

            var schema = Assert.Single(compilation.SyntaxTrees, tree => tree.FilePath.EndsWith(ArtifactSchema.FileName, StringComparison.Ordinal));
            var text = schema.ToString();
            using var reader = new StringReader(text);
            var plan = ArtifactSchema.ReadSource(reader);
            Assert.Equal(text, ArtifactSchema.WriteSource(plan));
            var script = Assert.IsType<TypeScriptArtifactPlan>(plan.TypeScript);
            var files = script.Models.Select(model => (model.Name + ".ts", TypeScriptEmitter.EmitModelFile(model, script.ImportExtension)))
                .Concat(script.Enums.Select(model => (model.Name + ".ts", TypeScriptEmitter.EmitEnumFile(model))))
                .Concat(TypeScriptEmitter.RuntimeFiles())
                .Concat(plan.SerializationInfo.Select(model => (model.FileName, model.Text)))
                .OrderBy(file => file.Item1, StringComparer.Ordinal).ToArray();

            Assert.Equal(Directory.GetFiles(legacyDirectory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal),
                files.Select(file => file.Item1));
            foreach (var (name, contents) in files)
                Assert.Equal(Encoding.UTF8.GetBytes(contents), File.ReadAllBytes(Path.Combine(legacyDirectory, name)));

            foreach (var (mode, designTime) in new[] { ("plan", "true"), ("none", "false") })
            {
                tooling["build_property.SharpPackGenerator_ArtifactMode"] = mode;
                tooling["build_property.DesignTimeBuild"] = designTime;
                var (disabled, _) = CSharpGeneratorRunner.RunGenerator(source, options: tooling);
                Assert.DoesNotContain(disabled.SyntaxTrees, tree => tree.FilePath.EndsWith(ArtifactSchema.FileName, StringComparison.Ordinal));
                Assert.False(Directory.Exists(toolDirectory));
            }
        }
        finally
        {
            if (Directory.Exists(legacyDirectory))
            {
                foreach (var file in Directory.GetFiles(legacyDirectory)) File.Delete(file);
                Directory.Delete(legacyDirectory);
            }
            root.Delete();
        }

        static TypeScriptOptionProvider Options(string path) => new()
        {
            ["build_property.SharpPackGenerator_TypeScriptOutputDirectory"] = path,
            ["build_property.SharpPackGenerator_TypeScriptImportExtension"] = ".mjs",
            ["build_property.SharpPackGenerator_SerializationInfoOutputDirectory"] = path,
        };
    }

    internal sealed class TypeScriptOptionProvider : AnalyzerConfigOptionsProvider
    {
        readonly Dictionary<string, string> _values = new();

        public string this[string key]
        {
            get => _values[key];
            set => _values[key] = value;
        }

        public override AnalyzerConfigOptions GlobalOptions => new SimpleOptions(_values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return new SimpleOptions(_values);
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return new SimpleOptions(_values);
        }

        public class SimpleOptions : AnalyzerConfigOptions
        {
            readonly Dictionary<string, string> _configValues;

            public SimpleOptions(Dictionary<string, string> configValues)
            {
                _configValues = configValues;
            }

            /// <inheritdoc />
            public override IEnumerable<string> Keys => _configValues.Keys;

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (_configValues.ContainsKey(key))
                {
                    value = _configValues[key];

                    return true;
                }

                if (key == "build_property.SharpPackGenerator_TypeScriptOutputDirectory")
                {
                    value = Path.GetTempPath();
                    return true;
                }

                value = null;
                return false;
            }
        }
    }
}

[SharpPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum? MyProperty { get; set; }
}

public enum LongEnum : long
{

}

public enum ULongEnum : ulong
{

}
