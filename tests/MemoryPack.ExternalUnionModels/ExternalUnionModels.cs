using MemoryPack;

namespace MemoryPack.ExternalUnionModels;

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IExternalUnion;

[MemoryPackable]
public partial class ExternalUnionA : IExternalUnion
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class ExternalUnionB : IExternalUnion
{
    public string? Value { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IExternalGenericUnion<T>
    where T : class?;

[MemoryPackable]
public partial class ExternalGenericUnionA<T> : IExternalGenericUnion<T>
    where T : class?
{
    public T? Value { get; set; }
}

[MemoryPackable]
public partial class ExternalGenericUnionB<T> : IExternalGenericUnion<T>
    where T : class?
{
    public T? Value { get; set; }
}
