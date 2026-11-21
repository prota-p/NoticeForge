using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

/// <summary>URL parsing only. Fetching from GitHub goes over the network, so unit tests skip it (verify against real builds).</summary>
public class GitHubRepositorySourceTests
{
    [Theory]
    [InlineData("https://github.com/serilog/serilog", "serilog/serilog")]
    [InlineData("https://github.com/protocolbuffers/protobuf.git", "protocolbuffers/protobuf")]
    [InlineData("https://github.com/AvaloniaUI/Avalonia/", "AvaloniaUI/Avalonia")]
    [InlineData("git://github.com/dotnet/runtime", "dotnet/runtime")]
    [InlineData("https://www.github.com/tmds/Tmds.DBus.git", "tmds/Tmds.DBus")]
    public void Parses_github_urls(string url, string expected) =>
        Assert.Equal(expected, GitHubRepositorySource.ParseRepo(url));

    [Theory]
    [InlineData("https://go.microsoft.com/fwlink/?linkid=868515")]   // the projectUrl form used by SkiaSharp and HarfBuzz
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    public void Rejects_other_urls(string url) =>
        Assert.Null(GitHubRepositorySource.ParseRepo(url));

    [Fact]
    public void Origin_urls_open_the_file_at_that_commit()
    {
        Assert.Equal("https://github.com/serilog/serilog/blob/c00e17fd/LICENSE",
            GitHubRepositorySource.BlobUrl("serilog/serilog", "c00e17fd", "LICENSE"));
        Assert.Equal("https://github.com/serilog/serilog/tree/c00e17fd",
            GitHubRepositorySource.TreeUrl("serilog/serilog", "c00e17fd"));
    }

    [Fact]
    public async Task Other_hosts_are_not_supported()
    {
        using var source = new GitHubRepositorySource(token: null);
        Assert.Null(await source.GetAsync("https://gitlab.com/owner/repo", "0123456789abcdef"));
    }
}
