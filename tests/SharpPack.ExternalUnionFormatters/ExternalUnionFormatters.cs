using SharpPack;
using SharpPack.ExternalUnionModels;

namespace SharpPack.ExternalUnionFormatters;

[SharpPackUnionFormatter(typeof(IExternalUnion))]
[SharpPackUnion(11, typeof(ExternalUnionA))]
[SharpPackUnion(12, typeof(ExternalUnionB))]
public partial class ExternalUnionFormatter;

[SharpPackUnionFormatter(typeof(IExternalGenericUnion<>))]
[SharpPackUnion(21, typeof(ExternalGenericUnionA<>))]
[SharpPackUnion(22, typeof(ExternalGenericUnionB<>))]
public partial class ExternalGenericUnionFormatter<T>
    where T : class?;
