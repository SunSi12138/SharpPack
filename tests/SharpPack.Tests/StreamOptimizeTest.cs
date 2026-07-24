using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests;

public class StreamOptimizeTest
{
    [Fact]
    public async Task LengthDelimitedDeserializeDoesNotConsumeFollowingMessage()
    {
        var first = SharpPackSerializer.Serialize(new[] { 1, 2, 3 });
        var second = SharpPackSerializer.Serialize(new[] { 4, 5, 6 });
        var bytes = first.Concat(second).ToArray();
        var context = new SharpPackSerializerContext();

        foreach (var useContext in new[] { false, true })
        {
            using var backing = new MemoryStream(bytes);
            using Stream stream = useContext
                ? new BufferedStream(backing, 8)
                : backing;

            var firstValue = useContext
                ? await SharpPackSerializer.DeserializeAsync<int[]>(
                    stream,
                    first.Length,
                    context)
                : await SharpPackSerializer.DeserializeAsync<int[]>(
                    stream,
                    first.Length);
            var secondValue = useContext
                ? await SharpPackSerializer.DeserializeAsync<int[]>(
                    stream,
                    second.Length,
                    context)
                : await SharpPackSerializer.DeserializeAsync<int[]>(
                    stream,
                    second.Length);

            firstValue.Should().Equal(1, 2, 3);
            secondValue.Should().Equal(4, 5, 6);
        }
    }

    [Fact]
    public async Task MemoryStream()
    {
        var ms = new MemoryStream();
        await SharpPackSerializer.SerializeAsync(ms, new[] { 1, 2, 3 });
        var offset = ms.Position;
        await SharpPackSerializer.SerializeAsync(ms, new[] { 10, 20, 30 });
        await SharpPackSerializer.SerializeAsync(ms, new[] { 40, 50, 60 });

        ms.Position = offset;

        var data1 = await SharpPackSerializer.DeserializeAsync<int[]>(ms);
        var data2 = await SharpPackSerializer.DeserializeAsync<int[]>(ms);

        data1.Should().Equal(10, 20, 30);
        data2.Should().Equal(40, 50, 60);
    }

}
