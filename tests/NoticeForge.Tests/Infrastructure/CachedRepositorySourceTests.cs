using NoticeForge.Application;
using NoticeForge.Domain;
using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

public class CachedRepositorySourceTests
{
    const string Url = "https://github.com/owner/repo";
    const string Sha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>A fake that counts calls and returns a fixed answer.</summary>
    sealed class CountingSource(Func<RepositoryFiles?> answer) : IRepositorySource
    {
        public int Calls { get; private set; }
        public Task<RepositoryFiles?> GetAsync(string repositoryUrl, string commit)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    static RepositoryFiles Found => new($"{Url}/tree/{Sha}", [new TextFile("LICENSE", $"{Url}/blob/{Sha}/LICENSE", "MIT text")], [new TextFile("NOTICE", "n", "notice")], null);

    [Fact]
    public async Task Second_run_reads_from_the_cache_without_asking_again()
    {
        using var dir = new TempDir();
        var inner = new CountingSource(() => Found);

        var first = await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);
        // A separate run (new instance) still reads what was saved
        var second = await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(first!.Location, second!.Location);
        Assert.Equal(first.Licenses, second.Licenses);
        Assert.Equal(first.Notices, second.Notices);
    }

    [Fact]
    public async Task No_license_answer_is_cached_too()
    {
        using var dir = new TempDir();
        var inner = new CountingSource(() => new RepositoryFiles($"{Url}/tree/{Sha}", [], [], null));

        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);
        var second = await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);

        Assert.Equal(1, inner.Calls);
        Assert.Empty(second!.Licenses);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        // Caching a rate-limited answer would stop refetching even after the limit resets
        using var dir = new TempDir();
        var inner = new CountingSource(() => new RepositoryFiles(Url, [], [], "GitHub API 403 rate limit exceeded"));

        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);
        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);

        Assert.Equal(2, inner.Calls);
    }

    [Theory]
    [InlineData("main")]          // a branch head moves
    [InlineData("0123456789ab")]  // a short SHA may come to point at another commit
    public async Task Only_full_commit_shas_are_cached(string commit)
    {
        using var dir = new TempDir();
        var inner = new CountingSource(() => Found);

        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, commit);
        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, commit);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Unsupported_hosts_are_passed_through()
    {
        using var dir = new TempDir();
        var inner = new CountingSource(() => null);
        Assert.Null(await new CachedRepositorySource(inner, dir.Path).GetAsync("https://gitlab.com/o/r", Sha));
    }

    [Fact]
    public async Task Broken_cache_file_is_fetched_again()
    {
        using var dir = new TempDir();
        var inner = new CountingSource(() => Found);
        await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);
        foreach (var f in Directory.EnumerateFiles(dir.Path, "*.json", SearchOption.AllDirectories))
            File.WriteAllText(f, "{ broken");

        var again = await new CachedRepositorySource(inner, dir.Path).GetAsync(Url, Sha);

        Assert.Equal(2, inner.Calls);
        Assert.Single(again!.Licenses);
    }
}
