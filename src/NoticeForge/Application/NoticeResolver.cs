using NoticeForge.Domain;

namespace NoticeForge.Application;

/// <summary>
/// For each component, finds the license type and the original license text. Search order:
/// (1) LICENSE* inside the nupkg
/// (2) LICENSE* at the root of the repository and commit the nuspec points to
/// (3) only if neither worked automatically, the file from a human-written override (config packages)
/// If none yields a text, fail with "no original license text". Never fill in by guessing.
/// Things that cannot be decided automatically (a notice mentions GPL, the root has several licenses) also fail, and pass once a human reviews them and writes "reviewed" in the config.
/// The result is either ok or NG. An ok records whether it passed automatically (auto) or with help from human-written config (config).
/// </summary>
public sealed class NoticeResolver(IPackageSource packages, IRepositorySource repositories, NoticePolicy policy)
{
    // Which override items were actually used by some version (per config key; for "SkiaSharp*", by any matching package). Used to find config that is no longer needed
    readonly HashSet<string> _usedLicense = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _usedFile = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _usedReviewed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves everything and numbers entries from #1. Non-NuGet components (config extras) in the current scope are appended last.
    /// check looks at the repository (Scope.Repository); notices looks at the distribution (Scope.Distribution).
    /// check marks override items that no version needed as NG (so the config does not drift from reality).
    /// notices sees only part of the distribution, so it cannot tell an item is unneeded and skips this.
    /// </summary>
    public async Task<List<NoticeEntry>> ResolveAllAsync(IEnumerable<Component> components, Scope scope)
    {
        _usedLicense.Clear();
        _usedFile.Clear();
        _usedReviewed.Clear();
        var entries = new List<NoticeEntry>();
        foreach (var c in components)
            entries.Add(await ResolveAsync(c, scope));
        if (scope == Scope.Repository)
            ReportUnusedOverrides(entries);
        entries.AddRange(policy.Extras.Where(x => (x.In & scope) != 0).Select(x => ResolveExtra(x, scope)));
        for (int i = 0; i < entries.Count; i++)
            entries[i].Number = i + 1;
        return entries;
    }

    public async Task<NoticeEntry> ResolveAsync(Component c, Scope scope)
    {
        var e = new NoticeEntry { Name = c.Id, Version = c.Version, Kind = c.Kind, UsedBy = c.UsedBy ?? [] };
        var key = policy.FindOverrideKey(c.Id);
        var ov = key != null ? new KeyedOverride(key, policy.Packages[key]) : null;
        if (ov is { Value.Exclude: true })
        {
            e.Excluded = true;
            e.ExcludeReason = ov.Value.Reason;
            if (string.IsNullOrWhiteSpace(ov.Value.Reason)) e.Problems.Add("excluded without a reason (write \"reason\" in the config)");
            else e.Config.Add($"excluded{ov.Via}: {ov.Value.Reason}");
            return e;
        }

        var pkg = packages.Find(c.Id, c.Version);
        if (pkg == null)
        {
            e.Problems.Add("package not found in the NuGet packages folder (run restore/publish first)");
            ApplyLicenseOverride(e, ov, nuspecExpression: null);
            if (ov?.Value.File != null) UseOverrideFile(e, ov);
            Check(e, scope, [], ov);
            return e;
        }

        var meta = pkg.Metadata;
        e.Copyright = meta.Copyright;
        e.SourceUrl = meta.SourceUrl;
        e.License = meta.LicenseExpression;
        ApplyLicenseOverride(e, ov, meta.LicenseExpression);
        if (e.License == null)
            e.Notes.Add($"nuspec has no license expression (licenseUrl: {meta.LicenseUrl ?? "none"})");

        e.Licenses.AddRange(pkg.Licenses);
        e.Notices.AddRange(pkg.Notices);
        var toReview = new List<string>();
        var whyMissing = await FillFromRepositoryAsync(e, meta, toReview);

        if (e.Licenses.Count == 0 && ov?.Value.File != null)
            UseOverrideFile(e, ov);   // a human already judged why it was missing, so don't report the reasons
        else
            e.Notes.AddRange(whyMissing);

        Check(e, scope, toReview, ov);
        return e;
    }

