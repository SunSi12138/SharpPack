using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPack.Generator;
using System;
using System.Linq;
using System.Threading;

namespace SharpPack.Tests.SourceGeneratorTests;

public class TypeShapeTest
{
    const string Model = """
        using System;
        using SharpPack;
        namespace ShapeFixtures;

        [SharpPackable(GenerateType.VersionTolerant)]
        public partial class Envelope
        {
            [SharpPackOrder(2)]
            [Inspect(new[] { 10, 20 }, typeof(Envelope), Mode.Second, Label = "wire")]
            public int Payload { get; }
            public Envelope(int payload) { Payload = payload; }
            [SharpPackOnDeserialized]
            void OnDone() { }
        }

        public enum Mode { First, Second }
        public sealed class InspectAttribute : SharpPackCustomFormatterAttribute<SharpPackFormatter<int>, int>
        {
            public InspectAttribute(int[] values, Type type, Mode mode) { }
            public string Label { get; set; } = "";
            public override SharpPackFormatter<int> GetFormatter() => new SharpPack.Formatters.UnmanagedFormatter<int>();
        }
        """;

    [Fact]
    public void SnapshotIncludesConstructorCallbackAndTypedFormatterArguments()
    {
        var shape = Analyze(Model, "Envelope");
        Assert.Equal("global::ShapeFixtures.Envelope", shape.DeclarationName);
        Assert.Equal(Generator.GenerateType.VersionTolerant, shape.GenerateType);
        Assert.Equal(Generator.SerializeLayout.Explicit, shape.Layout);
        var member = Assert.Single(shape.Members);
        Assert.Equal(("Payload", "int", 2, "payload"),
            (member.Name, member.TypeName, member.Order, member.ConstructorParameter));
        Assert.True(member.HasExplicitOrder);
        Assert.Equal(new ParameterShape("payload", "int"), Assert.Single(shape.ConstructorParameters));
        Assert.Equal(new CallbackShape(CallbackPhase.Deserialized, "OnDone", false, false, false),
            Assert.Single(shape.Callbacks));
        var formatter = member.CustomFormatter!;
        Assert.Equal("global::ShapeFixtures.InspectAttribute", formatter.AttributeType);
        Assert.Equal(new object?[] { 10, 20 }, formatter.Arguments[0].Elements.Select(item => item.Scalar));
        Assert.Equal("global::ShapeFixtures.Envelope", formatter.Arguments[1].TypeName);
        Assert.Equal("global::ShapeFixtures.Mode", formatter.Arguments[2].TypeName);
        var named = Assert.Single(formatter.Initializers);
        Assert.Equal("Label", named.Name);
        Assert.Equal("wire", named.Value.Scalar);

        var (output, diagnostics) = CSharpGeneratorRunner.RunGenerator(Model);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith(".SharpPackFormatter.g.cs")).ToString();
        Assert.Contains("new global::ShapeFixtures.InspectAttribute(new int[] { 10, 20 }, typeof(global::ShapeFixtures.Envelope), (global::ShapeFixtures.Mode)1) { @Label = \"wire\" }.GetFormatter();", generated);
        Assert.Contains("value?.OnDone();", generated);
    }

    [Fact]
    public void EqualityIgnoresRoslynIdentityAndDetectsCallbackAndFormatterChanges()
    {
        var original = Analyze(Model, "Envelope");
        var unaffected = Analyze(Model + "\n// trivia", "Envelope", "internal class Unrelated { public string? Name { get; set; } }");
        Assert.Equal(original, unaffected);
        Assert.Equal(original.GetHashCode(), unaffected.GetHashCode());
        Assert.NotEqual(original, Analyze(Model.Replace("10, 20", "10, 30"), "Envelope"));
        Assert.NotEqual(original, Analyze(Model.Replace("OnDone", "AfterLoad"), "Envelope"));
        Assert.NotEqual(original, Analyze(Model.Replace("SharpPackOrder(2)", "SharpPackOrder(3)"), "Envelope"));
    }

    [Fact]
    public void NullFormatterArgumentsRetainTheirDeclaredType()
    {
        var source = Model.Replace("new[] { 10, 20 }", "null");
        var ints = Analyze(source, "Envelope");
        var strings = Analyze(source.Replace("int[] values", "string[] values"), "Envelope");
        Assert.Equal("int[]", ints.Members[0].CustomFormatter!.Arguments[0].TypeName);
        Assert.Equal("string[]", strings.Members[0].CustomFormatter!.Arguments[0].TypeName);
        Assert.NotEqual(ints, strings);
    }

    [Fact]
    public void InterfaceAndAbstractClassUnionsHaveDifferentShapes()
    {
        const string source = """
            using SharpPack;
            [SharpPackable]
            [SharpPackUnion(1, typeof(Message))]
            public partial interface IMessage { }
            [SharpPackable] public partial class Message : IMessage { }
            """;
        var asInterface = Analyze(source, "IMessage");
        var asClass = Analyze(source.Replace("partial interface", "abstract partial class"), "IMessage");
        Assert.Equal(TypeKind.Interface, asInterface.DeclarationKind);
        Assert.Equal(TypeKind.Interface, asInterface.TargetKind);
        Assert.Equal(TypeKind.Class, asClass.DeclarationKind);
        Assert.NotEqual(asInterface, asClass);
    }

    [Fact]
    public void ExternalUnionRecordsTargetIndependentlyOfTheFormatterDeclaration()
    {
        const string source = """
            using SharpPack;
            namespace Models
            {
                [SharpPackable(GenerateType.NoGenerate)] public partial interface IMessage { }
                [SharpPackable] public partial class Message : IMessage { public int Id { get; set; } }
            }
            namespace Serialization
            {
                [SharpPackUnionFormatter(typeof(Models.IMessage))]
                [SharpPackUnion(19, typeof(Models.Message))]
                public partial class MessageFormatter { }
            }
            """;
        var shape = Analyze(source, "MessageFormatter");
        Assert.Equal("global::Serialization.MessageFormatter", shape.DeclarationName);
        Assert.Equal("global::Models.IMessage", shape.TargetName);
        Assert.Equal(TypeKind.Class, shape.DeclarationKind);
        Assert.Equal(TypeKind.Interface, shape.TargetKind);
        Assert.Equal(default(GeneratedNameFacts), shape.NameFacts);
        var union = Assert.IsType<UnionShape>(shape.Union);
        Assert.Equal("global::Models.IMessage", union.TypeName);
        Assert.Equal("MessageFormatter", union.ExternalFormatter?.Name);
        Assert.Equal(new UnionCaseShape(19, "global::Models.Message", UsePackablePath: true), Assert.Single(union.Cases));
    }

    [Fact]
    public void CollectionCapturesSelectedInterfaceArguments()
    {
        const string source = """
            using SharpPack;
            [SharpPackable(GenerateType.Collection)]
            public partial class Mapping : System.Collections.Generic.Dictionary<int, string> { }
            """;

        var collection = Assert.IsType<CollectionShape>(Analyze(source, "Mapping").Collection);
        Assert.Equal(CollectionKind.Dictionary, collection.Kind);
        Assert.Equal(new[] { "int", "string" }, collection.TypeArguments.ToArray());

        var (compiled, diagnostics) = CSharpGeneratorRunner.RunGenerator(source);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(compiled.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var emitted = compiled.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("Mapping.SharpPackFormatter.g.cs")).ToString();
        Assert.Contains("new global::SharpPack.Formatters.GenericDictionaryFormatter<Mapping, int, string>()", emitted);
    }

    [Fact]
    public void ArraySnapshotDoesNotRetainTheCallersMutableArray()
    {
        string[] items = ["first", "second"];
        var snapshot = new EquatableArray<string>(items);
        items[0] = "changed";
        Assert.Equal(new EquatableArray<string>(["first", "second"]), snapshot);
        Assert.Equal(default, new EquatableArray<string>());
        Assert.Empty(default(EquatableArray<string>));
    }

    internal static TypeShape Analyze(string text, string name, string unrelated = "")
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp14);
        var tree = CSharpSyntaxTree.ParseText(text, options, "Models.cs");
        var compilation = CSharpGeneratorRunner.CreateCompilation(tree, CSharpSyntaxTree.ParseText(unrelated, options, "Other.cs"));
        var syntax = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Single(node => node.Identifier.ValueText == name);
        var result = SharpPackGenerator.AnalyzeModel(syntax, compilation, CancellationToken.None);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<TypeShape>(result.Shape);
    }
}
