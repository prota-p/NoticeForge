using System.Text.Json;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

public sealed class PolicyFileException(string message) : Exception(message);

/// <summary>
/// Reads noticeforge.json into a <see cref="NoticePolicy"/>.
/// License files it names are read here (paths relative to the config file). A missing file fails as a config error.
/// origin is where that file came from (a URL, etc.), shown as the origin in the notices file and reports. May be omitted for human-written text (shown as "config").
/// allow and extras' in use the same words (repository, distribution, both) for scope.
/// <code>
/// {
///   "allow": { "MIT": "both", "Apache-2.0": "both", "LicenseRef-Vendor-NoRedistribution": "repository" },
///   "packages": {
///     "Some.Package": { "license": "MIT", "file": "licenses/Some.Package.txt", "origin": "https://github.com/o/r/blob/abc/LICENSE", "reason": "why it was placed by hand" },
///     "Build.Only":   { "exclude": true, "reason": "not in the distribution" },
///     "SkiaSharp*":   { "reviewed": "GPL in the notice is an option; the distributed parts use MIT/BSD" }   // pattern: trailing *
///   },
///   "extras": [
///     { "name": "VS Code Codicons", "license": "CC-BY-4.0", "file": "licenses/codicons.txt", "origin": "https://github.com/microsoft/vscode-codicons/blob/abc/LICENSE", "sourceUrl": "https://github.com/microsoft/vscode-codicons", "in": "both" }
///   ]
/// }
/// </code>
/// </summary>
public static class PolicyFile
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <exception cref="PolicyFileException">Unreadable, malformed, or a referenced file is missing.</exception>
    public static NoticePolicy Load(string path)
    {
        Dto dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Options) ?? throw new PolicyFileException($"{path}: empty");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PolicyFileException($"{path}: {ex.Message}");
        }

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        TextFile? Read(string? relative, string? origin)
        {
            if (relative == null)
            {
                if (origin != null) throw new PolicyFileException($"{path}: \"origin\" ({origin}) is given without \"file\"");
                return null;
            }
            var full = Path.GetFullPath(Path.Combine(baseDir, relative));
            if (!File.Exists(full)) throw new PolicyFileException($"{path}: license file not found: {relative}");
            return new TextFile(Path.GetFileName(full), origin ?? "config", File.ReadAllText(full));
        }

        var allow = NoticePolicy.DefaultAllow;
        if (dto.Allow != null)
        {
            var d = new Dictionary<string, Scope>(StringComparer.OrdinalIgnoreCase);
            foreach (var (license, where) in dto.Allow)
            {
                d[license] = where switch
                {
                    "both" => Scope.Both,
                    "repository" => Scope.Repository,
                    // What is distributed always comes from the repository's dependencies, so "distribute but not use" makes no sense
                    "distribution" => throw new PolicyFileException($"{path}: allow \"{license}\" cannot be \"distribution\" only (what is distributed comes from the repository); use \"both\""),
                    _ => throw new PolicyFileException($"{path}: allow \"{license}\" has unknown value \"{where}\" (both or repository)"),
                };
            }
            allow = d;
        }

        var packages = new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, p) in dto.Packages ?? [])
        {
            // Patterns allow only a trailing *. A bare * (everything) or a * in the middle is rejected, as its reach is unclear
            if (id.IndexOf('*') is var star and >= 0 && (star != id.Length - 1 || id.Length == 1))
                throw new PolicyFileException($"{path}: packages \"{id}\": \"*\" is allowed only at the end of a name prefix (like \"SkiaSharp*\")");
            if (p.Reviewed != null && string.IsNullOrWhiteSpace(p.Reviewed))
                throw new PolicyFileException($"{path}: packages \"{id}\" has an empty \"reviewed\" (write what was checked and why it is fine)");
            packages[id] = new PackageOverride(p.License, Read(p.File, p.Origin), p.Exclude, p.Reason, p.Reviewed);
        }

        var extras = new List<ExtraComponent>();
        foreach (var x in dto.Extras ?? [])
        {
            if (string.IsNullOrWhiteSpace(x.Name)) throw new PolicyFileException($"{path}: every entry in extras needs a name");
            if (x.File == null) throw new PolicyFileException($"{path}: extras \"{x.Name}\" needs a license file");
            // Not optional: deciding whether it is in the repository or the distribution is itself the human's judgment
            var scope = x.In switch
            {
                "repository" => Scope.Repository,
                "distribution" => Scope.Distribution,
                "both" => Scope.Both,
                null => throw new PolicyFileException($"{path}: extras \"{x.Name}\" needs \"in\": \"repository\", \"distribution\" or \"both\""),
                _ => throw new PolicyFileException($"{path}: extras \"{x.Name}\" has unknown \"in\": \"{x.In}\" (repository, distribution or both)"),
            };
            extras.Add(new ExtraComponent(x.Name, x.Version, x.License, Read(x.File, x.Origin), x.SourceUrl, scope));
        }

        return new NoticePolicy
        {
            Allow = allow,
            Packages = packages,
            Extras = extras,
        };
    }

    sealed record Dto(Dictionary<string, string>? Allow, Dictionary<string, PackageDto>? Packages, List<ExtraDto>? Extras);
    sealed record PackageDto(string? License, string? File, string? Origin, bool Exclude, string? Reason, string? Reviewed);
    sealed record ExtraDto(string Name, string? Version, string? License, string? File, string? Origin, string? SourceUrl, string? In);
}
