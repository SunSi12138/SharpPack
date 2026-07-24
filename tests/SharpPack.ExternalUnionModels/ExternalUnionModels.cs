using SharpPack;

namespace SharpPack.ExternalUnionModels;

[SharpPackable(GenerateType.NoGenerate)]
public partial interface IExternalUnion;

[SharpPackable]
public partial class ExternalUnionA : IExternalUnion
{
    public int Value { get; set; }
}

[SharpPackable]
public partial class ExternalUnionB : IExternalUnion
{
    public string? Value { get; set; }
}

[SharpPackable(GenerateType.NoGenerate)]
public partial interface IExternalGenericUnion<T>
    where T : class?;

[SharpPackable]
public partial class ExternalGenericUnionA<T> : IExternalGenericUnion<T>
    where T : class?
{
    public T? Value { get; set; }
}

[SharpPackable]
public partial class ExternalGenericUnionB<T> : IExternalGenericUnion<T>
    where T : class?
{
    public T? Value { get; set; }
}
