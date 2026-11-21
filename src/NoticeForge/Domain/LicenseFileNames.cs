using System.Text.RegularExpressions;

namespace NoticeForge.Domain;

/// <summary>Which file names count as original license or notice texts. The same rules apply inside the nupkg and at the repository root.</summary>
public static partial class LicenseFileNames
{
    /// <summary>LICENSE, LICENCE, COPYING (also with an extension or a suffix like -MIT).</summary>
    public static bool IsLicense(string name) => License().IsMatch(name);

    /// <summary>NOTICE, THIRD-PARTY-NOTICES, ThirdPartyNotices (also with an extension).</summary>
    public static bool IsNotice(string name) => Notice().IsMatch(name);

    [GeneratedRegex(@"^(licen[cs]e|copying)([-._].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex License();

    [GeneratedRegex(@"^(notice|third[-_]?party[-_]?notices)(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Notice();
}
