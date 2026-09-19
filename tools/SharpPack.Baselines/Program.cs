using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpPack;
using SharpPack.Generator;
using SharpPack.Streaming;
using System.Globalization;
using System.Reflection;
using System.Text;

return BaselineProgram.Run(args);

static class BaselineProgram
{
    const string ToolProject = "tools/SharpPack.Baselines/SharpPack.Baselines.csproj";

    public static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "verify" : args[0];
        if (mode is not ("verify" or "update"))
        {
            Console.Error.WriteLine($"Usage: dotnet run --project {ToolProject} -- [verify|update]");
            return 2;
        }

        var repoRoot = FindRepoRoot();
        var baselines = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eng/baselines/public-api/SharpPack.Core.txt"] =
                PublicApiBaseline.Create(typeof(SharpPackSerializer).Assembly),
            ["eng/baselines/public-api/SharpPack.Streaming.txt"] =
                PublicApiBaseline.Create(typeof(SharpPackStreamingSerializer).Assembly),
            ["eng/baselines/generated/representative.g.cs.txt"] =
                GeneratedSourceBaseline.Create(),
        };

        return mode == "update"
            ? Update(repoRoot, baselines)
            : Verify(repoRoot, baselines);
    }

    static int Update(string repoRoot, IReadOnlyDictionary<string, string> baselines)
    {
        foreach (var (relativePath, content) in baselines)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Normalize(content), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Updated {relativePath}");
        }

        return 0;
    }

    static int Verify(string repoRoot, IReadOnlyDictionary<string, string> baselines)
    {
        var failed = false;
        foreach (var (relativePath, expected) in baselines)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Missing baseline: {relativePath}");
                failed = true;
                continue;
            }

            var actual = Normalize(File.ReadAllText(path));
            var normalizedExpected = Normalize(expected);
            if (actual == normalizedExpected)
            {
                Console.WriteLine($"Verified {relativePath}");
                continue;
            }

            failed = true;
            var (line, expectedLine, actualLine) = FirstDifference(normalizedExpected, actual);
            Console.Error.WriteLine($"Baseline mismatch: {relativePath}");
            Console.Error.WriteLine($"  first differing line: {line}");
            Console.Error.WriteLine($"  expected: {expectedLine}");
            Console.Error.WriteLine($"  actual:   {actualLine}");
        }

        if (!failed)
        {
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"Run 'dotnet run --project {ToolProject} -- update' and review the resulting baseline diff.");
        return 1;
    }

    static (int Line, string Expected, string Actual) FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var count = Math.Max(expectedLines.Length, actualLines.Length);
        for (var i = 0; i < count; i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<EOF>";
            var actualLine = i < actualLines.Length ? actualLines[i] : "<EOF>";
            if (!StringComparer.Ordinal.Equals(expectedLine, actualLine))
            {
                return (i + 1, expectedLine, actualLine);
            }
        }

        return (0, string.Empty, string.Empty);
    }

    static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd() + "\n";

    static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpPack.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing SharpPack.sln.");
    }
}

static class PublicApiBaseline
{
    static readonly BindingFlags DeclaredMembers =
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    public static string Create(Assembly assembly)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SharpPack public API baseline");
        builder.AppendLine("# Generated by tools/SharpPack.Baselines. Review intentional diffs.");
        builder.AppendLine($"assembly {assembly.GetName().Name}");

        foreach (var type in assembly.GetTypes()
                     .Where(IsExternallyVisible)
                     .OrderBy(TypeName, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.AppendLine(TypeDeclaration(type));

            foreach (var member in MemberLines(type).OrderBy(static x => x, StringComparer.Ordinal))
            {
                builder.Append("  ").AppendLine(member);
            }
        }

        return builder.ToString();
    }

