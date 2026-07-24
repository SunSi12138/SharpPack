using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;


[SharpPackable]
public partial class GenericsType<T>
{
    public int MyProperty1 { get; set; }
    public T? MyProperty2 { get; set; }
}


[SharpPackable]
public partial class MoreComplecsGenerics<T1, T2>
    where T1 : notnull
{
    public Dictionary<T1, GenericsType<T2>>? Dict { get; set; }
}

// Union

[SharpPackable]
[SharpPackUnion(0, typeof(GenricUnionA<>))]
[SharpPackUnion(1, typeof(GenricUnionB<>))]
public partial interface IGenericUnion<ToaruHoge>
{
    ToaruHoge? Value { get; set; }
}


[SharpPackable]
public partial class GenricUnionA<T> : IGenericUnion<T>
{
    public T? Value { get; set; }
    public int MyProperty { get; set; }
}

[SharpPackable]
public partial class GenricUnionB<T> : IGenericUnion<T>
{
    public T? Value { get; set; }
    public double MyProperty { get; set; }
}
