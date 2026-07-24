using MemoryPack;

namespace Benchmark.Benchmarks;

[MemoryDiagnoser]
public class FormatterArchitectureBenchmark
{
    readonly FormatterBenchmarkDto simple = new()
    {
        Id = 42,
        Name = "MemoryPack",
    };

    readonly FormatterBenchmarkGraph graph = new()
    {
        Id = 42,
        Name = "MemoryPack",
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
        stringBytes = MemoryPackSerializer.Serialize("MemoryPack formatter architecture benchmark");
        simpleBytes = MemoryPackSerializer.Serialize(simple);
        graphBytes = MemoryPackSerializer.Serialize(graph);
        primitiveArrayBytes = MemoryPackSerializer.Serialize(primitiveArray);
        listBytes = MemoryPackSerializer.Serialize(list);
        dictionaryBytes = MemoryPackSerializer.Serialize(dictionary);
        unionBytes = MemoryPackSerializer.Serialize<FormatterBenchmarkUnion>(union);
        circularBytes = MemoryPackSerializer.Serialize(circular);

        _ = MemoryPackSerializer.Deserialize<string>(stringBytes);
        _ = MemoryPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes);
        _ = MemoryPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes);
        _ = MemoryPackSerializer.Deserialize<int[]>(primitiveArrayBytes);
        _ = MemoryPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes);
        _ = MemoryPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes);
        _ = MemoryPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes);
        _ = MemoryPackSerializer.Deserialize<FormatterBenchmarkCircular>(circularBytes);
    }

    [Benchmark]
    public byte[] SerializePrimitive() => MemoryPackSerializer.Serialize(42);

    [Benchmark]
    public int DeserializePrimitive() => MemoryPackSerializer.Deserialize<int>(new byte[] { 42, 0, 0, 0 });

    [Benchmark]
    public byte[] SerializeString() => MemoryPackSerializer.Serialize("MemoryPack formatter architecture benchmark");

    [Benchmark]
    public string? DeserializeString() => MemoryPackSerializer.Deserialize<string>(stringBytes);

    [Benchmark]
    public byte[] SerializeSimple() => MemoryPackSerializer.Serialize(simple);

    [Benchmark]
    public FormatterBenchmarkDto? DeserializeSimple() => MemoryPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes);

    [Benchmark]
    public byte[] SerializeGraph() => MemoryPackSerializer.Serialize(graph);

    [Benchmark]
    public FormatterBenchmarkGraph? DeserializeGraph() => MemoryPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes);

    [Benchmark]
    public byte[] SerializeArray() => MemoryPackSerializer.Serialize(primitiveArray);

    [Benchmark]
    public int[]? DeserializeArray() => MemoryPackSerializer.Deserialize<int[]>(primitiveArrayBytes);

    [Benchmark]
    public byte[] SerializeList() => MemoryPackSerializer.Serialize(list);

    [Benchmark]
    public List<FormatterBenchmarkChild>? DeserializeList() => MemoryPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes);

    [Benchmark]
    public byte[] SerializeDictionary() => MemoryPackSerializer.Serialize(dictionary);

    [Benchmark]
    public Dictionary<int, FormatterBenchmarkChild>? DeserializeDictionary()
        => MemoryPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes);

    [Benchmark]
    public byte[] SerializeUnion() => MemoryPackSerializer.Serialize<FormatterBenchmarkUnion>(union);

    [Benchmark]
    public FormatterBenchmarkUnion? DeserializeUnion()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes);

    [Benchmark]
    public byte[] SerializeCircularReference()
        => MemoryPackSerializer.Serialize(circular);

    [Benchmark]
    public FormatterBenchmarkCircular? DeserializeCircularReference()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkCircular>(
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
    readonly MemoryPackSerializerContext context = new();
    readonly FormatterBenchmarkDto simple = new() { Id = 42, Name = "MemoryPack" };
    readonly FormatterBenchmarkGraph graph = new()
    {
        Id = 42,
        Name = "MemoryPack",
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
        stringBytes = MemoryPackSerializer.Serialize("MemoryPack formatter architecture benchmark", context);
        simpleBytes = MemoryPackSerializer.Serialize(simple, context);
        graphBytes = MemoryPackSerializer.Serialize(graph, context);
        listBytes = MemoryPackSerializer.Serialize(list, context);
        dictionaryBytes = MemoryPackSerializer.Serialize(dictionary, context);
        unionBytes = MemoryPackSerializer.Serialize<FormatterBenchmarkUnion>(union, context);
        circularBytes = MemoryPackSerializer.Serialize(circular, context);
    }

    [Benchmark]
    public byte[] SerializePrimitive() => MemoryPackSerializer.Serialize(42, context);

    [Benchmark]
    public int DeserializePrimitive() => MemoryPackSerializer.Deserialize<int>(new byte[] { 42, 0, 0, 0 }, context);

    [Benchmark]
    public byte[] SerializeString()
        => MemoryPackSerializer.Serialize("MemoryPack formatter architecture benchmark", context);

    [Benchmark]
    public string? DeserializeString() => MemoryPackSerializer.Deserialize<string>(stringBytes, context);

    [Benchmark]
    public byte[] SerializeSimple() => MemoryPackSerializer.Serialize(simple, context);

    [Benchmark]
    public FormatterBenchmarkDto? DeserializeSimple()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkDto>(simpleBytes, context);

    [Benchmark]
    public byte[] SerializeGraph() => MemoryPackSerializer.Serialize(graph, context);

    [Benchmark]
    public FormatterBenchmarkGraph? DeserializeGraph()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkGraph>(graphBytes, context);

    [Benchmark]
    public byte[] SerializeList() => MemoryPackSerializer.Serialize(list, context);

    [Benchmark]
    public List<FormatterBenchmarkChild>? DeserializeList()
        => MemoryPackSerializer.Deserialize<List<FormatterBenchmarkChild>>(listBytes, context);

    [Benchmark]
    public byte[] SerializeDictionary() => MemoryPackSerializer.Serialize(dictionary, context);

    [Benchmark]
    public Dictionary<int, FormatterBenchmarkChild>? DeserializeDictionary()
        => MemoryPackSerializer.Deserialize<Dictionary<int, FormatterBenchmarkChild>>(dictionaryBytes, context);

    [Benchmark]
    public byte[] SerializeUnion() => MemoryPackSerializer.Serialize<FormatterBenchmarkUnion>(union, context);

    [Benchmark]
    public FormatterBenchmarkUnion? DeserializeUnion()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkUnion>(unionBytes, context);

    [Benchmark]
    public byte[] SerializeCircularReference()
        => MemoryPackSerializer.Serialize(circular, context);

    [Benchmark]
    public FormatterBenchmarkCircular? DeserializeCircularReference()
        => MemoryPackSerializer.Deserialize<FormatterBenchmarkCircular>(
            circularBytes,
            context);

    [Benchmark]
    public MemoryPackFormatter<FormatterBenchmarkGraph> ColdCreateAndResolveGraph()
        => new MemoryPackSerializerContext().GetFormatter<FormatterBenchmarkGraph>();

    [Benchmark]
    public byte[] ColdCreateAndSerializeSimple()
        => MemoryPackSerializer.Serialize(simple, new MemoryPackSerializerContext());

    static FormatterBenchmarkCircular CreateCircular()
    {
        var value = new FormatterBenchmarkCircular { Value = 42 };
        value.Self = value;
        return value;
    }
}

[MemoryPackable]
public partial class FormatterBenchmarkDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

[MemoryPackable]
public partial class FormatterBenchmarkGraph
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public FormatterBenchmarkChild? Child { get; set; }
    public FormatterBenchmarkChild[]? Children { get; set; }
    public List<FormatterBenchmarkChild>? ChildList { get; set; }
    public Dictionary<int, FormatterBenchmarkChild>? ChildMap { get; set; }
}

[MemoryPackable]
public partial class FormatterBenchmarkChild
{
    public int Value { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(0, typeof(FormatterBenchmarkUnionValue))]
public partial interface FormatterBenchmarkUnion
{
}

[MemoryPackable]
public partial class FormatterBenchmarkUnionValue : FormatterBenchmarkUnion
{
    public int Value { get; set; }
}

[MemoryPackable(GenerateType.CircularReference)]
public partial class FormatterBenchmarkCircular
{
    [MemoryPackOrder(0)]
    public int Value { get; set; }

    [MemoryPackOrder(1)]
    public FormatterBenchmarkCircular? Self { get; set; }
}
