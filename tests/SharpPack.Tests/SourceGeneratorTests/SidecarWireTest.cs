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
        CompileAndVerify("""
            namespace Vendor;

            public sealed class Request
            {
                public int Count { get; }
                public string? Name { get; init; }
                public Request(int count) => Count = count;
            }
            """, """
            [assembly: SharpPackExternalType(typeof(Vendor.Request))]

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
                    var context = SharpPack.Generated.SharpPackExternalFormatters
                        .Register(new SharpPackSerializerContextBuilder()).Build();
                    var externalBytes = SharpPackSerializer.Serialize(new Vendor.Request(7) { Name = "ready" }, context);
                    var localBytes = SharpPackSerializer.Serialize(new LocalRequest(7) { Name = "ready" });
                    var external = SharpPackSerializer.Deserialize<Vendor.Request>(localBytes, context);
                    var local = SharpPackSerializer.Deserialize<LocalRequest>(externalBytes);
                    var overrideContext = SharpPack.Generated.SharpPackExternalFormatters
                        .Register(new SharpPackSerializerContextBuilder()).Register(new ShortIntFormatter()).Build();
                    var overrideBytes = SharpPackSerializer.Serialize(new Vendor.Request(7) { Name = "ready" }, overrideContext);
                    var overrideValue = SharpPackSerializer.Deserialize<Vendor.Request>(overrideBytes, overrideContext);
                    return externalBytes.AsSpan().SequenceEqual(localBytes)
                        && external is { Count: 7, Name: "ready" }
                        && local is { Count: 7, Name: "ready" }
                        && overrideBytes.Length == externalBytes.Length - 2
                        && overrideValue is { Count: 7, Name: "ready" };
                }
            }

            internal sealed class ShortIntFormatter : SharpPackFormatter<int>
            {
                public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref int value)
                    => writer.WriteUnmanaged((short)value);
                public override void Deserialize(ref SharpPackReader reader, scoped ref int value)
                {
                    reader.ReadUnmanaged(out short encoded);
                    value = encoded;
                }
            }
            """, "Vendor.Request");
    }

    [Fact]
    public void ContextOnlyFactoryIsBoundLazilyForAnOtherwiseEmptyContext()
    {
        CompileAndVerify("""
            namespace Vendor;

            public sealed class ContextOnly(int value) : ISharpPackContextFormatterFactory<ContextOnly>
            {
                public int Value { get; } = value;
                public static int Calls { get; private set; }

                static SharpPackFormatter<ContextOnly> ISharpPackContextFormatterFactory<ContextOnly>.CreateFormatter(
                    SharpPackSerializerContext context)
                {
                    Calls++;
                    return new Codec(context.Configuration.StringEncoding == SharpPackStringEncoding.Utf16 ? 2 : 0);
                }

                private sealed class Codec(int adjustment) : SharpPackFormatter<ContextOnly>
                {
                    public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref ContextOnly? value)
                        => writer.WriteUnmanaged(value!.Value - adjustment);
                    public override void Deserialize(ref SharpPackReader reader, scoped ref ContextOnly? value)
                    {
                        reader.ReadUnmanaged(out int encoded);
                        value = new ContextOnly(encoded + adjustment);
                    }
                }
            }
            """, """
            [assembly: SharpPackExternalType(typeof(Vendor.ContextOnly))]
            public static class WireChecks
            {
                public static bool Check()
                {
                    var context = SharpPack.Generated.SharpPackExternalFormatters
                        .Register(new SharpPackSerializerContextBuilder().Configure(SharpPackSerializerConfiguration.Utf16)).Build();
                    if (Vendor.ContextOnly.Calls != 0) return false;
                    var reader = context.GetFormatter<Vendor.ContextOnly>();
                    return SharpPackSerializer.Deserialize<Vendor.ContextOnly>(new byte[] { 5, 0, 0, 0 }, context) is { Value: 7 }
                        && ReferenceEquals(reader, context.GetFormatter<Vendor.ContextOnly>())
                        && Vendor.ContextOnly.Calls == 1;
                }
            }
            """, "Vendor.ContextOnly");
    }

    [Fact]
    public void DeserializationCallsTheExplicitlySelectedInConstructor()
    {
        CompileAndVerify("""
            namespace Vendor;
            public sealed class Choice
            {
                public int Value { get; }
                public Choice(int value) => Value = -1;
                [SharpPackConstructor]
                public Choice(in int value) => Value = value;
            }
            """, """
            [assembly: SharpPackExternalType(typeof(Vendor.Choice))]
            public static class WireChecks
            {
                public static bool Check()
                {
                    var context = SharpPack.Generated.SharpPackExternalFormatters
                        .Register(new SharpPackSerializerContextBuilder()).Build();
                    return SharpPackSerializer.Deserialize<Vendor.Choice>(new byte[] { 1, 7, 0, 0, 0 }, context) is { Value: 7 };
                }
            }
            """, "Vendor.Choice");
    }

    static void CompileAndVerify(string vendorSource, string consumerSource, string typeName)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: ["NET10_0_OR_GREATER"]);
        var vendorCompilation = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText(vendorSource, parse, "Vendor.cs"))
            .WithAssemblyName("SidecarWireVendor");
        var vendor = Emit(vendorCompilation);
        var consumer = CSharpGeneratorRunner.CreateCompilation(CSharpSyntaxTree.ParseText(consumerSource, parse, "Consumer.cs"))
            .WithAssemblyName("SidecarWireConsumer")
            .AddReferences(MetadataReference.CreateFromImage(vendor));

        var target = Assert.IsAssignableFrom<INamedTypeSymbol>(consumer.GetTypeByMetadataName(typeName));
        Assert.Empty(target.DeclaringSyntaxReferences);
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