    static IEnumerable<string> MemberLines(Type type)
    {
        foreach (var field in type.GetFields(DeclaredMembers))
        {
            var visibility = Visibility(field);
            if (visibility is null)
            {
                continue;
            }

            var modifiers = new List<string>();
            if (field.IsLiteral)
            {
                modifiers.Add("const");
            }
            else
            {
                if (field.IsStatic) modifiers.Add("static");
                if (field.IsInitOnly) modifiers.Add("readonly");
            }

            var suffix = field.IsLiteral
                ? $" = {FormatConstant(field.GetRawConstantValue(), field.FieldType)}"
                : string.Empty;
            yield return $"field {visibility} {JoinModifiers(modifiers)}{TypeName(field.FieldType)} {field.Name}{suffix}";
        }

        foreach (var constructor in type.GetConstructors(DeclaredMembers))
        {
            var visibility = Visibility(constructor);
            if (visibility is null)
            {
                continue;
            }

            yield return $"ctor {visibility} {type.Name.Split((char)96)[0]}({Parameters(constructor.GetParameters())})";
        }

        foreach (var property in type.GetProperties(DeclaredMembers))
        {
            var accessors = new[]
            {
                (Name: "get", Method: property.GetMethod),
                (Name: "set", Method: property.SetMethod),
            }
            .Where(static x => x.Method is not null && Visibility(x.Method) is not null)
            .Select(static x => $"{x.Name}:{Visibility(x.Method!)}")
            .ToArray();

            if (accessors.Length == 0)
            {
                continue;
            }

            var accessor = property.GetMethod ?? property.SetMethod!;
            var indexParameters = property.GetIndexParameters();
            var name = indexParameters.Length == 0
                ? property.Name
                : $"{property.Name}[{Parameters(indexParameters)}]";
            yield return
                $"property {(accessor.IsStatic ? "static " : string.Empty)}{TypeName(property.PropertyType)} {name} {{ {string.Join("; ", accessors)}; }}";
        }

        foreach (var @event in type.GetEvents(DeclaredMembers))
        {
            var methods = new[] { @event.AddMethod, @event.RemoveMethod }
                .Where(static x => x is not null && Visibility(x) is not null)
                .Select(static x => Visibility(x!)!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static x => x, StringComparer.Ordinal)
                .ToArray();

            if (methods.Length == 0)
            {
                continue;
            }

            var accessor = @event.AddMethod ?? @event.RemoveMethod!;
            yield return
                $"event {string.Join("/", methods)} {(accessor.IsStatic ? "static " : string.Empty)}{TypeName(@event.EventHandlerType!)} {@event.Name}";
        }

        foreach (var method in type.GetMethods(DeclaredMembers))
        {
            var visibility = Visibility(method);
            if (visibility is null || IsAccessor(method))
            {
                continue;
            }

            var modifiers = new List<string>();
            if (method.IsStatic) modifiers.Add("static");
            if (method.IsAbstract) modifiers.Add("abstract");
            else if (method.IsVirtual)
            {
                if (method.IsFinal) modifiers.Add("sealed override");
                else if (method.GetBaseDefinition() != method) modifiers.Add("override");
                else modifiers.Add("virtual");
            }

            var genericArguments = method.IsGenericMethodDefinition
                ? $"<{string.Join(", ", method.GetGenericArguments().Select(static x => x.Name))}>"
                : string.Empty;
            var constraints = GenericConstraints(method.GetGenericArguments());
            yield return
                $"method {visibility} {JoinModifiers(modifiers)}{ReturnType(method)} {method.Name}{genericArguments}({Parameters(method.GetParameters())}){constraints}";
        }
    }

    static string TypeDeclaration(Type type)
    {
        var parts = new List<string> { "type", TypeVisibility(type) };

        if (type.IsByRefLike)
        {
            parts.Add("ref");
        }

        if (type.IsValueType &&
            type.GetCustomAttributesData().Any(static x =>
                x.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"))
        {
            parts.Add("readonly");
        }

        if (type.IsClass && !typeof(MulticastDelegate).IsAssignableFrom(type))
        {
            if (type.IsAbstract && type.IsSealed) parts.Add("static");
            else
            {
                if (type.IsAbstract) parts.Add("abstract");
                if (type.IsSealed) parts.Add("sealed");
            }
        }

        parts.Add(TypeKind(type));
        parts.Add(TypeName(type));

        var bases = new List<string>();
        if (type.IsEnum)
        {
            bases.Add(TypeName(Enum.GetUnderlyingType(type)));
        }
        else if (!type.IsInterface && !type.IsValueType && !typeof(MulticastDelegate).IsAssignableFrom(type))
        {
            if (type.BaseType is not null && type.BaseType != typeof(object))
            {
                bases.Add(TypeName(type.BaseType));
            }
        }

        bases.AddRange(type.GetInterfaces()
            .Where(IsExternallyVisible)
            .Select(TypeName)
            .OrderBy(static x => x, StringComparer.Ordinal));

        var declaration = string.Join(" ", parts);
        if (bases.Count != 0)
        {
            declaration += " : " + string.Join(", ", bases.Distinct(StringComparer.Ordinal));
        }

        return declaration + GenericConstraints(type.GetGenericArguments());
    }

    static string TypeKind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.BaseType is not null && typeof(MulticastDelegate).IsAssignableFrom(type.BaseType)) return "delegate";
        return "class";
    }

