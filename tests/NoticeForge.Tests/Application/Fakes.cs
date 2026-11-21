using NoticeForge.Application;
using NoticeForge.Domain;

namespace NoticeForge.Tests.Application;

sealed class FakePackages : IPackageSource
{
    readonly Dictionary<string, LocalPackage> _packages = new(StringComparer.OrdinalIgnoreCase);

    public FakePackages Add(string id, string version, PackageMetadata meta, TextFile[]? licenses = null, TextFile[]? notices = null)
    {
        _packages[$"{id}/{version}"] = new LocalPackage(meta, licenses ?? [], notices ?? []);
        return this;
    }

    public LocalPackage? Find(string id, string version) => _packages.GetValueOrDefault($"{id}/{version}");
}

sealed class FakeRepositories : IRepositorySource
{
    readonly Dictionary<string, RepositoryFiles> _repos = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Requests { get; } = [];

    public FakeRepositories Add(string url, string commit, RepositoryFiles files)
    {
        _repos[$"{url}@{commit}"] = files;
        return this;
    }

    public Task<RepositoryFiles?> GetAsync(string repositoryUrl, string commit)
    {
        Requests.Add($"{repositoryUrl}@{commit}");
        if (!repositoryUrl.Contains("github.com")) return Task.FromResult<RepositoryFiles?>(null);
        return Task.FromResult<RepositoryFiles?>(
            _repos.GetValueOrDefault($"{repositoryUrl}@{commit}") ?? new RepositoryFiles(repositoryUrl, [], [], "not found"));
    }
}

static class Meta
{
    public const string Repo = "https://github.com/owner/repo";
    public const string Commit = "abc123";

    public static PackageMetadata Expression(string expression, string? repo = Repo, string? commit = Commit) =>
        new(expression, null, null, "Copyright (c) Owner", null, repo, commit);

    public static PackageMetadata NoLicense(string? licenseUrl = null, string? repo = Repo, string? commit = Commit) =>
        new(null, null, licenseUrl, null, null, repo, commit);

    public static TextFile Text(string name, string origin, string content = "text") => new(name, origin, content);
}
