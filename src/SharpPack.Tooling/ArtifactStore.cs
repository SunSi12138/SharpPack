using System.Text;
using System.Text.Json;

namespace SharpPack.Tooling;

internal sealed record ArtifactFile(string Name, string Text, bool PreserveUnowned = false);

internal static class ArtifactStore
{
    const string ManifestName = ".sharppack-artifacts.json";
    const string LockName = ".sharppack-artifacts.lock";
    const int SchemaVersion = 1;
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };
    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static void Apply(string directory, string project, IReadOnlyList<ArtifactFile> output, bool adopt)
    {
        var manifestPath = Path.Combine(directory, ManifestName);
        if (output.Count == 0 && Kind(manifestPath) == FileKind.Missing) return;

        // Validate the whole output set before touching any of its files.
        var requested = Index(output, file => file.Name);
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, LockName);
        EnsurePlainOrMissing(lockPath);
        using var directoryLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var manifest = Load(manifestPath, Path.GetRelativePath(directory, project));
        if (manifest.Pending is not null)
        {
            // A prior run wrote its journal before starting any file moves.
            // Each move is safe to repeat; modified or foreign files are never taken over.
            manifest = Complete(directory, manifestPath, manifest);
        }

        var saved = Index(manifest.Committed, file => file.Name);
        var desired = new List<OwnedFile>(requested.Count);
        foreach (var artifact in requested.Values.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, artifact.Name);
            var kind = EnsurePlainOrMissing(path);
            if (kind == FileKind.Plain)
            {
                if (saved.TryGetValue(artifact.Name, out var owned))
                {
                    if (!Matches(path, owned.Text))
                    {
                        if (artifact.PreserveUnowned) continue;
                        throw new IOException($"Generated file '{path}' was edited. Move it before rebuilding.");
                    }
                }
                else if (!adopt || !Matches(path, artifact.Text))
                {
                    if (artifact.PreserveUnowned) continue;
                    throw new IOException($"'{path}' is not owned by SharpPack. Move it, change the output directory, or explicitly adopt matching legacy output.");
                }
            }
            desired.Add(new(artifact.Name, artifact.Text));
        }

        if (manifest.Committed.SequenceEqual(desired) &&
            desired.All(file => Kind(Path.Combine(directory, file.Name)) == FileKind.Plain)) return;

        var transition = manifest with { Pending = desired.ToArray() };
        Save(manifestPath, transition);
        Complete(directory, manifestPath, transition);
    }

    static Manifest Complete(string directory, string manifestPath, Manifest transition)
    {
        var previous = Index(transition.Committed, file => file.Name);
        var next = Index(transition.Pending!, file => file.Name);

        foreach (var file in next.Values)
        {
            var path = Path.Combine(directory, file.Name);
            var kind = EnsurePlainOrMissing(path);
            if (kind == FileKind.Plain)
            {
                if (Matches(path, file.Text)) continue;
                if (!previous.TryGetValue(file.Name, out var owned) || !Matches(path, owned.Text))
                    throw new IOException($"'{path}' changed during SharpPack generation. Move it before rebuilding.");
            }

            AtomicWrite(path, file.Text, replace: kind == FileKind.Plain);
        }

        foreach (var file in previous.Values)
        {
            if (next.ContainsKey(file.Name)) continue;
            var path = Path.Combine(directory, file.Name);
            // A removed model relinquishes ownership of an edited file rather
            // than deleting the user's edits or blocking future builds.
            if (Kind(path) == FileKind.Plain && Matches(path, file.Text)) File.Delete(path);
        }

        var committed = transition with { Committed = transition.Pending!, Pending = null };
        Save(manifestPath, committed);
        return committed;
    }

    static Manifest Load(string path, string project)
    {
        if (EnsurePlainOrMissing(path) == FileKind.Missing) return new(SchemaVersion, project, [], null);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path, Utf8));
        if (manifest?.Committed is null || manifest.Version != SchemaVersion)
            throw new InvalidDataException($"Invalid SharpPack ownership manifest '{path}'.");
        if (!PathComparer.Equals(manifest.Project, project))
            throw new IOException($"SharpPack output directory '{Path.GetDirectoryName(path)}' already belongs to another project.");
        return manifest;
    }

    static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> nameOf)
    {
        var byName = new Dictionary<string, T>(PathComparer);
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException("Missing SharpPack artifact entry.");
            var name = nameOf(item);
            if (string.IsNullOrEmpty(name) || !name.Equals(Path.GetFileName(name), StringComparison.Ordinal) ||
                name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !(name.EndsWith(".ts", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal)))
                throw new InvalidDataException($"'{name}' is not a valid SharpPack output filename.");
            if (!byName.TryAdd(name, item)) throw new InvalidDataException($"Multiple SharpPack artifacts are named '{name}'.");
        }
        return byName;
    }

    static void Save(string path, Manifest manifest)
        => AtomicWrite(path, JsonSerializer.Serialize(manifest, ManifestJson) + "\n", replace: EnsurePlainOrMissing(path) == FileKind.Plain);

    static bool Matches(string path, string expected)
        => File.ReadAllBytes(path).AsSpan().SequenceEqual(Utf8.GetBytes(expected));

    static FileKind EnsurePlainOrMissing(string path)
    {
        var kind = Kind(path);
        if (kind == FileKind.Other) throw new IOException($"'{path}' is a directory or link, not a SharpPack artifact file.");
        return kind;
    }

    static FileKind Kind(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                ? FileKind.Other : FileKind.Plain;
        }
        catch (FileNotFoundException) { return FileKind.Missing; }
        catch (DirectoryNotFoundException) { return FileKind.Missing; }
    }

    static void AtomicWrite(string path, string text, bool replace)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".sharppack-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                created = true;
                using var writer = new StreamWriter(stream, Utf8);
                writer.Write(text);
            }
            File.Move(temporary, path, overwrite: replace);
        }
        finally
        {
            if (created) File.Delete(temporary);
        }
    }

    enum FileKind { Missing, Plain, Other }
    sealed record OwnedFile(string Name, string Text);
    sealed record Manifest(int Version, string Project, OwnedFile[] Committed, OwnedFile[]? Pending);
}
