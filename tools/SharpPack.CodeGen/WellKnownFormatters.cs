using System.Text;

internal static class WellKnownFormatters
{
    public static string Render()
    {
        string[] unmanagedTypes =
        [
            "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64",
            "Char", "Single", "Double", "Decimal", "Boolean", "IntPtr", "UIntPtr",
            "DateTime", "DateTimeOffset", "TimeSpan", "Guid",
            "Complex", "Plane", "Quaternion", "Matrix3x2", "Matrix4x4", "Vector2", "Vector3", "Vector4",
            "Rune", "DateOnly", "TimeOnly", "Half", "Int128", "UInt128",
        ];
        string[] net11UnmanagedTypes = ["BFloat16", "Decimal32", "Decimal64", "Decimal128"];
        string[] customTypes =
        [
            "String", "Version", "Uri", "TimeZoneInfo", "BigInteger", "BitArray", "StringBuilder", "Type", "CultureInfo",
        ];

        var source = new StringBuilder().AppendLine("""
            using SharpPack.Formatters;
            using System.Collections;
            using System.Globalization;
            using System.Text;
            using System.Numerics;

            namespace SharpPack;

            internal static partial class FormatterResolver
            {
                internal static object? CreateWellKnownFormatter(Type type)
                {
            """);
        foreach (var type in unmanagedTypes)
            AppendUnmanagedFormatters(type);
        foreach (var type in customTypes)
        {
            source.AppendLine($"        if (type == typeof({type})) return new {type}Formatter();");
            source.AppendLine($"        if (type == typeof({type}[])) return new ArrayFormatter<{type}>();");
        }

        source.AppendLine("#if NET11_0_OR_GREATER");
        foreach (var type in net11UnmanagedTypes)
            AppendUnmanagedFormatters(type);
        source.AppendLine("#endif");

        return source.AppendLine("""
                    return null;
                }
            }
            """).ToString();

        void AppendUnmanagedFormatters(string type)
        {
            source.AppendLine($"        if (type == typeof({type})) return new UnmanagedFormatter<{type}>();");
            source.AppendLine($"        if (type == typeof({type}[])) return new UnmanagedArrayFormatter<{type}>();");
            source.AppendLine($"        if (type == typeof({type}?)) return new NullableFormatter<{type}>();");
        }
    }
}
