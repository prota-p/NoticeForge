using NoticeForge.Application;
using NoticeForge.Domain;
using static NoticeForge.Tests.Application.Meta;

namespace NoticeForge.Tests.Application;

public class NoticeResolverTests
{
    static readonly Component Pkg = new("Sample", "1.0.0", ComponentKind.Package);

    static Task<NoticeEntry> Resolve(FakePackages packages, FakeRepositories? repos = null, NoticePolicy? policy = null) =>
        new NoticeResolver(packages, repos ?? new FakeRepositories(), policy ?? NoticePolicy.Default).ResolveAsync(Pkg, Scope.Repository);

    [Fact]
    public async Task License_in_the_package_is_used_without_asking_the_repository()
    {
        var repos = new FakeRepositories();
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT"), [Text("LICENSE.txt", "nupkg")]), repos);

        Assert.Empty(e.Problems);
        Assert.Equal("MIT", e.License);
        Assert.Equal("nupkg", Assert.Single(e.Licenses).Origin);
        Assert.Empty(repos.Requests);
    }

    [Fact]
    public async Task License_comes_from_the_repository_commit_when_the_package_has_none()
    {
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles(Repo, [Text("LICENSE", "github")], [Text("NOTICE", "github")], null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT")), repos);

        Assert.Empty(e.Problems);
        Assert.Equal("github", Assert.Single(e.Licenses).Origin);
        Assert.Equal("NOTICE", Assert.Single(e.Notices).Name);
        Assert.Equal([$"{Repo}@{Commit}"], repos.Requests);
    }

    [Fact]
    public async Task Notices_in_the_package_win_over_the_repository_root()
    {
        // The notices at the root of dotnet/dotnet also cover things unrelated to the package, so they are not added
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles(Repo, [Text("LICENSE.TXT", "github")], [Text("THIRD-PARTY-NOTICES.txt", "github")], null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT"), notices: [Text("THIRD-PARTY-NOTICES.TXT", "nupkg")]), repos);

        Assert.Equal("nupkg", Assert.Single(e.Notices).Origin);
    }

    [Fact]
    public async Task Apache_license_looks_for_a_notice_in_the_repository_even_when_the_package_has_the_license()
    {
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles(Repo, [Text("LICENSE", "github")], [Text("NOTICE", "github")], null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("Apache-2.0"), [Text("LICENSE", "nupkg")]), repos);

        Assert.Equal("nupkg", Assert.Single(e.Licenses).Origin);   // the license stays from the nupkg
        Assert.Equal("github", Assert.Single(e.Notices).Origin);
    }

    [Theory]
    [InlineData(null, null, "nuspec has no repository")]
    [InlineData(Repo, null, "nuspec has no repository commit")]
    [InlineData("https://go.microsoft.com/fwlink/?linkid=1", Commit, "repository host is not supported")]
    public async Task No_text_is_a_problem_and_says_why(string? repo, string? commit, string note)
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT", repo, commit)));

        Assert.Contains("no original license text found", e.Problems);
        Assert.Contains(e.Notes, n => n.StartsWith(note));
    }

    [Fact]
    public async Task Repository_without_a_license_file_says_where_it_looked()
    {
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles($"{Repo}/tree/{Commit}", [], [], null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT")), repos);

        Assert.Contains("no original license text found", e.Problems);
        Assert.Contains($"no LICENSE file at the repository root: {Repo}/tree/{Commit}", e.Notes);
    }

    [Fact]
    public async Task Several_license_files_at_the_repository_root_stop_until_a_person_reviews_them()
    {
        // LICENSE and LICENSE.PLATFORMTOOLS.txt in microsoft/testfx. A machine cannot decide which one applies to this package
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles(Repo,
            [Text("LICENSE", "github", "MIT"), Text("LICENSE.PLATFORMTOOLS.txt", "github", "MICROSOFT SOFTWARE LICENSE TERMS")], [], null));
        var packages = new FakePackages().Add("Sample", "1.0.0", Expression("MIT"));

        var before = await Resolve(packages, repos);
        Assert.Equal(2, before.Licenses.Count);
        Assert.Contains(before.Problems, p => p.StartsWith("repository root has 2 license files (LICENSE, LICENSE.PLATFORMTOOLS.txt)") && p.Contains("\"reviewed\""));

        var after = await Resolve(packages, repos, Policy(new PackageOverride(null, null, false, null, Reviewed: "the package is under LICENSE (MIT)")));
        Assert.Empty(after.Problems);
        Assert.Equal("config", after.By);
        Assert.Equal(["reviewed: the package is under LICENSE (MIT)"], after.Config);
        Assert.Contains(after.Notes, n => n.StartsWith("repository root has 2 license files"));   // keep what the human review was based on
    }

