using SharpPack;
var expected = new PackageModel { Id = 42, Name = "nupkg-only" };
var bytes = SharpPackSerializer.Serialize(expected);
var actual = SharpPackSerializer.Deserialize<PackageModel>(bytes);
if (actual?.Id != expected.Id || actual.Name != expected.Name) throw new Exception("Roundtrip failed");
#if STREAMING
var pipe = new System.IO.Pipelines.Pipe();
await SharpPack.Streaming.SharpPackStreamingSerializer.SerializeLengthPrefixedAsync(pipe.Writer, expected);
await pipe.Writer.CompleteAsync();
var streamed = await SharpPack.Streaming.SharpPackStreamingSerializer.DeserializeLengthPrefixedAsync<PackageModel>(pipe.Reader);
await pipe.Reader.CompleteAsync();
if (streamed?.Id != expected.Id || streamed.Name != expected.Name) throw new Exception("Streaming failed");
#endif
var token = typeof(SharpPackSerializer).Assembly.GetName().GetPublicKeyToken();
if (token is null || token.Length == 0) throw new Exception("Core must retain its strong name");
Console.WriteLine("Package consumer roundtrip passed");
