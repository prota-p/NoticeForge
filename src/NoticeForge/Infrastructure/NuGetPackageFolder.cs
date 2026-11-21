using System.Xml.Linq;
using NoticeForge.Application;
using NoticeForge.Domain;

namespace NoticeForge.Infrastructure;

/// <summary>
/// Reads the NuGet global packages folder (restored packages).
/// Extracted nupkgs live in &lt;root&gt;/&lt;lowercase id&gt;/&lt;lowercase version&gt;/, so they can be read without opening the zip.
/// </summary>
public sealed class NuGetPackageFolder(string root) : IPackageSource
{
    /// <summary>$NUGET_PACKAGES, or ~/.nuget/packages if unset.</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public LocalPackage? Find(string id, string version)
    {
        var dir = Path.Combine(root, id.ToLowerInvariant(), version.ToLowerInvariant());
        if (!Directory.Exists(dir)) return null;
        var nuspec = Directory.EnumerateFiles(dir, "*.nuspec").FirstOrDefault();
        if (nuspec == null) return null;

        var meta = ParseNuspec(File.ReadAllText(nuspec));
        return new LocalPackage(meta, FindLicenseFiles(dir, meta), FindNoticeFiles(dir));
    }

    public static PackageMetadata ParseNuspec(string xml)
    {
        // The namespace varies with the nuspec version, so match on LocalName only
        var meta = XDocument.Parse(xml).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new FormatException("nuspec has no <metadata>");
        XElement? El(string name) => meta.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        string? Val(string name) => NullIfBlank(El(name)?.Value);

        var license = El("license");
        var licenseType = license?.Attribute("type")?.Value;
        var repo = El("repository");
        return new PackageMetadata(
            LicenseExpression: licenseType == "expression" ? NullIfBlank(license!.Value) : null,
            LicenseFile: licenseType == "file" ? NullIfBlank(license!.Value) : null,
            LicenseUrl: Val("licenseUrl"),
            Copyright: Val("copyright"),
            ProjectUrl: Val("projectUrl"),
            RepositoryUrl: NullIfBlank(repo?.Attribute("url")?.Value),
            RepositoryCommit: NullIfBlank(repo?.Attribute("commit")?.Value));
    }

    /// <summary>The file the nuspec points to, plus LICENSE* / COPYING* at the package root (including inside a LICENSE/ folder).</summary>
    static List<TextFile> FindLicenseFiles(string dir, PackageMetadata meta)
    {
        var paths = new List<string>();
        if (meta.LicenseFile != null)
        {
            // The nuspec is not trusted: only accept a path inside the package folder (no "../" or absolute paths)
            var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            var p = Path.GetFullPath(Path.Combine(dir, meta.LicenseFile.Replace('\\', '/')));
            if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(p)) paths.Add(p);
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            if (!LicenseFileNames.IsLicense(Path.GetFileName(entry))) continue;
            if (File.Exists(entry)) paths.Add(entry);
            else paths.AddRange(Directory.EnumerateFiles(entry));
        }
        return Load(dir, paths);
    }

    static List<TextFile> FindNoticeFiles(string dir) =>
        Load(dir, Directory.EnumerateFiles(dir).Where(f => LicenseFileNames.IsNotice(Path.GetFileName(f))));

    static List<TextFile> Load(string dir, IEnumerable<string> paths) =>
        paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => new TextFile(Path.GetRelativePath(dir, p).Replace('\\', '/'), "nupkg", File.ReadAllText(p)))
            .ToList();

    static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
