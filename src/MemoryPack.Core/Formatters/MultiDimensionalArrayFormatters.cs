using MemoryPack.Internal;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace MemoryPack.Formatters;

[Preserve]
public sealed class TwoDimensionalArrayFormatter<T> : MemoryPackFormatter<T?[,]>
{
    // {i-length, j-length, [total-length, values]}

    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T?[,]? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(3);

        var i = value.GetLength(0);
        var j = value.GetLength(1);
        writer.WriteUnmanaged(i, j);

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !writer.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(value.Length);
            ref var src = ref MemoryMarshal.GetArrayDataReference(value);
            var totalLength = FormatterValidation.AddHeader(byteCount);
            ref var dest = ref writer.GetSpanReference(totalLength);

            Unsafe.WriteUnaligned(ref dest, value.Length);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)byteCount);
            writer.Advance(totalLength);
        }
        else
        {

            writer.WriteCollectionHeader(value.Length);
            var formatter = writer.GetFormatter<T?>();
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }
    }

    [Preserve]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref T?[,]? value)
    {
        if (!reader.TryReadObjectHeader(out var propertyCount))
        {
            value = null;
            return;
        }

        if (propertyCount != 3)
        {
            MemoryPackSerializationException.ThrowInvalidPropertyCount(3, propertyCount);
        }

        reader.ReadUnmanaged(out int iLength, out int jLength);

        if (!reader.TryReadCollectionHeader(out var length))
        {
            MemoryPackSerializationException.ThrowInvalidCollection();
        }
        var elementCount = FormatterValidation.ValidateDimensions(
            length,
            iLength,
            jLength);

        if (value != null && value.GetLength(0) == iLength && value.GetLength(1) == jLength && value.Length == length)
        {
            // allow overwrite
        }
        else
        {
            value = new T[iLength, jLength];
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !reader.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(elementCount);
            ref var dest = ref MemoryMarshal.GetArrayDataReference(value);
            ref var src = ref reader.GetSpanReference(byteCount);
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            reader.Advance(byteCount);
        }
        else
        {
            var formatter = reader.GetFormatter<T?>();

            var i = 0;
            var j = -1;
            var count = 0;
            while (count++ < length)
            {
                if (j < jLength - 1)
                {
                    j++;
                }
                else
                {
                    j = 0;
                    i++;
                }

                formatter.Deserialize(ref reader, ref value[i, j]);
            }
        }
    }
}

[Preserve]
public sealed class ThreeDimensionalArrayFormatter<T> : MemoryPackFormatter<T?[,,]>
{
    // {i-length, j-length, k-length, [total-length, values]}

    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T?[,,]? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(4);

        var i = value.GetLength(0);
        var j = value.GetLength(1);
        var k = value.GetLength(2);
        writer.WriteUnmanaged(i, j, k);

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !writer.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(value.Length);
            ref var src = ref MemoryMarshal.GetArrayDataReference(value);
            var totalLength = FormatterValidation.AddHeader(byteCount);
            ref var dest = ref writer.GetSpanReference(totalLength);

            Unsafe.WriteUnaligned(ref dest, value.Length);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)byteCount);
            writer.Advance(totalLength);
        }
        else
        {
            writer.WriteCollectionHeader(value.Length);
            var formatter = writer.GetFormatter<T?>();
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }
    }

    [Preserve]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref T?[,,]? value)
    {
        if (!reader.TryReadObjectHeader(out var propertyCount))
        {
            value = null;
            return;
        }

        if (propertyCount != 4)
        {
            MemoryPackSerializationException.ThrowInvalidPropertyCount(4, propertyCount);
        }

        reader.ReadUnmanaged(out int iLength, out int jLength, out int kLength);

        if (!reader.TryReadCollectionHeader(out var length))
        {
            MemoryPackSerializationException.ThrowInvalidCollection();
        }
        var elementCount = FormatterValidation.ValidateDimensions(
            length,
            iLength,
            jLength,
            kLength);

        if (value != null && value.GetLength(0) == iLength && value.GetLength(1) == jLength && value.GetLength(2) == kLength && value.Length == length)
        {
            // allow overwrite
        }
        else
        {
            value = new T[iLength, jLength, kLength];
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !reader.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(elementCount);
            ref var dest = ref MemoryMarshal.GetArrayDataReference(value);
            ref var src = ref reader.GetSpanReference(byteCount);
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            reader.Advance(byteCount);
        }
        else
        {
            var formatter = reader.GetFormatter<T?>();

            var i = 0;
            var j = 0;
            var k = -1;
            var count = 0;
            while (count++ < length)
            {
                if (k < kLength - 1)
                {
                    k++;
                }
                else if (j < jLength - 1)
                {
                    k = 0;
                    j++;
                }
                else
                {
                    k = 0;
                    j = 0;
                    i++;
                }

                formatter.Deserialize(ref reader, ref value[i, j, k]);
            }
        }
    }
}

