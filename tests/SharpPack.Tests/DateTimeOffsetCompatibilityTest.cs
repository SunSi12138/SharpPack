using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SharpPack.Formatters;

namespace SharpPack.Tests;

public class DateTimeOffsetCompatibilityTest
{
    static readonly SharpPackSerializerContext Migration = new SharpPackSerializerContextBuilder()
        .Register(new DotNetDateTimeOffsetFormatter()).Build();

    // Value bytes from MemoryPack #473, written on .NET 9. The first ushort
    // is -300 and the last eight bytes represent 16:48:36 UTC.
    const string LegacyValue = "D4FE000000000000008ADFC6014FDF08";
    static readonly DateTimeOffset LegacyExpected = new(2026, 11, 30, 11, 48, 36, TimeSpan.FromHours(-5));

    [Fact]
    public void ImportsPublishedNet9ValueAndOrdinaryMember()
    {
        var bytes = Convert.FromHexString(LegacyValue);
        var imported = SharpPackSerializer.Deserialize<DateTimeOffset>(bytes, Migration);
        Assert.True(LegacyExpected.EqualsExact(imported));

        var row = SharpPackSerializer.Deserialize<OffsetMigrationRow>([1, .. bytes], Migration)!;
        Assert.True(LegacyExpected.EqualsExact(row.When));
        var normalized = SharpPackSerializer.Serialize(row, Migration);
        Assert.Equal(Convert.FromHexString("01D4FEFFFF00000000008ADFC6014FDF08"), normalized);
        Assert.True(LegacyExpected.EqualsExact(SharpPackSerializer.Deserialize<OffsetMigrationRow>(normalized)!.When));
    }

    [Theory]
    [InlineData(-840)]
    [InlineData(0)]
    [InlineData(345)]
    [InlineData(840)]
    public void IgnoresLegacyPaddingAndKeepsModernLayout(int minutes)
    {
        var expected = new DateTimeOffset(638000000000000007, TimeSpan.FromMinutes(minutes));
        var legacy = new byte[16];
        legacy.AsSpan(2, 6).Fill(0xA5);
        BinaryPrimitives.WriteInt16LittleEndian(legacy, (short)minutes);
        BinaryPrimitives.WriteInt64LittleEndian(legacy.AsSpan(8), expected.UtcTicks);
        Assert.True(expected.EqualsExact(SharpPackSerializer.Deserialize<DateTimeOffset>(legacy, Migration)));

        var normalized = SharpPackSerializer.Serialize(expected, Migration);
        Assert.Equal(16, normalized.Length);
        Assert.Equal(minutes, BinaryPrimitives.ReadInt32LittleEndian(normalized));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(normalized.AsSpan(4)));
        Assert.True(expected.EqualsExact(SharpPackSerializer.Deserialize<DateTimeOffset>(normalized)));
        Assert.True(expected.EqualsExact(SharpPackSerializer.Deserialize<DateTimeOffset>(SharpPackSerializer.Serialize(expected), Migration)));
    }

    [Fact]
    public void ImportsArraysAndListsWithOriginalCollectionHeader()
    {
        var value = Convert.FromHexString(LegacyValue);
        byte[] collection = [2, 0, 0, 0, .. value, .. value];

        var array = SharpPackSerializer.Deserialize<DateTimeOffset[]>(collection, Migration)!;
        Assert.Equal(2, array.Length);
        Assert.All(array, item => Assert.True(LegacyExpected.EqualsExact(item)));
        var list = SharpPackSerializer.Deserialize<List<DateTimeOffset>>(collection, Migration)!;
        Assert.Equal(2, list.Count);
        Assert.All(list, item => Assert.True(LegacyExpected.EqualsExact(item)));
        Assert.Equal(SharpPackSerializer.Serialize(array, Migration), SharpPackSerializer.Serialize(list, Migration));
    }
}

[SharpPackable]
public partial class OffsetMigrationRow
{
    public DateTimeOffset When { get; set; }
}
