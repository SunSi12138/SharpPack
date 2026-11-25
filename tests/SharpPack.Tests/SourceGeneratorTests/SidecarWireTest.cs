using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack.Generator;
using SharpPack.Tests.Utils;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SharpPack.Tests.SourceGeneratorTests;

public class SidecarWireTest
{
    [Fact]
    public void StandaloneMetadataFormatterSharesTheGeneratedObjectWire()
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: ["NET10_0_OR_GREATER"]);
        var vendorCompilation = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText("""
            namespace Vendor;

            public sealed class Request
            {
                public int Count { get; }
                public string? Name { get; init; }
                public Request(int count) => Count = count;
            }
            """, parse, "Vendor.cs")).WithAssemblyName("SidecarWireVendor");
        var vendor = Emit(vendorCompilation);

        var consumer = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText("""
            [SharpPackable]
            public partial class LocalRequest
            {
                public int Count { get; }
                public string? Name { get; init; }
                public LocalRequest(int count) => Count = count;
            }

            public static class WireChecks
            {
                public static bool Check()
                {
                    var context = new SharpPackSerializerContextBuilder()
                        .Register(new SharpPack.Generated.RequestSidecar())
                        .Build();
                    var externalBytes = SharpPackSerializer.Serialize(new Vendor.Request(7) { Name = "ready" }, context);
                    var localBytes = SharpPackSerializer.Serialize(new LocalRequest(7) { Name = "ready" });
                    var external = SharpPackSerializer.Deserialize<Vendor.Request>(localBytes, context);
                    var local = SharpPackSerializer.Deserialize<LocalRequest>(externalBytes);
                    return externalBytes.AsSpan().SequenceEqual(localBytes)
                        && external is { Count: 7, Name: "ready" }
                        && local is { Count: 7, Name: "ready" };
                }
            }
            """, parse, "Consumer.cs")).WithAssemblyName("SidecarWireConsumer")
            .AddReferences(MetadataReference.CreateFromImage(vendor));

        var target = Assert.IsAssignableFrom<INamedTypeSymbol>(consumer.GetTypeByMetadataName("Vendor.Request"));
        Assert.Empty(target.DeclaringSyntaxReferences);
        var references = new ReferenceSymbols(consumer);
        var shape = TypeShape.Create(target, new TypeMeta(target, references), references) with
        {
            GenerateType = Generator.GenerateType.Object,
        };
        var formatter = CSharpEmitter.EmitObjectSidecar(SerializationPlanner.Plan(shape), "RequestSidecar");
        consumer = consumer.AddSyntaxTrees(CSharpSyntaxTree.ParseText(formatter.Text, parse, formatter.HintName));
        CSharpGeneratorDriver.Create([new SharpPackGenerator().AsSourceGenerator()], parseOptions: parse)
            .RunGeneratorsAndUpdateCompilation(consumer, out var generated, out var diagnostics);
        Assert.Empty(diagnostics);

        var loadContext = new AssemblyLoadContext(nameof(SidecarWireTest), isCollectible: true);
        try
        {
            using var vendorStream = new MemoryStream(vendor);
            loadContext.LoadFromStream(vendorStream);
            using var generatedStream = new MemoryStream(Emit(generated));
            var run = loadContext.LoadFromStream(generatedStream).GetType("WireChecks")!
                .GetMethod("Check", BindingFlags.Static | BindingFlags.Public)!;
            Assert.True(Assert.IsType<bool>(run.Invoke(null, null)));
        }
        finally
        {
            loadContext.Unload();
        }

        static byte[] Emit(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return stream.ToArray();
        }
    }
}
