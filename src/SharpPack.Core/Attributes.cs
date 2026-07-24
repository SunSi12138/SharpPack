namespace SharpPack;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackableAttribute : Attribute
{
    public GenerateType GenerateType { get; }
    public SerializeLayout SerializeLayout { get; }

    // ctor parameter is parsed in SharpPackGenerator.Parser TypeMeta for detect which ctor used in SharpPack.Generator.
    // if modify ctor, be careful.

    /// <summary>
    /// [generateType, (VersionTolerant or CircularReference) ? SerializeLayout.Explicit : SerializeLayout.Sequential]
    /// </summary>
    /// <param name="generateType"></param>
    public SharpPackableAttribute(GenerateType generateType = GenerateType.Object)
    {
        this.GenerateType = generateType;
        this.SerializeLayout = (generateType == GenerateType.VersionTolerant || generateType == GenerateType.CircularReference)
            ? SerializeLayout.Explicit
            : SerializeLayout.Sequential;
    }

    /// <summary>
    /// [GenerateType.Object, serializeLayout]
    /// </summary>
    public SharpPackableAttribute(SerializeLayout serializeLayout)
    {
        this.GenerateType = GenerateType.Object;
        this.SerializeLayout = serializeLayout;
    }

    public SharpPackableAttribute(GenerateType generateType, SerializeLayout serializeLayout)
    {
        this.GenerateType = generateType;
        this.SerializeLayout = serializeLayout;
    }
}

public enum GenerateType
{
    Object,
    VersionTolerant,
    CircularReference,
    Collection,
    NoGenerate
}

public enum SerializeLayout
{
    Sequential, // default
    Explicit
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class SharpPackUnionAttribute : Attribute
{
    public ushort Tag { get; }
    public Type Type { get; }

    public SharpPackUnionAttribute(ushort tag, Type type)
    {
        this.Tag = tag;
        this.Type = type;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackUnionFormatterAttribute : Attribute
{
    public Type Type { get; }

    public SharpPackUnionFormatterAttribute(Type type)
    {
        this.Type = type;
    }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackAllowSerializeAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackOrderAttribute : Attribute
{
    public int Order { get; }

    public SharpPackOrderAttribute(int order)
    {
        this.Order = order;
    }
}


[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public abstract class SharpPackCustomFormatterAttribute<T> : Attribute
{
    public abstract ISharpPackFormatter<T> GetFormatter();
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public abstract class SharpPackCustomFormatterAttribute<TFormatter, T> : Attribute
    where TFormatter : ISharpPackFormatter<T>
{
    public abstract TFormatter GetFormatter();
}


// similar naming as System.Text.Json attribtues
// https://docs.microsoft.com/en-us/dotnet/api/system.text.json.serialization.jsonattribute

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackIgnoreAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackIncludeAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackConstructorAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackOnSerializingAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackOnSerializedAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackOnDeserializingAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SharpPackOnDeserializedAttribute : Attribute
{
}

// Others

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class GenerateTypeScriptAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SuppressDefaultInitializationAttribute : Attribute;