    [Fact]
    public async Task Unknown_license_is_a_problem_even_with_a_text()
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", NoLicense("https://aka.ms/x"), [Text("LICENSE", "nupkg")]));

        Assert.Null(e.License);
        Assert.Contains(e.Problems, p => p.StartsWith("license is unknown"));
        Assert.Contains(e.Notes, n => n.Contains("https://aka.ms/x"));
    }

    [Fact]
    public async Task License_outside_the_allow_list_is_a_problem()
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MPL-1.1"), [Text("LICENSE", "nupkg")]));
        Assert.Equal(["license MPL-1.1 is not in the allow list"], e.Problems);
    }

    const string GplText = "                    GNU GENERAL PUBLIC LICENSE\n                       Version 3, 29 June 2007";

    [Fact]
    public async Task Gpl_text_under_a_permissive_declaration_is_a_problem()
    {
        // Declared as MIT, but the fetched original license text is GPL
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT"), [Text("COPYING", "nupkg", GplText)]));
        Assert.Contains(e.Problems, p => p.StartsWith("license text COPYING contains GNU GENERAL PUBLIC LICENSE but the license is declared as MIT"));
    }

    [Fact]
    public async Task Gpl_text_is_fine_when_the_declaration_offers_a_choice_with_gpl()
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT OR GPL-3.0-only"), [Text("LICENSE", "nupkg", "MIT ...\n" + GplText)]));
        Assert.Empty(e.Problems);   // MIT can be chosen, so it also passes the allow list
    }

    [Fact]
    public async Task Declared_gpl_is_stopped_by_the_allow_list()
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("GPL-3.0-only"), [Text("COPYING", "nupkg", GplText)]));
        Assert.Equal(["license GPL-3.0-only is not in the allow list"], e.Problems);
    }

    [Fact]
    public async Task Gpl_in_a_bundled_notice_stops_until_a_person_reviews_it()
    {
        // SkiaSharp's THIRD-PARTY-NOTICES also lists parts not in the distribution (e.g. LGPL libmicrohttpd). Whether they are included needs human review
        var packages = new FakePackages().Add("Sample", "1.0.0", Expression("MIT"),
            [Text("LICENSE.txt", "nupkg", "MIT")],
            [Text("THIRD-PARTY-NOTICES.txt", "nupkg", "# libmicrohttpd\n\t\t  GNU LESSER GENERAL PUBLIC LICENSE")]);

        var before = await Resolve(packages);
        Assert.Contains(before.Problems, p => p.StartsWith("notice THIRD-PARTY-NOTICES.txt mentions GNU LESSER GENERAL PUBLIC LICENSE"));

        var after = await Resolve(packages, policy: Policy(new PackageOverride(null, null, false, null, Reviewed: "libmicrohttpd is not shipped")));
        Assert.Empty(after.Problems);
        Assert.Equal(["reviewed: libmicrohttpd is not shipped"], after.Config);
    }

    [Fact]
    public async Task Package_missing_from_the_folder_is_a_problem()
    {
        var e = await Resolve(new FakePackages());
        Assert.Contains(e.Problems, p => p.StartsWith("package not found"));
    }

    [Fact]
    public async Task Override_gives_license_and_text_for_any_version()
    {
        var policy = Policy(new PackageOverride("MIT", Text("Sample.txt", "config"), false, "nuspec has only a link"));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", NoLicense()), policy: policy);

        Assert.Empty(e.Problems);
        Assert.Equal("MIT", e.License);
        Assert.Equal("config", Assert.Single(e.Licenses).Origin);
        Assert.Equal("config", e.By);
        Assert.Equal(["license MIT: nuspec has only a link", "license text Sample.txt: nuspec has only a link"], e.Config);
    }

    [Fact]
    public async Task Found_without_the_config_is_auto()
    {
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT"), [Text("LICENSE", "nupkg")]));
        Assert.Equal("auto", e.By);
        Assert.Empty(e.Config);
    }

    [Fact]
    public async Task Override_file_fills_only_the_versions_the_machine_could_not_find()
    {
        // An override applies to any version. Original license text written for AngleSharp 1.5.0 (no commit) must not replace 1.7.0 (fetchable)
        var policy = Policy(new PackageOverride(null, Text("Sample.txt", "config"), false, "1.5.0 has no commit"));
        var packages = new FakePackages()
            .Add("Sample", "1.5.0", Expression("MIT", commit: null))
            .Add("Sample", "1.7.0", Expression("MIT"), [Text("LICENSE", "nupkg")]);
        var entries = await new NoticeResolver(packages, new FakeRepositories(), policy).ResolveAllAsync(
            [new Component("Sample", "1.5.0", ComponentKind.Package), new Component("Sample", "1.7.0", ComponentKind.Package)], Scope.Repository);

        Assert.All(entries, e => Assert.Empty(e.Problems));   // used by 1.5.0, so not reported as unneeded
        Assert.Equal(["config", "auto"], entries.Select(e => e.By));
        Assert.Equal(["config", "nupkg"], entries.Select(e => Assert.Single(e.Licenses).Origin));
    }

    [Theory]
    // An override item no version needed stops check (keeps the config in line with reality)
    [InlineData("license", "\"license\" in the config is not needed")]
    [InlineData("file", "\"file\" in the config is not needed")]
    [InlineData("reviewed", "\"reviewed\" in the config is not needed")]
    public async Task Override_not_needed_by_any_version_stops_check(string item, string problem)
    {
        var ov = item switch
        {
            "license" => new PackageOverride("MIT", null, false, null),
            "file" => new PackageOverride(null, Text("Sample.txt", "config"), false, null),
            _ => new PackageOverride(null, null, false, null, Reviewed: "looked"),
        };
        var packages = new FakePackages().Add("Sample", "1.0.0", Expression("MIT"), [Text("LICENSE", "nupkg")]);
        var resolver = new NoticeResolver(packages, new FakeRepositories(), Policy(ov));

        var check = Assert.Single(await resolver.ResolveAllAsync([Pkg], Scope.Repository));
        Assert.Contains(check.Problems, p => p.StartsWith(problem));
        // notices sees only part of the distribution, so it cannot be sure the item is unneeded
        var notices = Assert.Single(await resolver.ResolveAllAsync([Pkg], Scope.Distribution));
        Assert.Empty(notices.Problems);
    }

    [Fact]
    public async Task Reasons_for_missing_text_are_not_repeated_when_the_override_fills_it()
    {
        var policy = Policy(new PackageOverride("MIT", Text("Sample.txt", "config"), false, "no commit in nuspec"));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("MIT", Repo, commit: null)), policy: policy);

        Assert.Empty(e.Problems);
        Assert.Empty(e.Notes);
        Assert.Equal(["license text Sample.txt: no commit in nuspec"], e.Config);
    }

    [Fact]
    public async Task Override_that_disagrees_with_the_nuspec_is_a_problem()
    {
        // The license changed with a version bump, but the override is still the old one
        var policy = Policy(new PackageOverride("MIT", Text("Sample.txt", "config"), false, null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", Expression("GPL-3.0-only")), policy: policy);

        Assert.Contains(e.Problems, p => p.StartsWith("override says MIT but the nuspec says GPL-3.0-only"));
    }

    [Fact]
    public async Task Override_with_only_a_license_still_looks_for_the_text()
    {
        var repos = new FakeRepositories().Add(Repo, Commit, new RepositoryFiles(Repo, [Text("LICENSE", "github")], [], null));
        var policy = Policy(new PackageOverride("MIT", null, false, null));
        var e = await Resolve(new FakePackages().Add("Sample", "1.0.0", NoLicense()), repos, policy);

        Assert.Empty(e.Problems);
        Assert.Equal("github", Assert.Single(e.Licenses).Origin);
    }

    [Fact]
    public async Task Exclude_needs_a_reason()
    {
        var withReason = await Resolve(new FakePackages(), policy: Policy(new PackageOverride(null, null, true, "not shipped")));
        var without = await Resolve(new FakePackages(), policy: Policy(new PackageOverride(null, null, true, null)));

        Assert.True(withReason.Excluded);
        Assert.Empty(withReason.Problems);
        Assert.Equal(["excluded: not shipped"], withReason.Config);
        Assert.Contains(without.Problems, p => p.StartsWith("excluded without a reason"));
    }

    [Fact]
    public async Task Extras_are_checked_against_the_allow_list()
    {
        var policy = new NoticePolicy
        {
            Extras = [new ExtraComponent("Codicons", null, "CC-BY-4.0", Text("codicons.txt", "config"), "https://github.com/microsoft/vscode-codicons")],
        };
        var entries = await new NoticeResolver(new FakePackages(), new FakeRepositories(), policy).ResolveAllAsync([], Scope.Distribution);

        var e = Assert.Single(entries);
        Assert.Equal(ComponentKind.Extra, e.Kind);
        Assert.Equal("config", e.By);   // extras are all written by a person
        Assert.Equal(["license CC-BY-4.0 is not in the allow list"], e.Problems);
    }

    [Fact]
    public async Task Entries_are_numbered_from_one_in_order_with_extras_last()
    {
        var packages = new FakePackages()
            .Add("A", "1.0.0", Expression("MIT"), [Text("LICENSE", "nupkg")])
            .Add("B", "1.0.0", Expression("MIT"), [Text("LICENSE", "nupkg")]);
        var policy = new NoticePolicy { Extras = [new ExtraComponent("Icons", null, "MIT", Text("icons.txt", "config"), null)] };
        var entries = await new NoticeResolver(packages, new FakeRepositories(), policy).ResolveAllAsync(
            [new Component("A", "1.0.0", ComponentKind.Package), new Component("B", "1.0.0", ComponentKind.Package)], Scope.Both);

        Assert.Equal(["#1 A 1.0.0", "#2 B 1.0.0", "#3 Icons"], entries.Select(e => e.NumberedTitle));
    }

    [Theory]
    // check looks at the repository, notices at the distribution
    [InlineData(Scope.Repository, new[] { "Codicons", "Bootstrap (demo)" })]
    [InlineData(Scope.Distribution, new[] { "Codicons", "Native lib" })]
    public async Task Extras_are_taken_only_where_they_are(Scope looking, string[] expected)
    {
        var policy = new NoticePolicy
        {
            Allow = new Dictionary<string, Scope> { ["MIT"] = Scope.Both },
            Extras =
            [
                new ExtraComponent("Codicons", null, "MIT", Text("codicons.txt", "config"), null, Scope.Both),
                new ExtraComponent("Bootstrap (demo)", null, "MIT", Text("bootstrap.txt", "config"), null, Scope.Repository),
                new ExtraComponent("Native lib", null, "MIT", Text("native.txt", "config"), null, Scope.Distribution),
            ],
        };
        var entries = await new NoticeResolver(new FakePackages(), new FakeRepositories(), policy).ResolveAllAsync([], looking);

        Assert.Equal(expected, entries.Select(e => e.Name));
    }

    [Theory]
    // A license fine for use only (repository) passes check but stops notices (distribution)
    [InlineData(Scope.Repository, null)]
    [InlineData(Scope.Distribution, "license LicenseRef-NoRedistribution is allowed only in the repository (\"repository\" in allow), not in the distribution")]
    public async Task Repository_only_license_passes_check_but_stops_notices(Scope looking, string? problem)
    {
        var packages = new FakePackages().Add("Sample", "1.0.0", Expression("LicenseRef-NoRedistribution"), [Text("LICENSE", "nupkg")]);
        var policy = new NoticePolicy
        {
            Allow = new Dictionary<string, Scope> { ["MIT"] = Scope.Both, ["LicenseRef-NoRedistribution"] = Scope.Repository },
        };
        var e = Assert.Single(await new NoticeResolver(packages, new FakeRepositories(), policy).ResolveAllAsync([Pkg], looking));

        Assert.Equal(problem == null ? Array.Empty<string>() : [problem], e.Problems);
    }

    [Fact]
    public async Task Licenses_not_in_allow_at_all_are_reported_as_such_in_both_places()
    {
        var packages = new FakePackages().Add("Sample", "1.0.0", Expression("GPL-3.0-only"), [Text("LICENSE", "nupkg")]);
        foreach (var scope in new[] { Scope.Repository, Scope.Distribution })
        {
            var e = Assert.Single(await new NoticeResolver(packages, new FakeRepositories(), NoticePolicy.Default).ResolveAllAsync([Pkg], scope));
            Assert.Equal(["license GPL-3.0-only is not in the allow list"], e.Problems);
        }
    }

    const string Gpl = "\t\t  GNU LESSER GENERAL PUBLIC LICENSE";

    static FakePackages SkiaLike() => new FakePackages()
        .Add("SkiaSharp", "2.88.9", Expression("MIT"), [Text("LICENSE.txt", "nupkg", "MIT")], [Text("THIRD-PARTY-NOTICES.txt", "nupkg", Gpl)])
        .Add("SkiaSharp.NativeAssets.Win32", "2.88.9", Expression("MIT"), [Text("LICENSE.txt", "nupkg", "MIT")], [Text("THIRD-PARTY-NOTICES.txt", "nupkg", Gpl)])
        .Add("SkiaSharp.Views", "2.88.9", Expression("MIT"), [Text("LICENSE.txt", "nupkg", "MIT")]);

    static readonly Component[] Skia =
    [
        new("SkiaSharp", "2.88.9", ComponentKind.Package),
        new("SkiaSharp.NativeAssets.Win32", "2.88.9", ComponentKind.Package),
        new("SkiaSharp.Views", "2.88.9", ComponentKind.Package),
    ];

    [Fact]
    public async Task One_pattern_reviews_every_matching_package()
    {
        var policy = new NoticePolicy
        {
            Packages = new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["SkiaSharp*"] = new(null, null, false, null, Reviewed: "GPL parts are choices"),
            },
        };
        var entries = await new NoticeResolver(SkiaLike(), new FakeRepositories(), policy).ResolveAllAsync(Skia, Scope.Repository);

        Assert.All(entries, e => Assert.Empty(e.Problems));   // SkiaSharp.Views has nothing to review, but the pattern was used elsewhere, so it is not reported as unneeded
        Assert.Equal(["reviewed [SkiaSharp*]: GPL parts are choices"], entries[0].Config);   // shows which key matched
        Assert.Equal(["config", "config", "auto"], entries.Select(e => e.By));
    }

    [Fact]
    public async Task Pattern_used_by_no_package_stops_check_on_every_match()
    {
        var policy = new NoticePolicy
        {
            Packages = new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["SkiaSharp.Views*"] = new(null, null, false, null, Reviewed: "nothing to see"),
            },
        };
        var entries = await new NoticeResolver(SkiaLike(), new FakeRepositories(), policy).ResolveAllAsync(Skia, Scope.Repository);

        Assert.Contains(entries[2].Problems, p => p.StartsWith("\"reviewed\" in the config [SkiaSharp.Views*] is not needed"));
    }

    static NoticePolicy Policy(PackageOverride ov) => new()
    {
        Packages = new Dictionary<string, PackageOverride>(StringComparer.OrdinalIgnoreCase) { ["Sample"] = ov },
    };
}