[Preserve]
public sealed class FourDimensionalArrayFormatter<T> : MemoryPackFormatter<T?[,,,]>
{
    // {i-length, j-length, k-length, l-length, [total-length, values]}

    internal override bool HasFormatterOverrideDependency(FormatterGraph graph)
        => graph.HasFormatterOverride<T>();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T?[,,,]? value)
    {
        if (value == null)
        {
            writer.WriteNullObjectHeader();
            return;
        }

        writer.WriteObjectHeader(5);

        var i = value.GetLength(0);
        var j = value.GetLength(1);
        var k = value.GetLength(2);
        var l = value.GetLength(3);
        writer.WriteUnmanaged(i, j, k, l);

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !writer.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(value.Length);
            ref var src = ref MemoryMarshal.GetArrayDataReference(value);
            var totalLength = FormatterValidation.AddHeader(byteCount);
            ref var dest = ref writer.GetSpanReference(totalLength);

            Unsafe.WriteUnaligned(ref dest, value.Length);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)byteCount);
            writer.Advance(totalLength);
        }
        else
        {
            writer.WriteCollectionHeader(value.Length);
            var formatter = writer.GetFormatter<T?>();
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }
    }

    [Preserve]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref T?[,,,]? value)
    {
        if (!reader.TryReadObjectHeader(out var propertyCount))
        {
            value = null;
            return;
        }

        if (propertyCount != 5)
        {
            MemoryPackSerializationException.ThrowInvalidPropertyCount(5, propertyCount);
        }

        reader.ReadUnmanaged(out int iLength, out int jLength, out int kLength, out int lLength);

        if (!reader.TryReadCollectionHeader(out var length))
        {
            MemoryPackSerializationException.ThrowInvalidCollection();
        }
        var elementCount = FormatterValidation.ValidateDimensions(
            length,
            iLength,
            jLength,
            kLength,
            lLength);

        if (value != null && value.GetLength(0) == iLength && value.GetLength(1) == jLength && value.GetLength(2) == kLength && value.GetLength(3) == lLength && value.Length == length)
        {
            // allow overwrite
        }
        else
        {
            value = new T[iLength, jLength, kLength, lLength];
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T?>() &&
            !reader.HasFormatterOverride<T>())
        {
            var byteCount = FormatterValidation.ByteCount<T>(elementCount);
            ref var dest = ref MemoryMarshal.GetArrayDataReference(value);
            ref var src = ref reader.GetSpanReference(byteCount);
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            reader.Advance(byteCount);
        }
        else
        {
            var formatter = reader.GetFormatter<T?>();

            var i = 0;
            var j = 0;
            var k = 0;
            var l = -1;
            var count = 0;
            while (count++ < length)
            {
                if (l < lLength - 1)
                {
                    l++;
                }
                else if (k < kLength - 1)
                {
                    l = 0;
                    k++;
                }
                else if (j < jLength - 1)
                {
                    l = 0;
                    k = 0;
                    j++;
                }
                else
                {
                    l = 0;
                    k = 0;
                    j = 0;
                    i++;
                }

                formatter.Deserialize(ref reader, ref value[i, j, k, l]);
            }
        }
    }
}