    static bool IsExternallyVisible(Type type)
    {
        if (!type.IsNested)
        {
            return type.IsPublic;
        }

        var selfVisible = type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;
        return selfVisible && type.DeclaringType is not null && IsExternallyVisible(type.DeclaringType);
    }

    static string TypeVisibility(Type type)
    {
        if (!type.IsNested) return type.IsPublic ? "public" : "internal";
        if (type.IsNestedPublic) return "public";
        if (type.IsNestedFamily) return "protected";
        if (type.IsNestedFamORAssem) return "protected internal";
        return "internal";
    }

    static string? Visibility(MethodBase method)
    {
        if (method.IsPublic) return "public";
        if (method.IsFamily) return "protected";
        if (method.IsFamilyOrAssembly) return "protected internal";
        return null;
    }

    static string? Visibility(FieldInfo field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily) return "protected";
        if (field.IsFamilyOrAssembly) return "protected internal";
        return null;
    }

    static bool IsAccessor(MethodInfo method)
        => method.IsSpecialName &&
           (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
            method.Name.StartsWith("set_", StringComparison.Ordinal) ||
            method.Name.StartsWith("add_", StringComparison.Ordinal) ||
            method.Name.StartsWith("remove_", StringComparison.Ordinal));

    static string ReturnType(MethodInfo method)
    {
        if (!method.ReturnType.IsByRef)
        {
            return TypeName(method.ReturnType);
        }

        var elementType = method.ReturnType.GetElementType()!;
        var isReadOnly = method.ReturnParameter.GetRequiredCustomModifiers()
            .Any(static x => x.FullName == "System.Runtime.InteropServices.InAttribute");
        return $"{(isReadOnly ? "ref readonly" : "ref")} {TypeName(elementType)}";
    }

    static string Parameters(IEnumerable<ParameterInfo> parameters)
        => string.Join(", ", parameters.Select(Parameter));

    static string Parameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        var prefix = string.Empty;
        if (parameter.GetCustomAttributesData().Any(static x =>
                x.AttributeType == typeof(ParamArrayAttribute)))
        {
            prefix = "params ";
        }

        if (type.IsByRef)
        {
            type = type.GetElementType()!;
            prefix += parameter.IsOut
                ? "out "
                : parameter.IsIn
                    ? "in "
                    : "ref ";
        }

        var result = $"{prefix}{TypeName(type)} {parameter.Name}";
        if (parameter.IsOptional)
        {
            result += $" = {FormatConstant(parameter.DefaultValue, type)}";
        }

        return result;
    }

    static string GenericConstraints(IEnumerable<Type> genericArguments)
    {
        var parts = new List<string>();
        foreach (var parameter in genericArguments.Where(static x => x.IsGenericParameter))
        {
            var constraints = new List<string>();
            var attributes = parameter.GenericParameterAttributes &
                             GenericParameterAttributes.SpecialConstraintMask;
            var hasStruct = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                constraints.Add("class");
            }

            if (hasStruct)
            {
                constraints.Add("struct");
            }

            if (parameter.GetCustomAttributesData().Any(static x =>
                    x.AttributeType.FullName == "System.Runtime.CompilerServices.IsUnmanagedAttribute"))
            {
                constraints.Add("unmanaged");
            }

            constraints.AddRange(parameter.GetGenericParameterConstraints()
                .Select(TypeName)
                .Where(static x => x != "System.ValueType")
                .OrderBy(static x => x, StringComparer.Ordinal));

            if (!hasStruct &&
                (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                constraints.Add("new()");
            }

            if (constraints.Count != 0)
            {
                parts.Add($" where {parameter.Name} : {string.Join(", ", constraints.Distinct(StringComparer.Ordinal))}");
            }
        }

        return string.Concat(parts);
    }

    static string TypeName(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!) + "&";
        }

        if (type.IsPointer)
        {
            return TypeName(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) +
                   "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsFunctionPointer)
        {
            var parameters = string.Join(", ", type.GetFunctionPointerParameterTypes().Select(TypeName));
            return $"delegate*<{parameters}{(parameters.Length == 0 ? string.Empty : ", ")}{TypeName(type.GetFunctionPointerReturnType())}>";
        }

        var prefix = type.IsNested
            ? TypeName(type.DeclaringType!) + "."
            : string.IsNullOrEmpty(type.Namespace)
                ? string.Empty
                : type.Namespace + ".";

        var name = type.Name;
        var tick = name.IndexOf((char)96);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (!type.IsGenericType)
        {
            return prefix + name;
        }

        var allArguments = type.GetGenericArguments();
        var declaringArgumentCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
        var ownArguments = allArguments.Skip(declaringArgumentCount).ToArray();
        return ownArguments.Length == 0
            ? prefix + name
            : $"{prefix}{name}<{string.Join(", ", ownArguments.Select(TypeName))}>";
    }

    static string FormatConstant(object? value, Type declaredType)
    {
        if (value is null) return "null";
        if (value is DBNull or Missing) return "<missing>";
        if (declaredType.IsEnum)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        }

        return value switch
        {
            string text => "\\\"" + text.Replace("\\\\", "\\\\\\\\", StringComparison.Ordinal)\n                .Replace("\\\"", "\\\\\\\"", StringComparison.Ordinal) + "\\\"",\n            char ch => "'" + ch.ToString().Replace("'", "\\'", StringComparison.Ordinal) + "'",
            bool boolean => boolean ? "true" : "false",
            float single => single.ToString("R", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
        };
    }

    static string JoinModifiers(IEnumerable<string> modifiers)
    {
        var text = string.Join(" ", modifiers);
        return text.Length == 0 ? string.Empty : text + " ";
    }
}

