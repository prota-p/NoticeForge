using NoticeForge.Domain;
using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

public class PolicyFileTests
{
    [Fact]
    public void Loads_allow_overrides_and_extras_with_files_relative_to_the_config()
    {
        using var dir = new TempDir();
        dir.Write("licenses/ms.txt", "MS license");
        dir.Write("licenses/codicons.txt", "CC BY 4.0");
        var path = dir.Write("noticeforge.json", """
            {
              // comments and trailing commas are allowed
              "allow": { "MIT": "both", "CC-BY-4.0": "both", "LicenseRef-NoRedistribution": "repository" },
              "packages": {
                "Microsoft.Windows.SDK.NET.Ref": { "license": "LicenseRef-WinSDK", "file": "licenses/ms.txt", "reason": "nuspec has only a link" },
                "Build.Only": { "exclude": true, "reason": "not shipped" },
                "SkiaSharp*": { "reviewed": "GPL in the notice is a choice" },
              },
              "extras": [
                { "name": "VS Code Codicons", "license": "CC-BY-4.0", "file": "licenses/codicons.txt", "sourceUrl": "https://github.com/microsoft/vscode-codicons", "in": "both" }
              ]
            }
            """);

        var policy = PolicyFile.Load(path);

        Assert.Equal(Scope.Both, policy.Allow["mit"]);   // license names are case-insensitive too
        Assert.Equal(["MIT", "CC-BY-4.0"], policy.AllowedIn(Scope.Distribution));
        Assert.Equal(["MIT", "CC-BY-4.0", "LicenseRef-NoRedistribution"], policy.AllowedIn(Scope.Repository));
        var ms = policy.FindOverride("microsoft.windows.sdk.net.ref");   // package names are case-insensitive
        Assert.NotNull(ms);
        Assert.Equal("LicenseRef-WinSDK", ms.License);
        Assert.Equal(new TextFile("ms.txt", "config", "MS license"), ms.File);
        Assert.True(policy.FindOverride("Build.Only")!.Exclude);
        Assert.Equal("GPL in the notice is a choice", policy.FindOverride("SkiaSharp.NativeAssets.Win32")!.Reviewed);   // pattern
        var extra = Assert.Single(policy.Extras);
        Assert.Equal("VS Code Codicons", extra.Name);
        Assert.Equal("CC BY 4.0", extra.File!.Content);
        Assert.Equal("https://github.com/microsoft/vscode-codicons", extra.SourceUrl);
        Assert.Equal(Scope.Both, extra.In);
    }

    [Fact]
    public void Origin_is_where_the_file_came_from()
    {
        using var dir = new TempDir();
        dir.Write("licenses/a.txt", "MIT text");
        dir.Write("licenses/b.txt", "written by a person");
        var policy = PolicyFile.Load(dir.Write("noticeforge.json", """
            {
              "packages": {
                "A": { "license": "MIT", "file": "licenses/a.txt", "origin": "https://github.com/o/a/blob/abc/LICENSE" },
                "B": { "license": "MIT", "file": "licenses/b.txt" }
              },
              "extras": [ { "name": "X", "license": "MIT", "file": "licenses/a.txt", "origin": "https://example.com/x/LICENSE", "in": "distribution" } ]
            }
            """));

        Assert.Equal("https://github.com/o/a/blob/abc/LICENSE", policy.FindOverride("A")!.File!.Origin);
        Assert.Equal("config", policy.FindOverride("B")!.File!.Origin);   // text written by a person may omit the origin
        var x = Assert.Single(policy.Extras);
        Assert.Equal("https://example.com/x/LICENSE", x.File!.Origin);
        Assert.Equal(Scope.Distribution, x.In);
    }

    [Fact]
    public void Default_allow_list_when_not_written()
    {
        using var dir = new TempDir();
        var policy = PolicyFile.Load(dir.Write("noticeforge.json", "{}"));
        Assert.Equal(NoticePolicy.DefaultAllow, policy.Allow);
    }

    [Theory]
    [InlineData("""{ "packages": { "X": { "file": "missing.txt" } } }""", "missing.txt")]
    [InlineData("""{ "allow": { "MIT": "both" }, "alow": [] }""", "alow")]                       // typos are not silently ignored
    [InlineData("""{ "extras": [ { "license": "MIT", "file": "x.txt" } ] }""", "name")]
    [InlineData("""{ "extras": [ { "name": "X", "license": "MIT", "in": "both" } ] }""", "license file")]
    [InlineData("""{ "extras": [ { "name": "X", "license": "MIT", "file": "x.txt" } ] }""", "needs \"in\"")]                  // may not be omitted
    [InlineData("""{ "extras": [ { "name": "X", "license": "MIT", "file": "x.txt", "in": "repo" } ] }""", "unknown \"in\"")]
    [InlineData("""{ "extras": [ { "name": "X", "license": "MIT", "file": "x.txt", "in": "shipped" } ] }""", "unknown \"in\"")]   // old names are rejected
    [InlineData("""{ "packages": { "X": { "license": "MIT", "origin": "https://e.com/L" } } }""", "without \"file\"")]
    [InlineData("""{ "packages": { "X": { "license": "MIT", "file": "x.txt", "source": "https://e.com/L" } } }""", "source")]   // old names are rejected
    [InlineData("""{ "allow": ["MIT"] }""", "allow")]                                                                         // the old form (array) is rejected
    [InlineData("""{ "allow": { "MIT": "distribution" } }""", "cannot be \"distribution\" only")]
    [InlineData("""{ "allow": { "MIT": "everywhere" } }""", "unknown value")]
    [InlineData("""{ "packages": { "X": { "reviewed": " " } } }""", "empty \"reviewed\"")]
    [InlineData("""{ "packages": { "*": { "reviewed": "all" } } }""", "only at the end")]          // a key matching everything is rejected
    [InlineData("""{ "packages": { "Skia*Sharp": { "reviewed": "x" } } }""", "only at the end")]
    [InlineData("""{ not json""", "noticeforge.json")]
    public void Rejects_broken_configs(string json, string messagePart)
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<PolicyFileException>(() => PolicyFile.Load(dir.Write("noticeforge.json", json)));
        Assert.Contains(messagePart, ex.Message);
    }
}
