using System.Runtime.InteropServices;
using SharpPack;

if (RuntimeInformation.ProcessArchitecture.ToString() != args[0])
    throw new Exception("Unexpected consumer process architecture");

var expected = new PackageModel { Id = 42, Name = "nupkg-only" };
AssertModel(SharpPackSerializer.Deserialize<PackageModel>(SharpPackSerializer.Serialize(expected)));
var wrapped = new Envelope { Payload = expected };
AssertModel(SharpPackSerializer.Deserialize<Envelope>(SharpPackSerializer.Serialize(wrapped))?.Payload);

#if STREAMING
var pipe = new System.IO.Pipelines.Pipe();
await SharpPack.Streaming.SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(pipe.Writer, expected);
await pipe.Writer.CompleteAsync();
AssertModel(await SharpPack.Streaming.SharpPackStreamingSerializer.DeserializeLengthPrefixedAsync<PackageModel>(pipe.Reader));
await pipe.Reader.CompleteAsync();
#endif

if (typeof(SharpPackSerializer).Assembly.GetName().GetPublicKeyToken() is not { Length: > 0 })
    throw new Exception("The packaged Core assembly must retain its strong name");

Console.WriteLine("Package consumer passed");

void AssertModel(PackageModel? model)
{
    if (model?.Id != expected.Id || model.Name != expected.Name)
        throw new Exception("Package consumer round trip failed");
}