static class GeneratedSourceBaseline
{
    public static string Create()
    {
        const string source = """
#nullable enable
using SharpPack;

namespace BaselineFixtures;

public sealed class OffsetFormatter : SharpPackFormatter<int>
{
    public override void Serialize<TBufferWriter>(
        ref SharpPackWriter<TBufferWriter> writer,
        scoped ref int value)
        => writer.WriteVarInt(value + 1);

    public override void Deserialize(
        ref SharpPackReader reader,
        scoped ref int value)
        => value = reader.ReadVarIntInt32() - 1;
}

public sealed class OffsetAttribute
    : SharpPackCustomFormatterAttribute<OffsetFormatter, int>
{
    public override OffsetFormatter GetFormatter() => new();
}

[SharpPackable]
public partial class Simple
{
    public int Id { get; set; }
    public string? Name { get; set; }

    [Offset]
    public int Score { get; set; }
}

[SharpPackable]
public partial struct Fixed
{
    public int Id { get; set; }
    public long Stamp { get; set; }
}

[SharpPackable(GenerateType.VersionTolerant)]
public partial class Versioned
{
    [SharpPackOrder(0)]
    public int Id { get; set; }

    [SharpPackOrder(1)]
    public string? Name { get; set; }
}

[SharpPackable(GenerateType.CircularReference)]
public partial class Node
{
    [SharpPackOrder(0)]
    public int Value { get; set; }

    [SharpPackOrder(1)]
    public Node? Next { get; set; }
}

[SharpPackable]
[SharpPackUnion(0, typeof(Cat))]
[SharpPackUnion(1, typeof(Dog))]
public partial interface IAnimal
{
}

[SharpPackable]
public partial class Cat : IAnimal
{
    public int Lives { get; set; }
}

[SharpPackable]
public partial class Dog : IAnimal
{
    public string? Name { get; set; }
}

public partial class Outer<T>
{
    [SharpPackable]
    public partial class Inner<U>
    {
        public U? Value { get; set; }
        public Simple? Payload { get; set; }
    }
}
""";

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp14,
            preprocessorSymbols: ["NET10_0_OR_GREATER"]);
        var references = TrustedPlatformReferences()
            .Append(MetadataReference.CreateFromFile(typeof(SharpPackableAttribute).Assembly.Location))
            .GroupBy(static x => x.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static x => x.First())
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "SharpPack.GeneratedBaseline",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(source, parseOptions, path: "RepresentativeModels.cs"),
            ],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SharpPackGenerator())
            .WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        var errors = generatorDiagnostics
            .Concat(updatedCompilation.GetDiagnostics())
            .Where(static x => x.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(
                "Representative generated-source fixture failed to compile:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(static x => x.ToString())));
        }

        var generated = driver.GetRunResult().Results
            .SelectMany(static x => x.GeneratedSources)
            .OrderBy(static x => x.HintName, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("# Representative generated-source snapshot");
        builder.AppendLine("# Change detector only; intentional emitter changes require baseline review.");
        builder.AppendLine("# Covers: simple object, unmanaged/exact-size, version tolerant,");
        builder.AppendLine("# circular/reference-aware, union, nested generic, custom formatter,");
        builder.AppendLine("# and context-override dependency paths.");

        foreach (var item in generated)
        {
            builder.AppendLine();
            builder.Append("===== ").Append(item.HintName).AppendLine(" =====");
            builder.Append(Normalize(item.SourceText.ToString()).TrimEnd()).AppendLine();
        }

        return builder.ToString();
    }

    static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is unavailable.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
}
