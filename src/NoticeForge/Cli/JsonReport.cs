using System.Text.Encodings.Web;
using System.Text.Json;
using NoticeForge.Domain;

namespace NoticeForge.Cli;

/// <summary>
/// Machine-readable report (--report). Same content as the screen report (no original license texts themselves, only where they came from).
/// Used for diffs against the previous run, PR comments, drafting overrides, etc.
/// </summary>
public static class JsonReport
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Don't escape non-ASCII text (e.g. Japanese reasons) as \uXXXX
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Format(string command, IReadOnlyList<NoticeEntry> entries, string? noticesPath)
    {
        var report = new
        {
            command,
            noticesPath,
            summary = new
            {
                components = entries.Count,
                ok = entries.Count(e => !e.HasProblems),
                auto = entries.Count(e => !e.HasProblems && e.By == "auto"),
                config = entries.Count(e => !e.HasProblems && e.By == "config"),
                ng = entries.Count(e => e.HasProblems),
            },
            components = entries.Select(e => new
            {
                number = e.Number,
                name = e.Name,
                version = e.Version,
                kind = e.Kind.ToString(),
                // ok: passed / ng: failed. by: auto = automatic only, config = helped by human-written config (noticeforge.json)
                status = e.HasProblems ? "ng" : "ok",
                by = e.By,
                license = e.License,
                excluded = e.Excluded,
                excludeReason = e.ExcludeReason,
                licenses = e.Licenses.Select(t => new { name = t.Name, origin = t.Origin }),
                notices = e.Notices.Select(t => new { name = t.Name, origin = t.Origin }),
                problems = e.Problems,
                notes = e.Notes,
                config = e.Config,
                usedBy = e.UsedBy,
                copyright = e.Copyright,
                sourceUrl = e.SourceUrl,
            }),
        };
        return JsonSerializer.Serialize(report, Options);
    }
}
