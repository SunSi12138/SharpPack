using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace MemoryPack;

using static MemoryMarshal;

[StructLayout(LayoutKind.Auto)]
public ref partial struct MemoryPackWriter<TBufferWriter>
    where TBufferWriter : IBufferWriter<byte>
{
    const int DepthLimit = 1000;

    ref TBufferWriter bufferWriter;
    ref byte bufferReference;
    int bufferLength;
    int advancedCount;
    int depth; // check recursive serialize
    int writtenCount;
    readonly bool serializeStringAsUtf8;
    readonly MemoryPackWriterOptionalState optionalState;

    public int WrittenCount => writtenCount;
    public int BufferLength => bufferLength;
    public MemoryPackWriterOptionalState OptionalState => optionalState;
    public MemoryPackSerializerConfiguration Configuration => optionalState.Configuration;

    public MemoryPackWriter(ref TBufferWriter writer, MemoryPackWriterOptionalState optionalState)
    {
        this.bufferWriter = ref writer;
        this.bufferReference = ref Unsafe.NullRef<byte>();
        this.bufferLength = 0;
        this.advancedCount = 0;
        this.writtenCount = 0;
        this.depth = 0;
        this.serializeStringAsUtf8 = optionalState.Configuration.StringEncoding == MemoryPackStringEncoding.Utf8;
        this.optionalState = optionalState;
    }

    // optimized ctor, avoid first GetSpan call if we can.
    public MemoryPackWriter(ref TBufferWriter writer, byte[] firstBufferOfWriter, MemoryPackWriterOptionalState optionalState)
    {
        this.bufferWriter = ref writer;
        this.bufferReference = ref GetArrayDataReference(firstBufferOfWriter);
        this.bufferLength = firstBufferOfWriter.Length;
        this.advancedCount = 0;
        this.writtenCount = 0;
        this.depth = 0;
        this.serializeStringAsUtf8 = optionalState.Configuration.StringEncoding == MemoryPackStringEncoding.Utf8;
        this.optionalState = optionalState;
    }

    public MemoryPackWriter(ref TBufferWriter writer, Span<byte> firstBufferOfWriter, MemoryPackWriterOptionalState optionalState)
    {
        this.bufferWriter = ref writer;
        this.bufferReference = ref MemoryMarshal.GetReference(firstBufferOfWriter);
        this.bufferLength = firstBufferOfWriter.Length;
        this.advancedCount = 0;
        this.writtenCount = 0;
        this.depth = 0;
        this.serializeStringAsUtf8 = optionalState.Configuration.StringEncoding == MemoryPackStringEncoding.Utf8;
        this.optionalState = optionalState;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanReference(int sizeHint)
    {
        if (sizeHint < 0)
        {
            MemoryPackSerializationException.ThrowInvalidLength(sizeHint);
        }
        if (bufferLength < sizeHint)
        {
            RequestNewBuffer(sizeHint);
        }

        return ref bufferReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void RequestNewBuffer(int sizeHint)
    {
        if (advancedCount != 0)
        {
            bufferWriter.Advance(advancedCount);
            advancedCount = 0;
        }
        var span = bufferWriter.GetSpan(sizeHint);
        bufferReference = ref MemoryMarshal.GetReference(span);
        bufferLength = span.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0)
        {
            MemoryPackSerializationException.ThrowInvalidLength(count);
        }
        if (count == 0) return;

        var rest = bufferLength - count;
        if (rest < 0)
        {
            MemoryPackSerializationException.ThrowInvalidAdvance();
        }

        bufferLength = rest;
        bufferReference = ref Unsafe.Add(ref bufferReference, count);
        advancedCount += count;
        writtenCount += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        if (advancedCount != 0)
        {
            bufferWriter.Advance(advancedCount);
            advancedCount = 0;
        }
        bufferReference = ref Unsafe.NullRef<byte>();
        bufferLength = 0;
        writtenCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemoryPackFormatter<T> GetFormatter<T>()
    {
        if (optionalState.FormatterGraph is { } graph)
        {
            return graph.GetFormatter<T>();
        }
        if (FormatterTypeTraits<T>.ContainsCollectibleType &&
            optionalState.SerializerContext is { } context)
        {
            return GetCollectibleContextFormatter<T>(context);
        }
        return FormatterSlot<T>.Formatter;
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    static MemoryPackFormatter<T> GetCollectibleContextFormatter<T>(
        MemoryPackSerializerContext context)
        => context.Graph.GetFormatter<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool HasFormatterOverride<T>()
        => optionalState.HasFormatterOverride<T>();


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetStringWriteLength(string? value)
    {
        if (value == null || value.Length == 0)
        {
            return 4;
        }

        if (serializeStringAsUtf8)
        {
            return Encoding.UTF8.GetByteCount(value) + 8;
        }
        else
        {
            return checked(value.Length * 2) + 4;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetUnmanageArrayWriteLength<T>(T[]? value)
        where T : unmanaged
    {
        if (value == null || value.Length == 0)
        {
            return 4;
        }

        return CheckedAdd(GetUnmanagedByteCount<T>(value.Length), 4);
    }

    // Write methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteObjectHeader(byte memberCount)
    {
        if (memberCount >= MemoryPackCode.Reserved1)
        {
            MemoryPackSerializationException.ThrowWriteInvalidMemberCount(memberCount);
        }
        GetSpanReference(1) = memberCount;
        Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNullObjectHeader()
    {
        GetSpanReference(1) = MemoryPackCode.NullObject;
        Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteObjectReferenceId(uint referenceId)
    {
        GetSpanReference(1) = MemoryPackCode.ReferenceId;
        Advance(1);
        WriteVarInt(referenceId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnionHeader(ushort tag)
    {
        if (tag < MemoryPackCode.WideTag)
        {
            GetSpanReference(1) = (byte)tag;
            Advance(1);
        }
        else
        {
            ref var spanRef = ref GetSpanReference(3);
            Unsafe.WriteUnaligned(ref spanRef, MemoryPackCode.WideTag);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref spanRef, 1), tag);
            Advance(3);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNullUnionHeader()
    {
        WriteNullObjectHeader();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCollectionHeader(int length)
    {
        ValidateLength(length);
        Unsafe.WriteUnaligned(ref GetSpanReference(4), length);
        Advance(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNullCollectionHeader()
    {
        Unsafe.WriteUnaligned(ref GetSpanReference(4), MemoryPackCode.NullCollection);
        Advance(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string? value)
    {
        if (serializeStringAsUtf8)
        {
            WriteUtf8(value);
        }
        else
        {
            WriteUtf16(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf16(string? value)
    {
        if (value == null)
        {
            WriteNullCollectionHeader();
            return;
        }

        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        var copyByteCount = checked(value.Length * 2);

        var totalLength = CheckedAdd(copyByteCount, 4);
        ref var dest = ref GetSpanReference(totalLength);
        Unsafe.WriteUnaligned(ref dest, value.Length);

        ref var src = ref Unsafe.As<char, byte>(ref Unsafe.AsRef(in value.GetPinnableReference()));
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)copyByteCount);

        Advance(totalLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf16(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        var copyByteCount = checked(value.Length * 2);

        var totalLength = CheckedAdd(copyByteCount, 4);
        ref var dest = ref GetSpanReference(totalLength);
        Unsafe.WriteUnaligned(ref dest, value.Length);
        MemoryMarshal.AsBytes(value).CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref dest, 4), copyByteCount));
        Advance(totalLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf8(string? value)
    {
        if (value == null)
        {
            WriteNullCollectionHeader();
            return;
        }

        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        // (int ~utf8-byte-count, int utf16-length, utf8-bytes)

        var source = value.AsSpan();

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(source.Length);
        var requiredLength = CheckedAdd(maxByteCount, 8);

        ref var destPointer = ref GetSpanReference(requiredLength); // header

        // write utf16-length
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref destPointer, 4), source.Length);

        var dest = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref destPointer, 8), maxByteCount);
        var status = Utf8.FromUtf16(source, dest, out var _, out var bytesWritten, replaceInvalidSequences: false);
        if (status != OperationStatus.Done)
        {
            MemoryPackSerializationException.ThrowFailedEncoding(status);
        }

        // write written utf8-length in header, that is ~length
        Unsafe.WriteUnaligned(ref destPointer, ~bytesWritten);
        Advance(CheckedAdd(bytesWritten, 8)); // + header
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf8(ReadOnlySpan<byte> utf8Value, int utf16Length = -1)
    {
        if (utf8Value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        // (int ~utf8-byte-count, int utf16-length, utf8-bytes)

        var requiredLength = CheckedAdd(utf8Value.Length, 8);
        ref var destPointer = ref GetSpanReference(requiredLength); // header

        Unsafe.WriteUnaligned(ref destPointer, ~utf8Value.Length);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref destPointer, 4), utf16Length);

        var dest = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref destPointer, 8), utf8Value.Length);
        utf8Value.CopyTo(dest);

        Advance(requiredLength);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackable<T>(scoped in T? value)
        where T : IMemoryPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            WriteValue(value);
            return;
        }

        EnterDepth<T>();
        try
        {
            T.Serialize(ref this, ref Unsafe.AsRef(in value));
        }
        finally
        {
            depth--;
        }
    }


    // non packable, get formatter dynamically.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteValue<T>(scoped in T? value)
    {
        EnterDepth<T>();
        try
        {
            GetFormatter<T>().Serialize(ref this, ref Unsafe.AsRef(in value));
        }
        finally
        {
            depth--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteValueWithFormatter<TFormatter, T>(TFormatter formatter, scoped in T? value)
        where TFormatter : IMemoryPackFormatter<T>
    {
        EnterDepth<T>();
        try
        {
            formatter.Serialize(ref this, ref Unsafe.AsRef(in value));
        }
        finally
        {
            depth--;
        }
    }

    #region WriteArray/Span

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArray<T>(T?[]? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            DangerousWriteUnmanagedArray(value);
            return;
        }

        if (value == null)
        {
            WriteNullCollectionHeader();
            return;
        }

        var formatter = GetFormatter<T>();
        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            formatter.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSpan<T>(scoped Span<T?> value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            DangerousWriteUnmanagedSpan(value);
            return;
        }

        var formatter = GetFormatter<T>();
        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            formatter.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSpan<T>(scoped ReadOnlySpan<T?> value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            DangerousWriteUnmanagedSpan(value);
            return;
        }

        var formatter = GetFormatter<T>();
        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            formatter.Serialize(ref this, ref Unsafe.AsRef(in value[i]));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackableArray<T>(T?[]? value)
        where T : IMemoryPackable<T>
    {

        if (optionalState.FormatterGraph is not null)
        {
            WriteArray(value);
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteUnmanagedArray(value);
            return;
        }

        if (value == null)
        {
            WriteNullCollectionHeader();
            return;
        }

        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            T.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackableSpan<T>(scoped Span<T?> value)
        where T : IMemoryPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            WriteSpan(value);
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteUnmanagedSpan(value);
            return;
        }

        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            T.Serialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePackableSpan<T>(scoped ReadOnlySpan<T?> value)
        where T : IMemoryPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            WriteSpan(value);
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousWriteUnmanagedSpan(value);
            return;
        }

        WriteCollectionHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            T.Serialize(ref this, ref Unsafe.AsRef(in value[i]));
        }
    }

    #endregion

    #region WriteUnmanagedArray/Span

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnmanagedArray<T>(T[]? value)
        where T : unmanaged
    {
        DangerousWriteUnmanagedArray(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnmanagedSpan<T>(scoped Span<T> value)
        where T : unmanaged
    {
        DangerousWriteUnmanagedSpan(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnmanagedSpan<T>(scoped ReadOnlySpan<T> value)
        where T : unmanaged
    {
        DangerousWriteUnmanagedSpan(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteUnmanagedArray<T>(T[]? value)
    {
        if (value == null)
        {
            WriteNullCollectionHeader();
            return;
        }
        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        var srcLength = GetUnmanagedByteCount<T>(value.Length);
        var allocSize = CheckedAdd(srcLength, 4);

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));

        Unsafe.WriteUnaligned(ref dest, value.Length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)srcLength);

        Advance(allocSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteUnmanagedSpan<T>(scoped Span<T> value)
    {
        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        var srcLength = GetUnmanagedByteCount<T>(value.Length);
        var allocSize = CheckedAdd(srcLength, 4);

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));

        Unsafe.WriteUnaligned(ref dest, value.Length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)srcLength);

        Advance(allocSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DangerousWriteUnmanagedSpan<T>(scoped ReadOnlySpan<T> value)
    {
        if (value.Length == 0)
        {
            WriteCollectionHeader(0);
            return;
        }

        var srcLength = GetUnmanagedByteCount<T>(value.Length);
        var allocSize = CheckedAdd(srcLength, 4);

        ref var dest = ref GetSpanReference(allocSize);
        ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));

        Unsafe.WriteUnaligned(ref dest, value.Length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref dest, 4), ref src, (uint)srcLength);

        Advance(allocSize);
    }

    #endregion


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSpanWithoutLengthHeader<T>(scoped ReadOnlySpan<T?> value)
    {
        if (value.Length == 0) return;

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            var srcLength = GetUnmanagedByteCount<T>(value.Length);
            ref var dest = ref GetSpanReference(srcLength);
            ref var src = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)!);

            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)srcLength);

            Advance(srcLength);
            return;
        }
        else
        {
            var formatter = GetFormatter<T>();
            for (int i = 0; i < value.Length; i++)
            {
                formatter.Serialize(ref this, ref Unsafe.AsRef(in value[i]));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ValidateLength(int length)
    {
        if (length < 0)
        {
            MemoryPackSerializationException.ThrowInvalidLength(length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int CheckedAdd(int left, int right)
    {
        var result = (long)left + right;
        if ((ulong)result > int.MaxValue)
        {
            MemoryPackSerializationException.ThrowSizeOverflow();
        }
        return (int)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetUnmanagedByteCount<T>(int length)
    {
        ValidateLength(length);
        var byteCount = (long)length * Unsafe.SizeOf<T>();
        if ((ulong)byteCount > int.MaxValue)
        {
            MemoryPackSerializationException.ThrowSizeOverflow();
        }
        return (int)byteCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnterDepth<T>()
    {
        depth++;
        if (depth >= DepthLimit)
        {
            depth--;
            MemoryPackSerializationException.ThrowReachedDepthLimit(typeof(T));
        }
    }
}
