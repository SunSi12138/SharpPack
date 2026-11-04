using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using SharpPack.Streaming;

namespace SharpPack.Tests;

public class ErrorCodeTest
{
    [Fact]
    public void LegacyConstructorsRetainUnknownAndInnerException()
    {
        var inner = new InvalidOperationException("business");
        Assert.Equal(SharpPackErrorCode.Unknown, new SharpPackSerializationException("old").ErrorCode);
        var error = new SharpPackSerializationException("old", inner);
        Assert.Equal(SharpPackErrorCode.Unknown, error.ErrorCode);
        Assert.Same(inner, error.InnerException);
    }

    [Fact]
    public void ExplicitCodeIsIndependentOfMessageAndCatchType()
    {
        var inner = new InvalidOperationException();
        foreach (var message in new[] { "old wording", "new wording" })
        {
            var error = Assert.Throws<SharpPackSerializationException>((Action)(() =>
                throw new SharpPackSerializationException(SharpPackErrorCode.InvalidEncoding, message, inner)));
            Assert.Equal(SharpPackErrorCode.InvalidEncoding, error.ErrorCode);
            Assert.Same(inner, error.InnerException);
            Assert.Equal(message, error.Message);
        }
    }

    [Fact]
    public void EnumNumericContractIsAppendOnly()
    {
        var names = new[] { "Unknown", "UnexpectedEndOfPayload", "InvalidLength", "SizeOverflow", "InvalidWireData", "InvalidEncoding", "DepthLimitExceeded", "ResourceLimitExceeded", "FormatterResolutionFailed", "TypeResolutionFailed", "FrameLengthMismatch", "RecursiveFormatterConstruction" };
        for (var i = 0; i < names.Length; i++)
            Assert.Equal(i, (int)Enum.Parse<SharpPackErrorCode>(names[i]));
    }

    [Fact]
    public void HelpersUseStableSemanticCategories()
    {
        Check(SharpPackErrorCode.InvalidLength, () => SharpPackSerializationException.ThrowInvalidLength(-2));
        Check(SharpPackErrorCode.SizeOverflow, SharpPackSerializationException.ThrowSizeOverflow);
        Check(SharpPackErrorCode.InvalidEncoding, () => SharpPackSerializationException.ThrowFailedEncoding(OperationStatus.InvalidData));
        Check(SharpPackErrorCode.InvalidEncoding, SharpPackSerializationException.ThrowInvalidEncodingLength);
        Check(SharpPackErrorCode.DepthLimitExceeded, () => SharpPackSerializationException.ThrowReachedDepthLimit(typeof(int)));
        Check(SharpPackErrorCode.ResourceLimitExceeded, () => SharpPackSerializationException.ThrowDecompressionSizeLimitExceeded(1, 2));
        Check(SharpPackErrorCode.InvalidWireData, SharpPackSerializationException.ThrowInvalidCollection);
        Check(SharpPackErrorCode.Unknown, () => SharpPackSerializationException.ThrowMessage("unclassified"));
    }

    [Fact]
    public void TruncatedCompletePayloadAndUnregisteredTypeAreClassified()
    {
        Check(SharpPackErrorCode.UnexpectedEndOfPayload, () => SharpPackSerializer.Deserialize<int>(new byte[3]));
        var context = new SharpPackSerializerContextBuilder().EnableTypeSerialization().Build();
        Check(SharpPackErrorCode.TypeResolutionFailed, () => SharpPackSerializer.Deserialize<Type>(SharpPackSerializer.Serialize("unknown"), context));
    }

    [Fact]
    public void FormatterDiscoveryAndRecursionHaveDistinctCodes()
    {
        Check(SharpPackErrorCode.FormatterResolutionFailed, () => SharpPackSerializer.Serialize(new UnsupportedModel()));
        var context = new SharpPackSerializerContext();
        Check(SharpPackErrorCode.RecursiveFormatterConstruction, () => context.GetFormatter<RecursiveContextFactoryModel>());
    }

    [Fact]
    public void StrictUtf8FailureHasEncodingCode()
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteInt32LittleEndian(payload, ~2);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), -1);
        payload[8] = 0xC3;
        payload[9] = 0x28;
        Check(SharpPackErrorCode.InvalidEncoding, () => SharpPackSerializer.Deserialize<string>(payload));
    }

    sealed class UnsupportedModel { public string? Value { get; set; } }

    [Fact]
    public void BusinessFormatterExceptionsRemainUnwrapped()
    {
        var context = new SharpPackSerializerContextBuilder().Register(new ThrowingStringFormatter()).Build();
        Assert.Throws<InvalidOperationException>(() => SharpPackSerializer.Serialize("value", context));
        Assert.Throws<InvalidOperationException>(() => SharpPackSerializer.Deserialize<string>(SharpPackSerializer.Serialize("value"), context));
    }

    [Fact]
    public void InvalidObjectReferenceIsWireData()
    {
        using var state = SharpPackReaderOptionalStatePool.Rent(null);
        Check(SharpPackErrorCode.InvalidWireData, () => state.State.GetObjectReference(42));
        state.State.AddObjectReference(0, new object());
        Check(SharpPackErrorCode.InvalidWireData, () => state.State.AddObjectReference(0, new object()));
    }

    [Theory]
    [InlineData(3, SharpPackErrorCode.UnexpectedEndOfPayload)]
    [InlineData(5, SharpPackErrorCode.FrameLengthMismatch)]
    public async Task CompleteFrameDistinguishesTruncationFromTrailingBytes(int length, SharpPackErrorCode expected)
    {
        var pipe = new Pipe();
        var frame = new byte[4 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)length);
        pipe.Writer.Write(frame);
        await pipe.Writer.CompleteAsync();
        try
        {
            var error = await Assert.ThrowsAsync<SharpPackSerializationException>(async () =>
                await SharpPackStreamingSerializer.DeserializeLengthPrefixedAsync<int>(pipe.Reader));
            Assert.Equal(expected, error.ErrorCode);
        }
        finally { await pipe.Reader.CompleteAsync(); }
    }

    [Fact]
    public async Task TransportEofIsNotWrapped()
    {
        var pipe = new Pipe();
        pipe.Writer.Write(new byte[] { 4, 0 });
        await pipe.Writer.CompleteAsync();
        try
        {
            await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                await SharpPackStreamingSerializer.DeserializeLengthPrefixedAsync<int>(pipe.Reader));
        }
        finally { await pipe.Reader.CompleteAsync(); }
    }

    static void Check(SharpPackErrorCode expected, Action action)
        => Assert.Equal(expected, Assert.Throws<SharpPackSerializationException>(action).ErrorCode);
}
