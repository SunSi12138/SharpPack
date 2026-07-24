using SharpPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class FormatterArchitectureBenchmark
{
    readonly FormatterBenchmarkDto simple = new()
    {
        Id = 42,
        Name = "SharpPack",
    };

    readonly FormatterBenchmarkGraph graph = new()
    {
        Id = 42,
        Name = "SharpPack",
        Child = new FormatterBenchmarkChild { Value = 10 },
        Children = Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToArray(),
        ChildList = Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToList(),
        ChildMap = Enumerable.Range(0, 32).ToDictionary(static x => x, static x => new FormatterBenchmarkChild { Value = x }),
    };

    readonly int[] primitiveArray = Enumerable.Range(0, 128).ToArray();
    readonly List<FormatterBenchmarkChild> list = Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToList();
    readonly Dictionary<int, FormatterBenchmarkChild> dictionary = Enumerable.Range(0, 32).ToDictionary(static x => x, static x => new FormatterBenchmarkChild { Value = x });
    readonly FormatterBenchmarkUnion union = new FormatterBenchmarkUnionValue { Value = 42 };
    readonly FormatterBenchmarkCircular circular = CreateCircular();

    byte[] stringBytes = null!;
    byte[] simpleBytes = null!;
    byte[] graphBytes = null!;
    byte[] primitiveArrayBytes = null!;
    byte[] listBytes = null!;
    byte[] dictionaryBytes = null!;
    byte[] unionBytes = null!;
    byte[] circularBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        stringBytes = SharpPackSerializer.Serialize("SharpPack formatter architecture benchmark");
        simpleBytes = SharpPackSerializer.Serialize(simple);
        graphBytes = SharpPackSerializer.Serialize(graph);
        primitiveArrayBytes = SharpPackSerializer.Serialize(primitiveArray);
        listBytes = SharpPackSerializer.Serialize(list);
        dictionaryBytes = SharpPackSerializer.Serialize(dictionary);
        unionBytes = SharpPackSerializer.Serialize<FormatterBenchmarkUnion>(union);
        circularBytes = SharpPackSerializer.Serialize(circular);

        _ = SharpPackSerializer.Deserialize<string>(stringBytes);
        _ = SharpPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes);
        _ = SharpPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes);
        _ = SharpPackSerializer.Deserialize<int[]>(primitiveArrayBytes);
        _ = SharpPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes);
        _ = SharpPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes);
        _ = SharpPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes);
        _ = SharpPackSerializer.Deserialize<FormatterBenchmarkCircular>(circularBytes);
    }

    [Benchmark]
    public byte[] SerializePrimitive() => SharpPackSerializer.Serialize(42);

    [Benchmark]
    public int DeserializePrimitive() => SharpPackSerializer.Deserialize<int>(new byte[] { 42, 0, 0, 0 });

    [Benchmark]
    public byte[] SerializeString() => SharpPackSerializer.Serialize("SharpPack formatter architecture benchmark");

    [Benchmark]
    public string? DeserializeString() => SharpPackSerializer.Deserialize<string>(stringBytes);

    [Benchmark]
    public byte[] SerializeSimple() => SharpPackSerializer.Serialize(simple);

    [Benchmark]
    public FormatterBenchmarkDto? DeserializeSimple() => SharpPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes);

    [Benchmark]
    public byte[] SerializeGraph() => SharpPackSerializer.Serialize(graph);

    [Benchmark]
    public FormatterBenchmarkGraph? DeserializeGraph() => SharpPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes);

    [Benchmark]
    public byte[] SerializeArray() => SharpPackSerializer.Serialize(primitiveArray);

    [Benchmark]
    public int[]? DeserializeArray() => SharpPackSerializer.Deserialize<int[]>(primitiveArrayBytes);

    [Benchmark]
    public byte[] SerializeList() => SharpPackSerializer.Serialize(list);

    [Benchmark]
    public List<FormatterBenchmarkChild>? DeserializeList() => SharpPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes);

    [Benchmark]
    public byte[] SerializeDictionary() => SharpPackSerializer.Serialize(dictionary);

    [Benchmark]
    public Dictionary<int, FormatterBenchmarkChild>? DeserializeDictionary()
        => SharpPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes);

    [Benchmark]
    public byte[] SerializeUnion() => SharpPackSerializer.Serialize<FormatterBenchmarkUnion>(union);

    [Benchmark]
    public FormatterBenchmarkUnion? DeserializeUnion()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes);

    [Benchmark]
    public byte[] SerializeCircularReference()
        => SharpPackSerializer.Serialize(circular);

    [Benchmark]
    public FormatterBenchmarkCircular? DeserializeCircularReference()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkCircular>(
            circularBytes);

    static FormatterBenchmarkCircular CreateCircular()
    {
        var value = new FormatterBenchmarkCircular { Value = 42 };
        value.Self = value;
        return value;
    }
}

