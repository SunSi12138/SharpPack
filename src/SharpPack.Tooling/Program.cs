using System.Text.Json;
using SharpPack.Generator;
using SharpPack.Tooling;

if (args is ["--help"])
{
    Console.WriteLine("Usage: dotnet SharpPack.Tooling.dll <build-options-file>");
    return 0;
}

try
{
    if (args.Length != 1) throw new ArgumentException("Expected a SharpPack build options file.");
    var options = ReadOptions(args[0]);
    using var schema = File.OpenText(Required("plan"));
    var plan = ArtifactSchema.ReadSource(schema);
    var project = Path.GetFullPath(Required("project"));
    var adopt = options.TryGetValue("adopt", out var adoption) && !string.IsNullOrEmpty(adoption)
        ? bool.Parse(adoption) : false;
    var outputs = new Dictionary<string, List<ArtifactFile>>(ArtifactStore.PathComparer);

    if (Output("typescript") is { } scripts && plan.TypeScript is { } script)
        foreach (var (name, source, isRuntime) in TypeScriptEmitter.RenderFiles(script))
            scripts.Add(new(name, source, PreserveUnowned: isRuntime));

    if (Output("serialization-info") is { } info)
        foreach (var file in plan.SerializationInfo) info.Add(new(file.FileName, file.Text));

    foreach (var (directory, files) in outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        ArtifactStore.Apply(directory, project, files, adopt);
    return 0;

    string Required(string key) => options.TryGetValue(key, out var value) && value.Length != 0
        ? value : throw new InvalidDataException($"Missing SharpPack tooling option '{key}'.");

    List<ArtifactFile>? Output(string key)
    {
        if (!options.TryGetValue(key, out var path) || path.Length == 0) return null;
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!outputs.TryGetValue(path, out var files)) outputs.Add(path, files = []);
        return files;
    }
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or JsonException or InvalidDataException)
{
    Console.Error.WriteLine($"SharpPack tooling: {error.Message}");
    return 1;
}

static Dictionary<string, string> ReadOptions(string path)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var line in File.ReadLines(path))
    {
        var equals = line.IndexOf('=');
        if (equals < 1 || !options.TryAdd(line[..equals], line[(equals + 1)..]))
            throw new InvalidDataException("Invalid SharpPack build options file.");
    }
    return options;
}
