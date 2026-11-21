using NoticeForge.Domain;

namespace NoticeForge.Application;

/// <summary>Reads restored packages (from the NuGet global packages folder).</summary>
public interface IPackageSource
{
    /// <summary>Null if not found.</summary>
    LocalPackage? Find(string id, string version);
}

/// <summary>A package's nuspec, plus the license and notice texts inside the nupkg.</summary>
public sealed record LocalPackage(PackageMetadata Metadata, IReadOnlyList<TextFile> Licenses, IReadOnlyList<TextFile> Notices);

/// <summary>Fetches the license and notice texts at the root of the repository recorded in the nuspec, at that commit.</summary>
public interface IRepositorySource
{
    /// <summary>Null if the host is not supported (anything other than GitHub).</summary>
    Task<RepositoryFiles?> GetAsync(string repositoryUrl, string commit);
}

/// <summary>
/// Location is a URL that opens the repository root at that commit (shown in reports).
/// Error is set when fetching failed (not found, rate limit, network failure). Fetching fine but finding no LICENSE is not an Error.
/// </summary>
public sealed record RepositoryFiles(string Location, IReadOnlyList<TextFile> Licenses, IReadOnlyList<TextFile> Notices, string? Error);
