using MemoryPack.Internal;
using System.Runtime.CompilerServices;

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace MemoryPack.Compression;

[Preserve]
public sealed class BitPackFormatter : MemoryPackFormatter<bool[]>
{
    public static readonly BitPackFormatter Default = new BitPackFormatter();

    [Preserve]
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref bool[]? value)
    {
        if (value == null)
        {
            writer.WriteNullCollectionHeader();
            return;
        }
        writer.WriteCollectionHeader(value.Length);
        if (value.Length == 0)
        {
            return;
        }

        var data = 0;
        ref var item = ref MemoryMarshal.GetArrayDataReference(value);
        ref var end = ref Unsafe.Add(ref item, value.Length);

        if (value.Length >= 32)
        {
            ref var loopEnd = ref Unsafe.Subtract(ref end, 32);
            if (Vector256.IsHardwareAccelerated)
            {
                while (!Unsafe.IsAddressGreaterThan(ref item, ref loopEnd))
                {
                    var vector = Vector256.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item));
                    // false -> 1 true -> 0
                    data = (int)Vector256.Equals(vector, Vector256<byte>.Zero).ExtractMostSignificantBits();
                    writer.WriteUnmanaged(~data);
                    item = ref Unsafe.Add(ref item, 32);
                }
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                while (!Unsafe.IsAddressGreaterThan(ref item, ref loopEnd))
                {
                    var bits0 = (ushort)Vector128.Equals(Vector128.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item)), Vector128<byte>.Zero).ExtractMostSignificantBits();
                    var bits1 = (ushort)Vector128.Equals(Vector128.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item), 16), Vector128<byte>.Zero).ExtractMostSignificantBits();
                    data = bits0 | (bits1 << 16);
                    writer.WriteUnmanaged(~data);
                    item = ref Unsafe.Add(ref item, 32);
                }
            }
            else if (Vector64.IsHardwareAccelerated)
            {
                while (!Unsafe.IsAddressGreaterThan(ref item, ref loopEnd))
                {
                    var bits0 = (byte)Vector64.Equals(Vector64.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item)), Vector64<byte>.Zero).ExtractMostSignificantBits();
                    var bits1 = (byte)Vector64.Equals(Vector64.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item), 8), Vector64<byte>.Zero).ExtractMostSignificantBits();
                    var bits2 = (byte)Vector64.Equals(Vector64.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item), 16), Vector64<byte>.Zero).ExtractMostSignificantBits();
                    var bits3 = (byte)Vector64.Equals(Vector64.LoadUnsafe(ref Unsafe.As<bool, byte>(ref item), 24), Vector64<byte>.Zero).ExtractMostSignificantBits();
                    data = bits0 | (bits1 << 8) | (bits2 << 16) | (bits3 << 24);
                    writer.WriteUnmanaged(~data);
                    item = ref Unsafe.Add(ref item, 32);
                }
            }

            data = 0;
        }
        var bit = 0;
        while (Unsafe.IsAddressLessThan(ref item, ref end))
        {
            Set(ref data, bit, item);

            item = ref Unsafe.Add(ref item, 1);
            bit += 1;

            if (bit == 32)
            {
                writer.WriteUnmanaged(data);
                data = 0;
                bit = 0;
            }
        }

        if (bit != 0)
        {
            writer.WriteUnmanaged(data);
        }
    }

    [Preserve]
    public override void Deserialize(ref MemoryPackReader reader, scoped ref bool[]? value)
    {
        if (!reader.DangerousTryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }

        if (length == 0)
        {
            value = Array.Empty<bool>();
            return;
        }

        var readCount = ((length - 1) / 32) + 1;
        var requireSize = readCount * 4;
        if (reader.Remaining < requireSize)
        {
            MemoryPackSerializationException.ThrowInsufficientBufferUnless(length);
        }

        if (value == null || value.Length != length)
        {
            value = new bool[length];
        }

        ref var item = ref MemoryMarshal.GetArrayDataReference(value);
        var fullBlockLength = length & ~31;
        for (var offset = 0; offset < fullBlockLength; offset += 32)
        {
            reader.ReadUnmanaged(out uint data);
            ref var destination = ref Unsafe.As<bool, byte>(
                ref Unsafe.Add(ref item, offset));
            if (Avx2.IsSupported)
            {
                ExpandAvx2(data, ref destination);
            }
            else if (AdvSimd.IsSupported)
            {
                ExpandAdvSimd(data, ref destination);
            }
            else
            {
                ExpandNibbles(data, ref destination);
            }
        }

        var remaining = length - fullBlockLength;
        if (remaining != 0)
        {
            reader.ReadUnmanaged(out int data);
            for (var bit = 0; bit < remaining; bit++)
            {
                Unsafe.Add(ref item, fullBlockLength + bit) =
                    Get(data, bit);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ExpandAvx2(uint data, ref byte destination)
    {
        var source = Vector256.Create(data);
        var mask = Vector256.Create(1u);
        var bits0 = Avx2.ShiftRightLogicalVariable(
            source,
            Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u)) & mask;
        var bits1 = Avx2.ShiftRightLogicalVariable(
            source,
            Vector256.Create(8u, 9u, 10u, 11u, 12u, 13u, 14u, 15u)) & mask;
        var bits2 = Avx2.ShiftRightLogicalVariable(
            source,
            Vector256.Create(16u, 17u, 18u, 19u, 20u, 21u, 22u, 23u)) & mask;
        var bits3 = Avx2.ShiftRightLogicalVariable(
            source,
            Vector256.Create(24u, 25u, 26u, 27u, 28u, 29u, 30u, 31u)) & mask;
        var lower = Vector256.Narrow(bits0, bits1);
        var upper = Vector256.Narrow(bits2, bits3);
        Vector256.Narrow(lower, upper).StoreUnsafe(ref destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ExpandAdvSimd(uint data, ref byte destination)
    {
        var source = Vector128.Create(data);
        var mask = Vector128.Create(1u);
        var bits0 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(0, -1, -2, -3)) & mask;
        var bits1 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-4, -5, -6, -7)) & mask;
        var bits2 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-8, -9, -10, -11)) & mask;
        var bits3 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-12, -13, -14, -15)) & mask;
        var lower = Vector128.Narrow(
            Vector128.Narrow(bits0, bits1),
            Vector128.Narrow(bits2, bits3));
        lower.StoreUnsafe(ref destination);

        bits0 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-16, -17, -18, -19)) & mask;
        bits1 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-20, -21, -22, -23)) & mask;
        bits2 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-24, -25, -26, -27)) & mask;
        bits3 = AdvSimd.ShiftLogical(
            source,
            Vector128.Create(-28, -29, -30, -31)) & mask;
        var upper = Vector128.Narrow(
            Vector128.Narrow(bits0, bits1),
            Vector128.Narrow(bits2, bits3));
        upper.StoreUnsafe(ref destination, 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ExpandNibbles(uint data, ref byte destination)
    {
        if (!BitConverter.IsLittleEndian)
        {
            for (var bit = 0; bit < 32; bit++)
            {
                Unsafe.Add(ref destination, bit) =
                    (byte)((data >> bit) & 1);
            }
            return;
        }

        var lookup = NibbleExpansion;
        Unsafe.WriteUnaligned(
            ref destination,
            lookup[(int)(data & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 4),
            lookup[(int)((data >> 4) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 8),
            lookup[(int)((data >> 8) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 12),
            lookup[(int)((data >> 12) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 16),
            lookup[(int)((data >> 16) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 20),
            lookup[(int)((data >> 20) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 24),
            lookup[(int)((data >> 24) & 0xF)]);
        Unsafe.WriteUnaligned(
            ref Unsafe.Add(ref destination, 28),
            lookup[(int)(data >> 28)]);
    }

    static ReadOnlySpan<uint> NibbleExpansion =>
    [
        0x00000000u,
        0x00000001u,
        0x00000100u,
        0x00000101u,
        0x00010000u,
        0x00010001u,
        0x00010100u,
        0x00010101u,
        0x01000000u,
        0x01000001u,
        0x01000100u,
        0x01000101u,
        0x01010000u,
        0x01010001u,
        0x01010100u,
        0x01010101u,
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Get(int data, int index)
    {
        return (data & (1 << index)) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(ref int data, int index, bool value)
    {
        int bitMask = 1 << index;
        if (value)
        {
            data |= bitMask;
        }
        else
        {
            data &= ~bitMask;
        }
    }
}
