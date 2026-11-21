using NoticeForge.Domain;

namespace NoticeForge.Cli;

/// <summary>
/// Console report, in three parts for different readers and situations.
/// (1) All entries, one per line: to look up later where each came from. Details are in the --report JSON
/// (2) NG entries: when CI fails, what to do (reasons, which projects use it)
/// (3) Summary: the last line, the first thing seen in a terminal or CI log
/// ok entries note whether they passed automatically (auto) or with help from human-written config (config).
/// The reasons for that config live in the config file, so they are not shown here.
/// </summary>
public static class ConsoleReport
{
    /// <summary>A null outputPath (check) means no file was written.</summary>
    public static void Print(TextWriter w, IReadOnlyList<NoticeEntry> entries, string? outputPath)
    {
        foreach (var e in entries)
        {
            var mark = e.HasProblems ? "[NG]       " : $"[ok] {e.By,-6}";
            w.WriteLine($"{mark} {"#" + e.Number,4} {e.Title,-60} {e.License ?? "?",-14} {Status(e)}");
        }

        var ng = entries.Where(e => e.HasProblems).ToList();
        if (ng.Count > 0)
        {
            w.WriteLine();
            w.WriteLine($"== NG ({ng.Count}) ==");
            foreach (var e in ng)
            {
                w.WriteLine($"{"#" + e.Number,5} {e.Title}  ({e.License ?? "license unknown"})");
                foreach (var p in e.Problems) w.WriteLine($"       ERROR: {p}");
                foreach (var n in e.Notes) w.WriteLine($"       note:  {n}");
                foreach (var c in e.Config) w.WriteLine($"       config: {c}");
                if (e.UsedBy.Count > 0) w.WriteLine($"       used by: {UsedBy(e.UsedBy)}");
            }
        }

        w.WriteLine();
        var ok = entries.Where(e => !e.HasProblems).ToList();
        var summary = $"{entries.Count} components: {ok.Count} ok ({ok.Count(e => e.By == "auto")} auto, {ok.Count(e => e.By == "config")} config), {ng.Count} NG.";
        w.WriteLine(outputPath != null ? $"{summary} Wrote {outputPath}" : summary);
    }

    /// <summary>A long list is unreadable, so show up to 3 and only count the rest.</summary>
    static string UsedBy(IReadOnlyList<string> projects) =>
        projects.Count <= 3
            ? string.Join(", ", projects)
            : $"{string.Join(", ", projects.Take(3))} and {projects.Count - 3} more";

    static string Status(NoticeEntry e)
    {
        if (e.Excluded) return $"excluded: {e.ExcludeReason ?? "(no reason)"}";
        if (e.Licenses.Count == 0) return "NO LICENSE TEXT";
        return string.Join(", ", e.Licenses.Select(t => t.Origin).Distinct())
            + (e.Notices.Count > 0 ? $" +{e.Notices.Count} notice" : "");
    }
}