[MemoryDiagnoser]
public class FormatterContextBenchmark
{
    readonly SharpPackSerializerContext context = new();
    readonly FormatterBenchmarkDto simple = new() { Id = 42, Name = "SharpPack" };
    readonly FormatterBenchmarkGraph graph = new()
    {
        Id = 42,
        Name = "SharpPack",
        Child = new FormatterBenchmarkChild { Value = 10 },
        Children = Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToArray(),
        ChildList = Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToList(),
        ChildMap = Enumerable.Range(0, 32).ToDictionary(static x => x, static x => new FormatterBenchmarkChild { Value = x }),
    };
    readonly List<FormatterBenchmarkChild> list =
        Enumerable.Range(0, 32).Select(static x => new FormatterBenchmarkChild { Value = x }).ToList();
    readonly Dictionary<int, FormatterBenchmarkChild> dictionary =
        Enumerable.Range(0, 32).ToDictionary(static x => x, static x => new FormatterBenchmarkChild { Value = x });
    readonly FormatterBenchmarkUnion union = new FormatterBenchmarkUnionValue { Value = 42 };
    readonly FormatterBenchmarkCircular circular = CreateCircular();

    byte[] stringBytes = null!;
    byte[] simpleBytes = null!;
    byte[] graphBytes = null!;
    byte[] listBytes = null!;
    byte[] dictionaryBytes = null!;
    byte[] unionBytes = null!;
    byte[] circularBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        stringBytes = SharpPackSerializer.Serialize("SharpPack formatter architecture benchmark", context);
        simpleBytes = SharpPackSerializer.Serialize(simple, context);
        graphBytes = SharpPackSerializer.Serialize(graph, context);
        listBytes = SharpPackSerializer.Serialize(list, context);
        dictionaryBytes = SharpPackSerializer.Serialize(dictionary, context);
        unionBytes = SharpPackSerializer.Serialize<FormatterBenchmarkUnion>(union, context);
        circularBytes = SharpPackSerializer.Serialize(circular, context);
    }

    [Benchmark]
    public byte[] SerializePrimitive() => SharpPackSerializer.Serialize(42, context);

    [Benchmark]
    public int DeserializePrimitive() => SharpPackSerializer.Deserialize<int>(new byte[] { 42, 0, 0, 0 }, context);

    [Benchmark]
    public byte[] SerializeString()
        => SharpPackSerializer.Serialize("SharpPack formatter architecture benchmark", context);

    [Benchmark]
    public string? DeserializeString() => SharpPackSerializer.Deserialize<string>(stringBytes, context);

    [Benchmark]
    public byte[] SerializeSimple() => SharpPackSerializer.Serialize(simple, context);

    [Benchmark]
    public FormatterBenchmarkDto? DeserializeSimple()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes, context);

    [Benchmark]
    public byte[] SerializeGraph() => SharpPackSerializer.Serialize(graph, context);

    [Benchmark]
    public FormatterBenchmarkGraph? DeserializeGraph()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes, context);

    [Benchmark]
    public byte[] SerializeList() => SharpPackSerializer.Serialize(list, context);

    [Benchmark]
    public List<FormatterBenchmarkChild>? DeserializeList()
        => SharpPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes, context);

    [Benchmark]
    public byte[] SerializeDictionary() => SharpPackSerializer.Serialize(dictionary, context);

    [Benchmark]
    public Dictionary<int, FormatterBenchmarkChild>? DeserializeDictionary()
        => SharpPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes, context);

    [Benchmark]
    public byte[] SerializeUnion() => SharpPackSerializer.Serialize<FormatterBenchmarkUnion>(union, context);

    [Benchmark]
    public FormatterBenchmarkUnion? DeserializeUnion()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes, context);

    [Benchmark]
    public byte[] SerializeCircularReference()
        => SharpPackSerializer.Serialize(circular, context);

    [Benchmark]
    public FormatterBenchmarkCircular? DeserializeCircularReference()
        => SharpPackSerializer.Deserialize<FormatterBenchmarkCircular>(
            circularBytes,
            context);

    [Benchmark]
    public SharpPackFormatter<FormatterBenchmarkGraph> ColdCreateAndResolveGraph()
        => new SharpPackSerializerContext().GetFormatter<FormatterBenchmarkGraph>();

    [Benchmark]
    public byte[] ColdCreateAndSerializeSimple()
        => SharpPackSerializer.Serialize(simple, new SharpPackSerializerContext());

    static FormatterBenchmarkCircular CreateCircular()
    {
        var value = new FormatterBenchmarkCircular { Value = 42 };
        value.Self = value;
        return value;
    }
}

[SharpPackable]
public partial class FormatterBenchmarkDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

[SharpPackable]
public partial class FormatterBenchmarkGraph
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public FormatterBenchmarkChild? Child { get; set; }
    public FormatterBenchmarkChild[]? Children { get; set; }
    public List<FormatterBenchmarkChild>? ChildList { get; set; }
    public Dictionary<int, FormatterBenchmarkChild>? ChildMap { get; set; }
}

[SharpPackable]
public partial class FormatterBenchmarkChild
{
    public int Value { get; set; }
}

[SharpPackable]
[SharpPackUnion(0, typeof(FormatterBenchmarkUnionValue))]
public partial interface FormatterBenchmarkUnion
{
}

[SharpPackable]
public partial class FormatterBenchmarkUnionValue : FormatterBenchmarkUnion
{
    public int Value { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class FormatterBenchmarkCircular
{
    [SharpPackOrder(0)]
    public int Value { get; set; }

    [SharpPackOrder(1)]
    public FormatterBenchmarkCircular? Self { get; set; }
}
