namespace NoticeForge.Domain;

/// <summary>License-related entries in a package's nuspec.</summary>
public sealed record PackageMetadata(
    string? LicenseExpression, // <license type="expression">
    string? LicenseFile,       // path in <license type="file"> (inside the nupkg)
    string? LicenseUrl,        // <licenseUrl> (legacy; with an expression it just points to licenses.nuget.org)
    string? Copyright,
    string? ProjectUrl,
    string? RepositoryUrl,
    string? RepositoryCommit)
{
    /// <summary>URL shown as where to get the source. Fixes git:// and a trailing .git.</summary>
    public string? SourceUrl
    {
        get
        {
            if (RepositoryUrl == null) return ProjectUrl;
            var u = RepositoryUrl.Replace("git://", "https://").TrimEnd('/');
            return u.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? u[..^4] : u;
        }
    }
}
