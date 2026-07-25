using System.Buffers;
using System.ComponentModel;

namespace SharpPack;


public interface IFixedSizeSharpPackable
{
    static abstract int Size { get; }
}


public interface ISharpPackFormatterFactory<T>
{
    static abstract SharpPackFormatter<T> CreateFormatter();
}


[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISharpPackContextFormatterFactory<T>
{
    static abstract SharpPackFormatter<T> CreateFormatter(
        SharpPackSerializerContext context);
}


[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISharpPackExactSizeSerializable<T>
{
    byte[] SerializeExact();
}


public interface ISharpPackable<T>
{
    // note: serialize parameter should be `ref readonly` but current lang spec can not.
    // see proposal https://github.com/dotnet/csharplang/issues/6010

    static abstract void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;
    static abstract void Deserialize(ref SharpPackReader reader, scoped ref T? value);
}
