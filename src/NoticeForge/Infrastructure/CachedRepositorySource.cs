using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NoticeForge.Application;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

/// <summary>
/// Caches texts fetched from repositories locally and doesn't fetch again (to avoid GitHub rate limits).
/// Keyed by commit (40-char SHA), so the content never changes or goes stale.
/// Only successful fetches are cached (including the answer "no LICENSE"). Rate limits and network failures are not.
/// If the commit is not a 40-char SHA (branch name, short SHA), its content may change, so nothing is cached.
/// </summary>
public sealed partial class CachedRepositorySource(IRepositorySource inner, string dir) : IRepositorySource
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Default location: %LOCALAPPDATA%\NoticeForge\cache on Windows, ~/.local/share/NoticeForge/cache elsewhere.</summary>
    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoticeForge", "cache");

    public async Task<RepositoryFiles?> GetAsync(string repositoryUrl, string commit)
    {
        if (!FullSha().IsMatch(commit))
            return await inner.GetAsync(repositoryUrl, commit);

        var path = PathFor(repositoryUrl, commit);
        if (TryRead(path, repositoryUrl, commit) is { } cached)
            return cached;

        var files = await inner.GetAsync(repositoryUrl, commit);
        if (files is { Error: null })
            Write(path, repositoryUrl, commit, files);
        return files;
    }

    string PathFor(string url, string commit)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{url}\n{commit}")))[..16];
        return Path.Combine(dir, "repositories", $"{hash}.json");
    }

    static RepositoryFiles? TryRead(string path, string url, string commit)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            // If unreadable or for something else (hash collision), treat as missing and fetch again
            if (entry == null || entry.Url != url || entry.Commit != commit) return null;
            return new RepositoryFiles(entry.Location, entry.Licenses, entry.Notices, null);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    static void Write(string path, string url, string commit, RepositoryFiles files)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Entry(url, commit, files.Location, [.. files.Licenses], [.. files.Notices]), Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            // The result can be returned even if caching fails, so don't fail
        }
    }

    sealed record Entry(string Url, string Commit, string Location, List<TextFile> Licenses, List<TextFile> Notices);

    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex FullSha();
}
