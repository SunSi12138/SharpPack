using MemoryPack;
using MemoryPack.ExternalUnionModels;

namespace MemoryPack.ExternalUnionFormatters;

[MemoryPackUnionFormatter(typeof(IExternalUnion))]
[MemoryPackUnion(11, typeof(ExternalUnionA))]
[MemoryPackUnion(12, typeof(ExternalUnionB))]
public partial class ExternalUnionFormatter;

[MemoryPackUnionFormatter(typeof(IExternalGenericUnion<>))]
[MemoryPackUnion(21, typeof(ExternalGenericUnionA<>))]
[MemoryPackUnion(22, typeof(ExternalGenericUnionB<>))]
public partial class ExternalGenericUnionFormatter<T>
    where T : class?;
