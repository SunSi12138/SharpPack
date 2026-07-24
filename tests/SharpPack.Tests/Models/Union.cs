using Microsoft.CodeAnalysis.Operations;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;


[SharpPackable]
public partial class StandardBase
{
    public int MyProperty1 { get; set; }
    public int MyProperty2 { get; set; }
}

[SharpPackable]
public partial class Derived1 : StandardBase
{
    public int DerivedProp1 { get; set; }
    public int DerivedProp2 { get; set; }
}

[SharpPackable]
public partial class Derived2 : Derived1
{
    public int Derived2Prop1 { get; set; }
    public int Derived2Prop2 { get; set; }
}


[SharpPackable]
[SharpPackUnion(0, typeof(Impl1))]
[SharpPackUnion(253, typeof(Impl2))]
public partial interface IUnionInterface
{
    int MyProperty { get; }
}

[SharpPackable]
public partial class Impl1 : IUnionInterface
{
    public int MyProperty { get; set; }
    public long Foo { get; set; }
}

[SharpPackable]
public partial class Impl2 : IUnionInterface
{
    public int MyProperty { get; set; }
    public string? Bar { get; set; }
}

[SharpPackable]
[SharpPackUnion(0, typeof(ImplA1))]
[SharpPackUnion(1, typeof(ImplA2))]
public abstract partial class UnionAbstractClass
{
    public virtual int MyProperty { get; set; }
}

[SharpPackable]
public partial class ImplA1 : UnionAbstractClass
{
    public override int MyProperty { get; set; }
    public long Foo { get; set; }
}

[SharpPackable]
public partial class ImplA2 : UnionAbstractClass
{
    public override int MyProperty { get; set; }
    public string? Bar { get; set; }
}


[SharpPackable(GenerateType.NoGenerate)]
public partial interface IForExternalUnion
{
    public int BaseValue { get; set; }
}

[SharpPackable]
public partial class AForOne : IForExternalUnion
{
    public int BaseValue { get; set; }
    public int MyProperty { get; set; }
}

[SharpPackable]
public partial class AForTwo : IForExternalUnion
{
    public int BaseValue { get; set; }
    public int MyProperty { get; set; }
}

[SharpPackUnionFormatter(typeof(IForExternalUnion))]
[SharpPackUnion(0, typeof(AForOne))]
[SharpPackUnion(1, typeof(AForTwo))]
public partial class ForExternalUnionFormatter
{
}


[SharpPackable(GenerateType.NoGenerate)]
public partial interface IGenericsUnion<T>
{
    public T? NoValue { get; set; }
}

[SharpPackable]
public partial class BForOne<T> : IGenericsUnion<T>
{
    public T? NoValue { get; set; }
    public int MyProperty { get; set; }
}

[SharpPackable]
public partial class BForTwo<T> : IGenericsUnion<T>
{
    public T? NoValue { get; set; }
    public int MyProperty { get; set; }
}

[SharpPackUnionFormatter(typeof(IGenericsUnion<>))]
[SharpPackUnion(0, typeof(BForOne<>))]
[SharpPackUnion(1, typeof(BForTwo<>))]
public partial class ForExternalUnionFormatter2<T>
{
}

[SharpPackUnionFormatter(typeof(IGenericsUnion<string>))]
[SharpPackUnion(10, typeof(BForOne<string>))]
[SharpPackUnion(11, typeof(BForTwo<string>))]
public partial class ForExternalUnionFormatter3
{
}

[SharpPackable(GenerateType.NoGenerate)]
public partial interface IClosedGenericsUnion<T>
{
    public T? Value { get; set; }
}

[SharpPackable]
public partial class ClosedGenericsUnionValue<T> : IClosedGenericsUnion<T>
{
    public T? Value { get; set; }
}

[SharpPackUnionFormatter(typeof(IClosedGenericsUnion<string>))]
[SharpPackUnion(7, typeof(ClosedGenericsUnionValue<string>))]
public partial class ClosedGenericsUnionFormatter
{
}


[SharpPackable]
public partial class NoraType
{
    public IForExternalUnion? ExtUnion { get; set; }
    public UnionAbstractClass? AbstractUnion { get; set; }
}

// Union for record
// https://github.com/Cysharp/MemoryPack/issues/86

[SharpPackable(SerializeLayout.Explicit)]
public sealed partial record ChargingBookSubmittedEvent
    ([property: SharpPackOrder(1)] string ChargingPlatform, [property: SharpPackOrder(2)] decimal Amount) : AbstractAuditEvent;

[SharpPackUnion(0, typeof(ChargingBookSubmittedEvent))]
[SharpPackable(SerializeLayout.Explicit)]
public abstract partial record AbstractAuditEvent
{
    [SharpPackOrder(0)]
    public DateTimeOffset EventDate { get; init; }
}
