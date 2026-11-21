using System.Text.Json;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

/// <summary>
/// Reads every *.deps.json in the publish folder and collects the bundled packages.
/// Unlike project.assets.json, build-only packages (Fody, etc.) and native assets for other OSes are not included.
/// Plugins in separate folders (e.g. RealtimeDebugger's capture/) are picked up from their own deps.json.
/// With a single-file publish (PublishSingleFile) the deps.json is embedded in the exe and not in the publish folder,
/// so pass the deps.json left in obj (obj/&lt;config&gt;/&lt;tfm&gt;/&lt;rid&gt;/&lt;App&gt;.deps.json) as a file.
/// </summary>
public static class DepsJsonReader
{
    const string RuntimePackPrefix = "runtimepack.";

    /// <summary>Reads a publish folder (every *.deps.json inside) or a single deps.json file.</summary>
    public static List<Component> Read(string path)
    {
        if (File.Exists(path)) return Collect([path]);
        if (Directory.Exists(path))
            return Collect(Directory.EnumerateFiles(path, "*.deps.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        throw new FileNotFoundException($"publish folder or deps.json not found: {path}");
    }

    static List<Component> Collect(IEnumerable<string> files)
    {
        var found = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            foreach (var c in Parse(File.ReadAllBytes(file)))
                found.TryAdd($"{c.Id}/{c.Version}", c);
        return found.Values.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Version, StringComparer.Ordinal).ToList();
    }

    /// <summary>Extracts package and runtimepack entries from one deps.json's "libraries" (project entries are our own, so skipped).</summary>
    public static IEnumerable<Component> Parse(ReadOnlyMemory<byte> json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("libraries", out var libs)) yield break;
        foreach (var lib in libs.EnumerateObject())
        {
            var type = lib.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
            ComponentKind kind;
            if (type == "package") kind = ComponentKind.Package;
            else if (type == "runtimepack") kind = ComponentKind.RuntimePack;
            else continue;

            var slash = lib.Name.IndexOf('/');
            if (slash < 0) continue;
            var id = lib.Name[..slash];
            // Runtime packs appear as "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.0"
            if (kind == ComponentKind.RuntimePack && id.StartsWith(RuntimePackPrefix, StringComparison.Ordinal))
                id = id[RuntimePackPrefix.Length..];
            yield return new Component(id, lib.Name[(slash + 1)..], kind);
        }
    }
}
