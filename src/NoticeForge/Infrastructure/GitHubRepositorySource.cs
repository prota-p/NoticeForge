using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using NoticeForge.Application;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

/// <summary>
/// Fetches LICENSE* and NOTICE* from the root of a GitHub repository at a given commit.
/// Fetching by commit, not branch head, yields the texts as of when the package was built.
/// One API call lists the root (reused for the same repository and commit); contents come from raw.githubusercontent.com.
/// </summary>
public sealed partial class GitHubRepositorySource : IRepositorySource, IDisposable
{
    readonly HttpClient _http;
    readonly string? _token;
    readonly Dictionary<string, Task<RepositoryFiles?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="token">Sent only with GitHub API (api.github.com) requests, never when fetching texts (raw.githubusercontent.com).</param>
    public GitHubRepositorySource(string? token)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NoticeForge", "0.1"));
        _token = string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>Converts the URL to GitHub "owner/repo" if possible. Accepts git:// and a trailing .git or /.</summary>
    public static string? ParseRepo(string url)
    {
        var m = GitHubUrl().Match(url);
        return m.Success ? $"{m.Groups[1].Value}/{m.Groups[2].Value}" : null;
    }

    public Task<RepositoryFiles?> GetAsync(string repositoryUrl, string commit)
    {
        var repo = ParseRepo(repositoryUrl);
        if (repo == null) return Task.FromResult<RepositoryFiles?>(null);

        var key = $"{repo}@{commit}";
        if (!_cache.TryGetValue(key, out var task))
            _cache[key] = task = FetchAsync(repo, commit);
        return task;
    }

    async Task<RepositoryFiles?> FetchAsync(string repo, string commit)
    {
        // The origin shown in reports and the notices file is a URL that opens in a browser (the root or the file at that commit)
        var origin = TreeUrl(repo, commit);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/contents?ref={Uri.EscapeDataString(commit)}");
            if (_token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var res = await _http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return new(origin, [], [], $"{origin}: repository or commit not found");
            if (!res.IsSuccessStatusCode)
                return new(origin, [], [], $"{origin}: GitHub API {(int)res.StatusCode} {res.ReasonPhrase}"
                    + (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ? " (rate limit? set GITHUB_TOKEN)" : ""));

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStreamAsync());
            var licenses = new List<TextFile>();
            var notices = new List<TextFile>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "file") continue;
                var name = item.GetProperty("name").GetString()!;
                var target = LicenseFileNames.IsLicense(name) ? licenses
                    : LicenseFileNames.IsNotice(name) ? notices
                    : null;
                if (target == null) continue;
                var raw = $"https://raw.githubusercontent.com/{repo}/{commit}/{Uri.EscapeDataString(name)}";
                target.Add(new TextFile(name, BlobUrl(repo, commit, name), await _http.GetStringAsync(raw)));
            }
            return new(origin, licenses, notices, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(origin, [], [], $"{origin}: {ex.Message}");
        }
    }

    public static string TreeUrl(string repo, string commit) => $"https://github.com/{repo}/tree/{commit}";

    public static string BlobUrl(string repo, string commit, string name) =>
        $"https://github.com/{repo}/blob/{commit}/{Uri.EscapeDataString(name)}";

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"^(?:https?|git)://(?:www\.)?github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrl();
}
