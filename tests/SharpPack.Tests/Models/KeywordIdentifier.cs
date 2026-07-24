using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpPack.Tests.Models;


// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/verbatim

[SharpPackable]
public partial class KeywordModel
{
    public int @int;
    public long @long;
    public string? @string { get; set; }

    public Version2? @for;

    [SharpPackConstructor]
    public KeywordModel(int @int)
    {
        this.@int = @int;
    }
}
