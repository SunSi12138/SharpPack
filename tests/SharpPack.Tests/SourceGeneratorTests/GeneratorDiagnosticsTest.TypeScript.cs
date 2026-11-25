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
        var optionProvider = new TypeScriptOptionProvider();
        optionProvider["build_property.SharpPackGenerator_TypeScriptEnableNullableTypes"] = enableNullableTypes ? "true" : "false";
        optionProvider["build_property.SharpPackGenerator_TypeScriptConvertPropertyName"] = convertPropertyName ? "true" : "false";

        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator(code, options: optionProvider);
        Assert.Empty(diagnostics);
        using var reader = new StringReader(ArtifactSource(compilation).ToString());
        var plan = Assert.IsType<TypeScriptArtifactPlan>(ArtifactSchema.ReadSource(reader).TypeScript);
        return Assert.Single(TypeScriptEmitter.RenderFiles(plan), file => file.Name == fileName).Source;
    }

    static SyntaxTree ArtifactSource(Compilation compilation)
        => Assert.Single(compilation.SyntaxTrees, tree => tree.FilePath.EndsWith(ArtifactSchema.FileName, StringComparison.Ordinal));

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
    public void CompilerEmitsACompleteArtifactPlanWithoutWritingToTheDestination()
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

        var destination = Path.Combine(Path.GetTempPath(), $"SharpPack artifact plan 测试 {Guid.NewGuid():N}");
        var tooling = new TypeScriptOptionProvider
        {
            ["build_property.SharpPackGenerator_TypeScriptOutputDirectory"] = destination,
            ["build_property.SharpPackGenerator_TypeScriptImportExtension"] = ".mjs",
            ["build_property.SharpPackGenerator_SerializationInfoOutputDirectory"] = destination,
        };
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator(source, options: tooling);
        Assert.Empty(diagnostics);
        Assert.False(Directory.Exists(destination));

        var text = ArtifactSource(compilation).ToString();
        using var reader = new StringReader(text);
        var plan = ArtifactSchema.ReadSource(reader);
        Assert.Equal(text, ArtifactSchema.WriteSource(plan));
        var script = Assert.IsType<TypeScriptArtifactPlan>(plan.TypeScript);
        Assert.Equal(["IEvent.ts", "SharpPackReader.ts", "SharpPackWriter.ts", "State.ts", "Update.ts"],
            TypeScriptEmitter.RenderFiles(script).Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("Update.txt", Assert.Single(plan.SerializationInfo).FileName);

        foreach (var (mode, designTime) in new[] { ("plan", "true"), ("none", "false") })
        {
            tooling["build_property.SharpPackGenerator_ArtifactMode"] = mode;
            tooling["build_property.DesignTimeBuild"] = designTime;
            var (disabled, _) = CSharpGeneratorRunner.RunGenerator(source, options: tooling);
            Assert.DoesNotContain(disabled.SyntaxTrees, tree => tree.FilePath.EndsWith(ArtifactSchema.FileName, StringComparison.Ordinal));
            Assert.False(Directory.Exists(destination));
        }
    }

    internal sealed class TypeScriptOptionProvider : AnalyzerConfigOptionsProvider
    {
        readonly Dictionary<string, string> _values = new()
        {
            ["build_property.SharpPackGenerator_ArtifactMode"] = "plan",
            ["build_property.SharpPackGenerator_TypeScriptOutputDirectory"] = "typescript",
        };

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
                => _configValues.TryGetValue(key, out value);
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
