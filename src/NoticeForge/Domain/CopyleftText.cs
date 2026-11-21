using System.Text.RegularExpressions;

namespace NoticeForge.Domain;

/// <summary>
/// Checks whether a text mentions a GNU GPL-family license name (GPL, LGPL, AGPL).
/// The allow list only sees the license declared in the nuspec, so this catches mismatches like declared MIT but GPL content.
/// Only full official names are searched, so mentions like "GPL-compatible" (e.g. in MIT descriptions) are not caught by mistake.
/// </summary>
public static partial class CopyleftText
{
    /// <summary>
    /// Boilerplate that names GPL but does not mean a GPL part is included. Removed before searching.
    /// Only fixed texts that clearly read as "not GPL" or "only grants extra permission". Any deviation is not removed and gets caught.
    /// "You may choose GPL" texts (MPL/GPL/LGPL tri-license, FreeType's FTL/GPLv2) and real full texts are left out, as they need human review.
    /// </summary>
    static readonly (string Name, Regex Pattern)[] KnownBenign =
    [
        // Definition 1.12 in the MPL-2.0 full text. Present in every MPL-2.0 full text (found in grpc's LICENSE)
        ("MPL-2.0 definition of Secondary License", MplSecondaryLicense()),
        // Mono's memcheck.h note: "this one file is BSD, the rest of Valgrind is GPLv2". In .NET's THIRD-PARTY-NOTICES
        ("Valgrind memcheck.h note", ValgrindRest()),
        // Standard clause in Microsoft licenses: "you may reverse engineer to debug changes to LGPL libraries"
        // (ThirdPartyNotices of Microsoft.CodeAnalysis.Analyzers, NOTICE of Microsoft.VisualStudio.SolutionPersistence)
        ("Microsoft LGPL reverse-engineering clause", MicrosoftReverseEngineer()),
    ];

    /// <summary>Names found (as written in the text, e.g. "GNU General Public License"), without duplicates.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        foreach (var (_, pattern) in KnownBenign)
            text = pattern.Replace(text, "");
        return GnuLicense().Matches(text)
            .Select(m => Regex.Replace(m.Value, @"\s+", " "))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether the SPDX expression includes a GPL-family license (the package itself declares it, as in "MIT OR GPL-2.0-only").</summary>
    public static bool IsDeclared(string? expression) =>
        expression != null && GplId().IsMatch(expression);

    [GeneratedRegex(@"GNU\s+(?:Lesser\s+|Library\s+|Affero\s+)?General\s+Public\s+License", RegexOptions.IgnoreCase)]
    private static partial Regex GnuLicense();

    [GeneratedRegex(@"\b(?:A|L)?GPL-", RegexOptions.IgnoreCase)]
    private static partial Regex GplId();

    [GeneratedRegex(@"""Secondary\s+License""\s+means\s+either\s+the\s+GNU\s.{0,300}?any\s+later\s+versions\s+of\s+those\s+licenses\.", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MplSecondaryLicense();

    [GeneratedRegex(@"The\s+(?:entire\s+)?rest\s+of\s+Valgrind\s+is\s+licensed\s+under\s+the\s+terms\s+of\s+the\s+GNU\s+General\s+Public\s+License", RegexOptions.IgnoreCase)]
    private static partial Regex ValgrindRest();

    [GeneratedRegex(@"you\s+may\s+reverse\s+engineer\s+this\s+software\s+to\s+the\s+extent\s+required\s+to\s+debug\s+changes\s+to\s+any\s+libraries\s+licensed\s+under\s+the\s+GNU\s+Lesser\s+General\s+Public\s+License", RegexOptions.IgnoreCase)]
    private static partial Regex MicrosoftReverseEngineer();
}