    /// <summary>
    /// If the nupkg has no original license text, fetches it from the repository. For Apache-2.0 without a NOTICE, looks for that too.
    /// Returns the reasons the original was not found (the caller decides whether to report them; not reported when an override file fills the gap).
    /// </summary>
    async Task<List<string>> FillFromRepositoryAsync(NoticeEntry e, PackageMetadata meta, List<string> toReview)
    {
        var whyMissing = new List<string>();
        var needLicense = e.Licenses.Count == 0;
        // Apache-2.0 requires passing on the NOTICE if one exists. If the nupkg lacks it, check the repository too
        var needNotice = e.Notices.Count == 0 && (e.License?.Contains("Apache-2.0", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!needLicense && !needNotice) return whyMissing;

        if (meta.RepositoryUrl == null || meta.RepositoryCommit == null)
        {
            if (needLicense) whyMissing.Add(meta.RepositoryUrl == null ? "nuspec has no repository" : "nuspec has no repository commit");
            return whyMissing;
        }

        var files = await repositories.GetAsync(meta.RepositoryUrl, meta.RepositoryCommit);
        if (files == null)
        {
            if (needLicense) whyMissing.Add($"repository host is not supported: {meta.RepositoryUrl}");
            return whyMissing;
        }
        if (needLicense) e.Licenses.AddRange(files.Licenses);
        // A notice in the nupkg takes precedence. Root notices of large repositories like dotnet/dotnet
        // include much unrelated to the package (about 5,000 lines for Microsoft.Extensions.*)
        if (e.Notices.Count == 0) e.Notices.AddRange(files.Notices);
        if (needLicense && files.Error != null) whyMissing.Add(files.Error);
        else if (needLicense && files.Licenses.Count == 0) whyMissing.Add($"no LICENSE file at the repository root: {files.Location}");
        // When the root has several (e.g. LICENSE and LICENSE.PLATFORMTOOLS.txt in microsoft/testfx),
        // which applies to this package cannot be decided automatically. Include all and ask for human review
        else if (needLicense && files.Licenses.Count > 1)
            toReview.Add($"repository root has {files.Licenses.Count} license files ({string.Join(", ", files.Licenses.Select(t => t.Name))}); check which applies to this package");
        return whyMissing;
    }

    NoticeEntry ResolveExtra(ExtraComponent x, Scope scope)
    {
        var e = new NoticeEntry { Name = x.Name, Version = x.Version, Kind = ComponentKind.Extra, License = x.License, SourceUrl = x.SourceUrl };
        e.Config.Add("written in the config (extras)");
        if (x.File != null) e.Licenses.Add(x.File);
        Check(e, scope, [], null);
        return e;
    }

    /// <summary>Uses the override's license as the license type when the nuspec declares none. Fails if it conflicts with the declared one.</summary>
    void ApplyLicenseOverride(NoticeEntry e, KeyedOverride? ov, string? nuspecExpression)
    {
        if (ov?.Value.License is not { } license) return;
        if (nuspecExpression != null)
        {
            if (!string.Equals(nuspecExpression, license, StringComparison.OrdinalIgnoreCase))
                e.Problems.Add($"override{ov.Via} says {license} but the nuspec says {nuspecExpression} (the package's license may have changed)");
            return;
        }
        e.License = license;
        e.Config.Add($"license {license}{ov.Via}" + (ov.Value.Reason != null ? $": {ov.Value.Reason}" : ""));
        _usedLicense.Add(ov.Key);
    }

    void UseOverrideFile(NoticeEntry e, KeyedOverride ov)
    {
        e.Licenses.Add(ov.Value.File!);
        e.Config.Add($"license text {ov.Value.File!.Name}{ov.Via}" + (ov.Value.Reason != null ? $": {ov.Value.Reason}" : ""));
        _usedFile.Add(ov.Key);
    }

    /// <summary>
    /// Marks override items that no version needed as NG, on every version of the packages they apply to.
    /// A pattern ("SkiaSharp*") counts as needed if any matching package used it.
    /// </summary>
    void ReportUnusedOverrides(List<NoticeEntry> entries)
    {
        foreach (var e in entries.Where(e => !e.Excluded))
        {
            if (policy.FindOverrideKey(e.Name) is not { } key) continue;
            var ov = new KeyedOverride(key, policy.Packages[key]);
            if (ov.Value.License != null && !_usedLicense.Contains(key))
                e.Problems.Add($"\"license\" in the config{ov.Via} is not needed (the nuspec declares it for every version); remove it");
            if (ov.Value.File != null && !_usedFile.Contains(key))
                e.Problems.Add($"\"file\" in the config{ov.Via} is not needed (the original is found automatically for every version); remove it");
            if (ov.Value.Reviewed != null && !_usedReviewed.Contains(key))
                e.Problems.Add($"\"reviewed\" in the config{ov.Via} is not needed (nothing to review in any version); remove it");
        }
    }

    /// <summary>An override and its config key. When applied via a pattern, reports append [key].</summary>
    sealed record KeyedOverride(string Key, PackageOverride Value)
    {
        public string Via => NoticePolicy.IsPattern(Key) ? $" [{Key}]" : "";
    }

    /// <summary>
    /// Uses only the allow list entries for the current scope: check (repository) uses both and repository; notices (distribution) uses both only.
    /// Anything fine to use but not to redistribute (e.g. Microsoft's non-redistributable licenses) fails here if it ends up in the distribution.
    /// toReview (things that cannot be decided automatically) passes if the config has "reviewed", and fails otherwise.
    /// </summary>
    void Check(NoticeEntry e, Scope scope, List<string> toReview, KeyedOverride? ov)
    {
        if (e.Licenses.Count == 0)
            e.Problems.Add("no original license text found");
        CheckCopyleftText(e, toReview);
        Review(e, toReview, ov);
        if (e.License == null)
        {
            e.Problems.Add("license is unknown (add \"license\" for this package in the config)");
            return;
        }
        try
        {
            if (LicenseExpression.IsAllowed(e.License, policy.AllowedIn(scope))) return;
            if (scope == Scope.Distribution && LicenseExpression.IsAllowed(e.License, policy.AllowedIn(Scope.Repository)))
                e.Problems.Add($"license {e.License} is allowed only in the repository (\"repository\" in allow), not in the distribution");
            else
                e.Problems.Add($"license {e.License} is not in the allow list");
        }
        catch (FormatException ex)
        {
            e.Problems.Add(ex.Message);
        }
    }

    void Review(NoticeEntry e, List<string> toReview, KeyedOverride? ov)
    {
        if (toReview.Count == 0) return;
        if (ov?.Value.Reviewed is not { } reviewed)
        {
            foreach (var r in toReview)
                e.Problems.Add($"{r} (after checking, write \"reviewed\" for this package in the config)");
            return;
        }
        e.Config.Add($"reviewed{ov.Via}: {reviewed}");
        e.Notes.AddRange(toReview);   // keep in the report what was reviewed
        _usedReviewed.Add(ov.Key);
    }

    /// <summary>
    /// Checks for GPL-family text.
    /// In an original license text, fails unless the declared expression includes a GPL-family license (declaration and content disagree).
    /// In a notice (list of bundled parts), marks it for human review: like SkiaSharp's notice, these often list
    /// parts not actually in the distribution or licenses that are only options, so it cannot be decided automatically.
    /// Anything declared as GPL-family is stopped by the allow list instead.
    /// </summary>
    static void CheckCopyleftText(NoticeEntry e, List<string> toReview)
    {
        if (!CopyleftText.IsDeclared(e.License))
        {
            foreach (var t in e.Licenses)
            {
                var names = CopyleftText.Find(t.Content);
                if (names.Count > 0)
                    e.Problems.Add($"license text {t.Name} contains {string.Join(", ", names)} but the license is declared as {e.License ?? "unknown"} ({t.Origin})");
            }
        }
        foreach (var t in e.Notices)
        {
            var names = CopyleftText.Find(t.Content);
            if (names.Count > 0)
                toReview.Add($"notice {t.Name} mentions {string.Join(", ", names)}; check whether that part is actually distributed ({t.Origin})");
        }
    }
}
