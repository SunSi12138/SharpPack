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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    public void ExactBuffer_ValidationAndStorageMatchLegacy(int size)
    {
        var bytes = new byte[size];
        var oldBytes = new byte[size];
        var buffer = new SharpPackExactBufferWriter(bytes);
        var legacy = new Internal.SharpPackExactArrayBufferWriter(oldBytes);
        var empty = default(SharpPackExactBufferWriter);
        var oldEmpty = default(Internal.SharpPackExactArrayBufferWriter);
        AssertSameFailure(() => empty.Advance(-1), () => oldEmpty.Advance(-1));
        AssertSameFailure(() => empty.Advance(0), () => oldEmpty.Advance(0));
        AssertSameFailure(() => empty.GetMemory(), () => oldEmpty.GetMemory());
        AssertSameFailure(() => empty.GetSpan(), () => oldEmpty.GetSpan());
        AssertSameFailure(() => empty.GetFilledBuffer(), () => oldEmpty.GetFilledBuffer());
        AssertSameFailure(() => new SharpPackExactBufferWriter(null!),
            () => new Internal.SharpPackExactArrayBufferWriter(null!));
        foreach (var hint in new[] { -1, 0, 1, size, size + 1, int.MaxValue })
        {
            AssertSameFailure(() => buffer.GetMemory(hint), () => legacy.GetMemory(hint));
            AssertSameFailure(() => buffer.GetSpan(hint), () => legacy.GetSpan(hint));
        }
        AssertSameFailure(() => buffer.Advance(-1), () => legacy.Advance(-1));
        AssertSameFailure(() => buffer.Advance(size + 1), () => legacy.Advance(size + 1));
        AssertSameFailure(() => buffer.GetFilledBuffer(), () => legacy.GetFilledBuffer());
        buffer.GetMemory().Span.Fill(0x24);
        legacy.GetMemory().Span.Fill(0x24);
        bytes.Should().Equal(oldBytes).And.OnlyContain(value => value == 0x24);
        buffer.GetSpan().Fill(0x42);
        legacy.GetSpan().Fill(0x42);
        bytes.Should().OnlyContain(value => value == 0x42);
        buffer.Advance(size);
        legacy.Advance(size);
        buffer.GetFilledBuffer().Should().BeSameAs(bytes).And.Equal(legacy.GetFilledBuffer());
        buffer.GetMemory().Length.Should().Be(legacy.GetMemory().Length).And.Be(0);
        AssertSameFailure(() => buffer.Advance(1), () => legacy.Advance(1));
        AssertSameFailure(() => buffer.GetSpan(1), () => legacy.GetSpan(1));

        static void AssertSameFailure(Action current, Action previous)
        {
            var actual = Record.Exception(current);
            var expected = Record.Exception(previous);
            (actual?.GetType()).Should().Be(expected?.GetType());
            (actual?.Message).Should().Be(expected?.Message);
            if (expected is null) actual.Should().BeNull();
        }
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
