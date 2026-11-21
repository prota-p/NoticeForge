using System.Text.Json;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

/// <summary>
/// Reads packages.lock.json and collects every package the projects reference (used by check).
/// Unlike deps.json, this includes things not in the distribution (source generators, IL weavers like Fody, analyzers, test packages).
/// Source generators and weavers matter for source license consistency, since their generated code becomes part of our own DLL.
/// </summary>
public static class LockFileReader
{
    const string LockFileName = "packages.lock.json";
    static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules", "artifacts" };

    /// <summary>
    /// Collects from the given paths (lock files, or folders searched below).
    /// The same package and version are merged into one, listing the projects using it (lock file location, relative to baseDir).
    /// </summary>
    public static List<Component> Read(IEnumerable<string> paths, string baseDir)
    {
        // Key: lowercased id and version. Value: id for display (first spelling seen) and the projects using it
        var found = new Dictionary<(string, string), (string Id, SortedSet<string> UsedBy)>();
        foreach (var file in paths.SelectMany(FindLockFiles).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var project = Path.GetRelativePath(baseDir, Path.GetDirectoryName(Path.GetFullPath(file))!).Replace('\\', '/');
            foreach (var (id, version) in Parse(File.ReadAllBytes(file)))
            {
                var key = (id.ToLowerInvariant(), version);
                if (!found.TryGetValue(key, out var item))
                    found[key] = item = (id, new SortedSet<string>(StringComparer.Ordinal));
                item.UsedBy.Add(project);
            }
        }
        return found
            .Select(kv => new Component(kv.Value.Id, kv.Key.Item2, ComponentKind.Package, kv.Value.UsedBy.ToList()))
            .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One lock file. Takes everything except Project (Direct, Transitive, CentralTransitive) from all target frameworks and RIDs.</summary>
    public static IEnumerable<(string Id, string Version)> Parse(ReadOnlyMemory<byte> json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("dependencies", out var targets)) yield break;
        foreach (var target in targets.EnumerateObject())
        {
            foreach (var dep in target.Value.EnumerateObject())
            {
                var type = dep.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "Project") continue;
                if (!dep.Value.TryGetProperty("resolved", out var r) || r.GetString() is not { Length: > 0 } version) continue;
                yield return (dep.Name, version);
            }
        }
    }

    static IEnumerable<string> FindLockFiles(string path)
    {
        if (File.Exists(path)) return [path];
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"not found: {path}");
        return Walk(path);
    }

    static IEnumerable<string> Walk(string dir)
    {
        var file = Path.Combine(dir, LockFileName);
        if (File.Exists(file)) yield return file;
        foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal))
        {
            if (SkippedDirs.Contains(Path.GetFileName(sub))) continue;
            foreach (var f in Walk(sub)) yield return f;
        }
    }
}
