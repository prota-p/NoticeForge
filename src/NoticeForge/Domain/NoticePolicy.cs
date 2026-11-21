namespace NoticeForge.Domain;

/// <summary>
/// What humans decide (the contents of noticeforge.json): allowed licenses, per-package overrides, components outside NuGet.
/// Overrides are keyed by package name only, without version (so upgrading does not detach them).
/// </summary>
public sealed class NoticePolicy
{
    /// <summary>The allow list when allow is not written (all may be distributed).</summary>
    public static readonly IReadOnlyDictionary<string, Scope> DefaultAllow = new Dictionary<string, Scope>(StringComparer.OrdinalIgnoreCase)
    {
        ["MIT"] = Scope.Both,
        ["Apache-2.0"] = Scope.Both,
        ["BSD-2-Clause"] = Scope.Both,
        ["BSD-3-Clause"] = Scope.Both,
    };

    public static readonly NoticePolicy Default = new();

    /// <summary>
    /// Allowed licenses and where. Both = may be distributed (check and notices); Repository = fine to use only (check only).
    /// Anything distributed always comes from the repository's dependencies, so Distribution alone never occurs.
    /// </summary>
    public IReadOnlyDictionary<string, Scope> Allow { get; init; } = DefaultAllow;

    /// <summary>Licenses allowed in the current scope.</summary>
    public IReadOnlyCollection<string> AllowedIn(Scope scope) =>
        Allow.Where(kv => (kv.Value & scope) == scope).Select(kv => kv.Key).ToList();
    public IReadOnlyDictionary<string, PackageOverride> Packages { get; init; } =
        new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ExtraComponent> Extras { get; init; } = [];

    public PackageOverride? FindOverride(string id) => FindOverrideKey(id) is { } key ? Packages[key] : null;

    /// <summary>
    /// The override key that applies to a package. An exact name match wins; otherwise the longest matching key ending with *.
    /// "SkiaSharp*" covers SkiaSharp, SkiaSharp.NativeAssets.Win32, etc. (the same decision is written once and reviewed in one place).
    /// </summary>
    public string? FindOverrideKey(string id)
    {
        if (Packages.ContainsKey(id)) return Packages.Keys.First(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
        return Packages.Keys
            .Where(k => IsPattern(k) && id.StartsWith(k[..^1], StringComparison.OrdinalIgnoreCase))
            .MaxBy(k => k.Length);
    }

    public static bool IsPattern(string key) => key.EndsWith('*');
}

/// <summary>
/// Per-package override. File's content is read when the config is loaded.
/// Reviewed is the conclusion of a human review of what cannot be decided automatically (a notice mentions GPL, the root has several licenses).
/// </summary>
public sealed record PackageOverride(string? License, TextFile? File, bool Exclude, string? Reason, string? Reviewed = null);

/// <summary>
/// Where we are looking. check looks at the repository (what is in git, what restore uses); notices looks at the distribution.
/// For extras, a human writes where each lives (e.g. a demo's Bootstrap is only in the repository; a DLL fetched at build time is only in the distribution).
/// For allow, a human writes where each is allowed (licenses fine to use but not to redistribute are Repository).
/// </summary>
[Flags]
public enum Scope
{
    Repository = 1,
    Distribution = 2,
    Both = Repository | Distribution,
}

/// <summary>A component outside NuGet (icons, fonts, JS vendored into the repository; native binaries bundled in the distribution, etc.). In is where it lives. SourceUrl is where its source is.</summary>
public sealed record ExtraComponent(string Name, string? Version, string? License, TextFile? File, string? SourceUrl, Scope In = Scope.Both);
