using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using SharpPack.Internal;

namespace SharpPack;

using static GC;
using static MemoryMarshal;

[StructLayout(LayoutKind.Auto)]
public ref partial struct SharpPackReader
{
    const int DepthLimit = 1000;
    static readonly UTF8Encoding StrictUtf8Encoding = new(false, true);

    ReadOnlySequence<byte> bufferSource;
    readonly long totalLength;
    ref byte bufferReference;
    int bufferLength;
    RentedBufferLease? rentBuffer;
    SmallReadBuffer smallBuffer;
    int advancedCount;
    int consumed;   // total length of consumed
    int depth;
    readonly SharpPackReaderOptionalState optionalState;

    public int Consumed => consumed;
    public long Remaining => totalLength - consumed;
    public SharpPackReaderOptionalState OptionalState => optionalState;

    public SharpPackReader(in ReadOnlySequence<byte> sequence, SharpPackReaderOptionalState optionalState)
    {
        this.bufferSource = sequence.IsSingleSegment ? ReadOnlySequence<byte>.Empty : sequence;
        var span = sequence.FirstSpan;
        this.bufferReference = ref MemoryMarshal.GetReference(span);
        this.bufferLength = span.Length;
        this.advancedCount = 0;
        this.consumed = 0;
        this.depth = 0;
        this.rentBuffer = null;
        this.smallBuffer = default;
        this.totalLength = sequence.Length;
        this.optionalState = optionalState;
    }

    public SharpPackReader(ReadOnlySpan<byte> buffer, SharpPackReaderOptionalState optionalState)
    {
        this.bufferSource = ReadOnlySequence<byte>.Empty;
        this.bufferReference = ref MemoryMarshal.GetReference(buffer);
        this.bufferLength = buffer.Length;
        this.advancedCount = 0;
        this.consumed = 0;
        this.depth = 0;
        this.rentBuffer = null;
        this.smallBuffer = default;
        this.totalLength = buffer.Length;
        this.optionalState = optionalState;
    }

    // buffer operations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanReference(int sizeHint)
    {
        if (sizeHint < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(sizeHint);
        }

        if (sizeHint <= bufferLength)
        {
            return ref bufferReference;
        }

        return ref GetNextSpan(sizeHint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    unsafe ref byte GetNextSpan(int sizeHint)
    {
        if (rentBuffer != null)
        {
            rentBuffer.Return();
            rentBuffer = null;
        }

        if (Remaining == 0)
        {
            SharpPackSerializationException.ThrowSequenceReachedEnd();
        }

        try
        {
            bufferSource = bufferSource.Slice(advancedCount);
        }
        catch (ArgumentOutOfRangeException)
        {
            SharpPackSerializationException.ThrowSequenceReachedEnd();
        }

        advancedCount = 0;

        if (sizeHint <= Remaining)
        {
            if (sizeHint <= bufferSource.FirstSpan.Length)
            {
                bufferReference = ref MemoryMarshal.GetReference(bufferSource.FirstSpan);
                bufferLength = bufferSource.FirstSpan.Length;
                return ref bufferReference;
            }

            if (sizeHint <= SmallReadBuffer.Capacity)
            {
                ref var smallBufferReference = ref Unsafe.AsRef<byte>(
                    Unsafe.AsPointer(ref smallBuffer));
                var smallSpan = MemoryMarshal.CreateSpan(
                    ref smallBufferReference,
                    sizeHint);
                bufferSource.Slice(0, sizeHint).CopyTo(smallSpan);
                bufferReference = ref smallBufferReference;
                bufferLength = sizeHint;
                return ref bufferReference;
            }

            rentBuffer = new RentedBufferLease(
                ArrayPool<byte>.Shared.Rent(sizeHint));
            var span = rentBuffer.Buffer.AsSpan(0, sizeHint);
            bufferSource.Slice(0, sizeHint).CopyTo(span);
            bufferReference = ref MemoryMarshal.GetReference(span);
            bufferLength = span.Length;
            return ref bufferReference;
        }

        SharpPackSerializationException.ThrowSequenceReachedEnd();
        return ref bufferReference; // dummy.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(count);
        }
        if (count == 0) return;

        var rest = bufferLength - count;
        if (rest < 0)
        {
            if (TryAdvanceSequence(count))
            {
                return;
            }
        }

        bufferLength = rest;
        bufferReference = ref Unsafe.Add(ref bufferReference, count);
        advancedCount += count;
        consumed += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void AdvanceWithinSpan(int count)
    {
        // Internal read paths call this only after GetSpanReference has
        // guaranteed a contiguous span of at least count bytes.
        bufferLength -= count;
        bufferReference = ref Unsafe.Add(ref bufferReference, count);
        advancedCount += count;
        consumed += count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    bool TryAdvanceSequence(int count)
    {
        var rest = Remaining - count;
        if (rest < 0)
        {
            SharpPackSerializationException.ThrowInvalidAdvance();
        }

        bufferSource = bufferSource.Slice((long)advancedCount + count);
        bufferReference = ref MemoryMarshal.GetReference(bufferSource.FirstSpan);
        bufferLength = bufferSource.FirstSpan.Length;
        advancedCount = 0;
        consumed += count;
        return true;
    }

    public void GetRemainingSource(out ReadOnlySpan<byte> singleSource, out ReadOnlySequence<byte> remainingSource)
    {
        if (bufferSource.IsEmpty)
        {
            remainingSource = ReadOnlySequence<byte>.Empty;
            singleSource = MemoryMarshal.CreateReadOnlySpan(ref bufferReference, bufferLength);
            return;
        }
        else
        {
            if (bufferSource.IsSingleSegment)
            {
                remainingSource = ReadOnlySequence<byte>.Empty;
                singleSource = bufferSource.FirstSpan.Slice(advancedCount);
                return;
            }

            singleSource = default;
            remainingSource = bufferSource.Slice(advancedCount);
            if (remainingSource.IsSingleSegment)
            {
                singleSource = remainingSource.FirstSpan;
                remainingSource = ReadOnlySequence<byte>.Empty;
                return;
            }
            return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (rentBuffer != null)
        {
            rentBuffer.Return();
            rentBuffer = null;
        }
    }

    [InlineArray(Capacity)]
    struct SmallReadBuffer
    {
        internal const int Capacity = 32;
        byte element0;
    }

    sealed class RentedBufferLease(byte[] buffer)
    {
        int returned;

        internal byte[] Buffer { get; } = buffer;

        internal void Return()
        {
            if (Interlocked.Exchange(ref returned, 1) != 0)
            {
                throw new InvalidOperationException(
                    "The reader buffer lease was already returned.");
            }
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharpPackFormatter<T> GetFormatter<T>()
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
    static SharpPackFormatter<T> GetCollectibleContextFormatter<T>(
        SharpPackSerializerContext context)
        => context.Graph.GetFormatter<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasFormatterOverride<T>()
        => optionalState.HasFormatterOverride<T>();


    // read methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadObjectHeader(out byte memberCount)
    {
        memberCount = GetSpanReference(1);
        AdvanceWithinSpan(1);
        return memberCount != SharpPackCode.NullObject;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUnionHeader(out ushort tag)
    {
        var firstTag = GetSpanReference(1);
        AdvanceWithinSpan(1);
        if (firstTag < SharpPackCode.WideTag)
        {
            tag = firstTag;
            return true;
        }
        else if (firstTag == SharpPackCode.WideTag)
        {
            ReadUnmanaged(out tag);
            return true;
        }
        else
        {
            tag = 0;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadCollectionHeader(out int length)
    {
        length = Unsafe.ReadUnaligned<int>(ref GetSpanReference(4));
        AdvanceWithinSpan(4);

        if (length == SharpPackCode.NullCollection)
        {
            return false;
        }
        if (length < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(length);
        }

        // If collection-length is larger than buffer-length, it is invalid data.
        if (Remaining < length)
        {
            SharpPackSerializationException.ThrowInsufficientBufferUnless(length);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PeekIsNull()
    {
        var code = GetSpanReference(1);
        return code == SharpPackCode.NullObject;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeekObjectHeader(out byte memberCount)
    {
        memberCount = GetSpanReference(1);
        return memberCount != SharpPackCode.NullObject;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeekUnionHeader(out ushort tag)
    {
        var firstTag = GetSpanReference(1);
        if (firstTag < SharpPackCode.WideTag)
        {
            tag = firstTag;
            return true;
        }
        else if (firstTag == SharpPackCode.WideTag)
        {
            ref var spanRef = ref GetSpanReference(sizeof(ushort) + 1); // skip firstTag
            tag = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref spanRef, 1));
            return true;
        }
        else
        {
            tag = 0;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeekCollectionHeader(out int length)
    {
        length = Unsafe.ReadUnaligned<int>(ref GetSpanReference(4));

        if (length == SharpPackCode.NullCollection)
        {
            return false;
        }
        if (length < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(length);
        }

        // If collection-length is larger than buffer-length, it is invalid data.
        if (Remaining < length)
        {
            SharpPackSerializationException.ThrowInsufficientBufferUnless(length);
        }

        return true;
    }

    /// <summary>
    /// no validate collection size, be careful to use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DangerousTryReadCollectionHeader(out int length)
    {
        length = Unsafe.ReadUnaligned<int>(ref GetSpanReference(4));
        AdvanceWithinSpan(4);

        if (length == SharpPackCode.NullCollection)
        {
            return false;
        }
        if (length < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(length);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? ReadString()
    {
        var length = ReadUnmanaged<int>();
        if (length == SharpPackCode.NullCollection)
        {
            return null;
        }
        if (length == 0)
        {
            return "";
        }

        if (length > 0)
        {
            return ReadUtf16(length);
        }
        else
        {
            return ReadUtf8(length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string ReadUtf16(int length)
    {
        var byteCount = GetUnmanagedByteCount<char>(length);
        ref var src = ref GetSpanReference(byteCount);

        var str = new string(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, char>(ref src), length));

        AdvanceWithinSpan(byteCount);

        return str;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // non default, no inline
    string ReadUtf8(int utf8Length)
    {
        // (int ~utf8-byte-count, int utf16-length, utf8-bytes)
        // already read utf8 length, but it is complement.

        utf8Length = ~utf8Length;

        var payloadLength = CheckedAdd(utf8Length, 4);
        ref var spanRef = ref GetSpanReference(payloadLength); // + read utf16 length

        string str;
        var utf16Length = Unsafe.ReadUnaligned<int>(ref spanRef);

        if (utf16Length <= 0)
        {
            var src = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref spanRef, 4), utf8Length);
            try
            {
                str = StrictUtf8Encoding.GetString(src);
            }
            catch (DecoderFallbackException ex)
            {
                throw new SharpPackSerializationException("Failed to decode a strict UTF-8 payload.", ex);
            }
        }
        else
        {
            if (utf16Length > utf8Length)
            {
                SharpPackSerializationException.ThrowInvalidEncodingLength();
            }


            // regular path, know decoded UTF16 length will gets faster decode result
            unsafe
            {
                fixed (byte* p = &Unsafe.Add(ref spanRef, 4))
                {
                    str = string.Create(utf16Length, ((IntPtr)p, utf8Length), static (dest, state) =>
                    {
                        var src = MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>((byte*)state.Item1), state.Item2);
                        var status = Utf8.ToUtf16(src, dest, out var bytesRead, out var charsWritten, replaceInvalidSequences: false);
                        if (status != OperationStatus.Done)
                        {
                            SharpPackSerializationException.ThrowFailedEncoding(status);
                        }
                        if (bytesRead != state.Item2 || charsWritten != dest.Length)
                        {
                            SharpPackSerializationException.ThrowInvalidEncodingLength();
                        }
                    });
                }
            }
        }

        AdvanceWithinSpan(payloadLength);

        return str;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T1 ReadUnmanaged<T1>()
        where T1 : unmanaged
    {
        var size = Unsafe.SizeOf<T1>();
        ref var spanRef = ref GetSpanReference(size);
        var value1 = Unsafe.ReadUnaligned<T1>(ref spanRef);
        AdvanceWithinSpan(size);
        return value1;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadPackable<T>(scoped ref T? value)
        where T : ISharpPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            ReadValue(ref value);
            return;
        }

        EnterDepth<T>();
        try
        {
            T.Deserialize(ref this, ref value);
        }
        finally
        {
            depth--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? ReadPackable<T>()
        where T : ISharpPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            return ReadValue<T>();
        }

        EnterDepth<T>();
        try
        {
            T? value = default;
            T.Deserialize(ref this, ref value);
            return value;
        }
        finally
        {
            depth--;
        }
    }


    // non packable, get formatter dynamically.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadValue<T>(scoped ref T? value)
    {
        EnterDepth<T>();
        try
        {
            GetFormatter<T>().Deserialize(ref this, ref value);
        }
        finally
        {
            depth--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? ReadValue<T>()
    {
        EnterDepth<T>();
        try
        {
            T? value = default;
            GetFormatter<T>().Deserialize(ref this, ref value);
            return value;
        }
        finally
        {
            depth--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadValueWithFormatter<TFormatter, T>(TFormatter formatter, scoped ref T? value)
        where TFormatter : ISharpPackFormatter<T>
    {
        EnterDepth<T>();
        try
        {
            formatter.Deserialize(ref this, ref value);
        }
        finally
        {
            depth--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? ReadValueWithFormatter<TFormatter, T>(TFormatter formatter)
        where TFormatter : ISharpPackFormatter<T>
    {
        EnterDepth<T>();
        try
        {
            T? value = default;
            formatter.Deserialize(ref this, ref value);
            return value;
        }
        finally
        {
            depth--;
        }
    }

    #region ReadArray/Span

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T?[]? ReadArray<T>()
    {
        T?[]? value = default;
        ReadArray(ref value);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadArray<T>(scoped ref T?[]? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            DangerousReadUnmanagedArray(ref value);
            return;
        }

        if (!TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        // T[] support overwrite
        if (value == null || value.Length != length)
        {
            value = new T?[length];
        }

        var formatter = GetFormatter<T>();
        for (int i = 0; i < length; i++)
        {
            formatter.Deserialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadSpan<T>(scoped ref Span<T?> value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            DangerousReadUnmanagedSpan(ref value);
            return;
        }

        if (!TryReadCollectionHeader(out var length))
        {
            value = default;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        if (value.Length != length)
        {
            value = new T?[length];
        }

        var formatter = GetFormatter<T>();
        for (int i = 0; i < length; i++)
        {
            formatter.Deserialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T?[]? ReadPackableArray<T>()
        where T : ISharpPackable<T>
    {
        T?[]? value = default;
        ReadPackableArray(ref value);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadPackableArray<T>(scoped ref T?[]? value)
        where T : ISharpPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            ReadArray(ref value);
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousReadUnmanagedArray(ref value);
            return;
        }

        if (!TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        // T[] support overwrite
        if (value is null || value.Length != length)
        {
            value = new T?[length];
        }

        for (int i = 0; i < length; i++)
        {
            T.Deserialize(ref this, ref value[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadPackableSpan<T>(scoped ref Span<T?> value)
        where T : ISharpPackable<T>
    {
        if (optionalState.FormatterGraph is not null)
        {
            ReadSpan(ref value);
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            DangerousReadUnmanagedSpan(ref value);
            return;
        }

        if (!TryReadCollectionHeader(out var length))
        {
            value = default;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        if (value.Length != length)
        {
            value = new T?[length];
        }

        for (int i = 0; i < length; i++)
        {
            T.Deserialize(ref this, ref value[i]);
        }
    }

    #endregion

    #region UnmanagedArray/Span

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T[]? ReadUnmanagedArray<T>()
        where T : unmanaged
    {
        return DangerousReadUnmanagedArray<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadUnmanagedArray<T>(scoped ref T[]? value)
        where T : unmanaged
    {
        DangerousReadUnmanagedArray<T>(ref value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadUnmanagedSpan<T>(scoped ref Span<T> value)
        where T : unmanaged
    {
        DangerousReadUnmanagedSpan<T>(ref value);
    }

    // T: should be unamanged type
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe T[]? DangerousReadUnmanagedArray<T>()
    {
        if (!TryReadCollectionHeader(out var length))
        {
            return null;
        }

        if (length == 0) return Array.Empty<T>();

        var byteCount = GetUnmanagedByteCount<T>(length);
        ref var src = ref GetSpanReference(byteCount);
        var dest = AllocateUninitializedArray<T>(length);
        Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref GetArrayDataReference(dest)), ref src, (uint)byteCount);
        AdvanceWithinSpan(byteCount);

        return dest;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DangerousReadUnmanagedArray<T>(scoped ref T[]? value)
    {
        if (!TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T>();
            return;
        }

        var byteCount = GetUnmanagedByteCount<T>(length);
        ref var src = ref GetSpanReference(byteCount);

        if (value is null || value.Length != length)
        {
            value = AllocateUninitializedArray<T>(length);
        }

        ref var dest = ref Unsafe.As<T, byte>(ref GetArrayDataReference(value));
        Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

        AdvanceWithinSpan(byteCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DangerousReadUnmanagedSpan<T>(scoped ref Span<T> value)
    {
        if (!TryReadCollectionHeader(out var length))
        {
            value = default;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T>();
            return;
        }

        var byteCount = GetUnmanagedByteCount<T>(length);
        ref var src = ref GetSpanReference(byteCount);

        if (value.Length != length)
        {
            value = AllocateUninitializedArray<T>(length);
        }

        ref var dest = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value));
        Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

        AdvanceWithinSpan(byteCount);
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadSpanWithoutReadLengthHeader<T>(int length, scoped ref Span<T?> value)
    {
        ValidateLength(length);
        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() &&
            !HasFormatterOverride<T>())
        {
            if (value.Length != length)
            {
                value = AllocateUninitializedArray<T?>(length);
            }

            var byteCount = GetUnmanagedByteCount<T>(length);
            ref var src = ref GetSpanReference(byteCount);
            ref var dest = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)!);
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            AdvanceWithinSpan(byteCount);
        }
        else
        {
            if (value.Length != length)
            {
                value = new T?[length];
            }

            var formatter = GetFormatter<T>();
            for (int i = 0; i < length; i++)
            {
                formatter.Deserialize(ref this, ref value[i]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetValidatedUnmanagedByteCount<T>(int length)
    {
        var byteCount = GetUnmanagedByteCount<T>(length);
        if (Remaining < byteCount)
        {
            SharpPackSerializationException.ThrowInvalidRange(
                byteCount,
                Remaining > int.MaxValue
                    ? int.MaxValue
                    : (int)Remaining);
        }
        return byteCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DangerousReadUnmanagedSpanWithoutReadLengthHeader<T>(
        int length,
        int byteCount,
        scoped ref Span<T> value)
    {
        if (length == 0)
        {
            value = Array.Empty<T>();
            return;
        }

        if (value.Length != length)
        {
            value = AllocateUninitializedArray<T>(length);
        }

        ref var src = ref GetSpanReference(byteCount);
        ref var dest = ref Unsafe.As<T, byte>(
            ref MemoryMarshal.GetReference(value));
        Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);
        AdvanceWithinSpan(byteCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadPackableSpanWithoutReadLengthHeader<T>(int length, scoped ref Span<T?> value)
        where T : ISharpPackable<T>
    {
        ValidateLength(length);
        if (optionalState.FormatterGraph is not null)
        {
            ReadSpanWithoutReadLengthHeader(length, ref value);
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<T?>();
            return;
        }

        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            if (value.Length != length)
            {
                value = AllocateUninitializedArray<T?>(length);
            }

            var byteCount = GetUnmanagedByteCount<T>(length);
            ref var src = ref GetSpanReference(byteCount);
            ref var dest = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(value)!);
            Unsafe.CopyBlockUnaligned(ref dest, ref src, (uint)byteCount);

            AdvanceWithinSpan(byteCount);
        }
        else
        {
            if (value.Length != length)
            {
                value = new T?[length];
            }

            for (int i = 0; i < length; i++)
            {
                T.Deserialize(ref this, ref value[i]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DangerousReadUnmanagedSpanView<T>(out bool isNull, out ReadOnlySpan<byte> view)
    {
        if (!TryReadCollectionHeader(out var length))
        {
            isNull = true;
            view = default;
            return;
        }

        isNull = false;

        if (length == 0)
        {
            view = Array.Empty<byte>();
            return;
        }

        var byteCount = GetUnmanagedByteCount<T>(length);
        ref var src = ref GetSpanReference(byteCount);

        var span = MemoryMarshal.CreateReadOnlySpan(ref src, byteCount);

        AdvanceWithinSpan(byteCount);
        view = span; // safe until call next GetSpanReference
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ValidateLength(int length)
    {
        if (length < 0)
        {
            SharpPackSerializationException.ThrowInvalidLength(length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int CheckedAdd(int left, int right)
    {
        var result = (long)left + right;
        if ((ulong)result > int.MaxValue)
        {
            SharpPackSerializationException.ThrowSizeOverflow();
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
            SharpPackSerializationException.ThrowSizeOverflow();
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
            SharpPackSerializationException.ThrowReachedDepthLimit(typeof(T));
        }
    }
}
