using System;
using System.Buffers;
using System.Linq;
using SharpPack.CompilerServices;

namespace SharpPack.Tests.SourceGeneratorTests;

public class CompilerServicesMigrationTest
{
    [Fact]
    public void ExactBuffer_ForwardsValidationAndProducesIdenticalBytes()
    {
        var bytes = new byte[5];
        var buffer = new SharpPackExactBufferWriter(bytes);
        var writer = SharpPackExactBufferWriter.CreateWriter(ref buffer);
        writer.WriteObjectHeader(1);
        writer.WriteUnmanaged(1234);
        writer.Flush();
        buffer.GetFilledBuffer().Should().BeSameAs(bytes);

        var legacyBytes = new byte[5];
        var legacy = new Internal.SharpPackExactArrayBufferWriter(legacyBytes);
        var legacyWriter = SharpPackSerializer.CreateExactWriter(ref legacy);
        legacyWriter.WriteObjectHeader(1);
        legacyWriter.WriteUnmanaged(1234);
        legacyWriter.Flush();
        bytes.Should().Equal(legacy.GetFilledBuffer());

        Action overrun = () => buffer.Advance(1);
        overrun.Should().Throw<SharpPackSerializationException>();
        Action negative = () => buffer.GetMemory(-1);
        negative.Should().Throw<SharpPackSerializationException>();
        Action underfilled = () => new SharpPackExactBufferWriter(new byte[1]).GetFilledBuffer();
        underfilled.Should().Throw<SharpPackSerializationException>();
    }

    [Fact]
    public void PooledBuffer_PreservesLeaseChecksAndReset()
    {
        using var state = SharpPackWriterOptionalStatePool.Rent();
        var buffer = SharpPackPooledBufferWriter.Rent(out var lease);
        var output = new ArrayBufferWriter<byte>();
        try
        {
            var temporary = new SharpPackWriter<SharpPackPooledBufferWriter>(
                ref buffer, state.State);
            temporary.WriteUnmanaged(4321);
            temporary.Flush();
            var writer = new SharpPackWriter<ArrayBufferWriter<byte>>(
                ref output, state.State);
            buffer.WriteToAndReset(ref writer);
            writer.Flush();
            output.WrittenSpan.ToArray().Should().Equal(BitConverter.GetBytes(4321));
            output.Clear();
            buffer.WriteToAndReset(ref writer);
            writer.Flush();
            output.WrittenCount.Should().Be(0);
        }
        finally
        {
            SharpPackPooledBufferWriter.Return(buffer, lease);
        }
        Action twice = () => SharpPackPooledBufferWriter.Return(buffer, lease);
        twice.Should().Throw<InvalidOperationException>();

        var next = SharpPackPooledBufferWriter.Rent(out var nextLease);
        SharpPackPooledBufferWriter.Return(next, nextLease);
    }

    [Fact]
    public void NewGeneratedCode_DoesNotNameInternalPlumbing()
    {
        var (compilation, diagnostics) = CSharpGeneratorRunner.RunGenerator("""
            [SharpPackable]
            public partial class Exact { public int Number { get; set; } public string? Text { get; set; } }
            [SharpPackable(GenerateType.VersionTolerant)]
            public partial class Pooled { [SharpPackOrder(0)] public Exact? Value { get; set; } }
            """);
        diagnostics.Should().BeEmpty();
        compilation.GetDiagnostics().Where(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var source = string.Join("\n", compilation.SyntaxTrees.Where(x => x.FilePath.EndsWith(".g.cs")));
        source.Should().Contain("SharpPack.CompilerServices.SharpPackExactBufferWriter")
            .And.Contain("SharpPack.CompilerServices.SharpPackPooledBufferWriter")
            .And.NotContain("SharpPack.Internal.");
    }
}
