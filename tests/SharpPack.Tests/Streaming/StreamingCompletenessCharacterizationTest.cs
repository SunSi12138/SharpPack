using FluentAssertions;
using SharpPack.Streaming;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace SharpPack.Tests.Streaming;

public class StreamingCompletenessCharacterizationTest
{
    const int LargeTextLength = 256 * 1024;
    const int ChunkSize = 1024;
    const int BufferAtLeast = 4 * 1024;
    const int ReadMinimumSize = 8 * 1024;

    [Fact]
    public async Task Streaming_LargeVariableItem_CurrentUnframedModeIsNotAssumedComplete()
    {
        var value = new StreamingLargeVariableItem
        {
            Payload = new string('a', LargeTextLength),
        };

        await AssertLegacyUnframedFragmentationFails(value);
    }

    [Fact]
    public async Task Streaming_NestedVariableItem_CurrentUnframedModeIsNotAssumedComplete()
    {
        var value = new StreamingNestedVariableItem
        {
            Items =
            [
                new StreamingLargeVariableItem
                {
                    Payload = new string('b', LargeTextLength),
                },
                new StreamingLargeVariableItem
                {
                    Payload = new string('c', LargeTextLength / 2),
                },
            ],
        };

        await AssertLegacyUnframedFragmentationFails(value);
    }

    [Fact]
    public async Task Streaming_CustomVariableFormatter_CurrentUnframedModeIsNotAssumedComplete()
    {
        var context = new SharpPackSerializerContextBuilder()
            .Register<StreamingCustomVariableItem>(
                new StreamingCustomVariableItemFormatter())
            .Build();
        var value = new StreamingCustomVariableItem
        {
            Payload = new string('d', LargeTextLength),
        };

        await AssertLegacyUnframedFragmentationFails(value, context);
    }

    [Fact]
    public async Task Streaming_ReferenceAwareItem_CurrentUnframedModeIsNotAssumedComplete()
    {
        var value = new StreamingReferenceVariableItem
        {
            Payload = new string('e', LargeTextLength),
        };
        value.Next = value;

        await AssertLegacyUnframedFragmentationFails(value);
    }

    static async Task AssertLegacyUnframedFragmentationFails<T>(
        T value,
        SharpPackSerializerContext? context = null)
    {
        var payload = context is null
            ? SharpPackSerializer.Serialize(new[] { value })
            : SharpPackSerializer.Serialize(new[] { value }, context);

        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 16 * 1024,
            resumeWriterThreshold: 8 * 1024,
            minimumSegmentSize: ChunkSize,
            useSynchronizationContext: false));

        var producer = Task.Run(async () =>
        {
            try
            {
                for (var offset = 0; offset < payload.Length; offset += ChunkSize)
                {
                    var length = Math.Min(ChunkSize, payload.Length - offset);
                    pipe.Writer.Write(payload.AsSpan(offset, length));

                    var flush = await pipe.Writer.FlushAsync();
                    if (flush.IsCompleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await pipe.Writer.CompleteAsync();
            }
        });

        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in SharpPackStreamingSerializer.DeserializeAsync<T>(
                pipe.Reader,
                bufferAtLeast: BufferAtLeast,
                readMinimumSize: ReadMinimumSize,
                context))
            {
            }
        });

        exception.Should().BeOfType<SharpPackSerializationException>(
            "buffer occupancy does not prove that a variable-size item is complete");

        await pipe.Reader.CompleteAsync();
        await producer;
    }
}

[SharpPackable]
public partial class StreamingLargeVariableItem
{
    public string? Payload { get; set; }
}

[SharpPackable]
public partial class StreamingNestedVariableItem
{
    public List<StreamingLargeVariableItem>? Items { get; set; }
}

public sealed class StreamingCustomVariableItem
{
    public string? Payload { get; set; }
}

public sealed class StreamingCustomVariableItemFormatter
    : SharpPackFormatter<StreamingCustomVariableItem>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref StreamingCustomVariableItem? value)
    {
        writer.WriteString(value?.Payload);
    }

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref StreamingCustomVariableItem? value)
    {
        value = new StreamingCustomVariableItem
        {
            Payload = reader.ReadString(),
        };
    }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class StreamingReferenceVariableItem
{
    [SharpPackOrder(0)]
    public string? Payload { get; set; }

    [SharpPackOrder(1)]
    public StreamingReferenceVariableItem? Next { get; set; }
}
