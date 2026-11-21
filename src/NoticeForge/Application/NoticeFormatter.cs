using System.Text;
using NoticeForge.Domain;

namespace NoticeForge.Application;

/// <summary>
/// Builds the notices text: a list first, then the original license texts.
/// Entries with the same original texts (identical LICENSE and NOTICE) are grouped and printed once (e.g. Avalonia's 15 packages).
/// Excluded entries are left out. Line endings are LF.
/// </summary>
public static class NoticeFormatter
{
    const string Rule = "================================================================================";
    const string SubRule = "--------------------------------------------------------------------------------";

    public static string Format(IEnumerable<NoticeEntry> all)
    {
        var entries = all.Where(e => !e.Excluded).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder();
        sb.Append("THIRD-PARTY SOFTWARE NOTICES\n\n");
        sb.Append("This software includes the third-party components listed below.\n");
        sb.Append("Each component is provided under its own license, reproduced in full in this file.\n\n");
        foreach (var e in entries)
            sb.Append($"  {e.NumberedTitle}  ({e.License ?? "UNKNOWN"})\n");
        sb.Append('\n');

        foreach (var group in entries.GroupBy(GroupKey))
        {
            var members = group.ToList();
            sb.Append(Rule).Append('\n');
            foreach (var e in members)
            {
                sb.Append(e.NumberedTitle).Append('\n');
                sb.Append($"  License: {e.License ?? "UNKNOWN"}\n");
                if (e.SourceUrl != null) sb.Append($"  Source: {e.SourceUrl}\n");
                if (e.Copyright != null) sb.Append($"  Copyright (from package metadata): {e.Copyright}\n");
            }
            var first = members[0];
            if (first.Licenses.Count == 0)
                sb.Append(SubRule).Append("\n[LICENSE TEXT NOT FOUND]\n");
            foreach (var t in first.Licenses.Concat(first.Notices))
            {
                sb.Append(SubRule).Append('\n');
                sb.Append($"{t.Name} ({t.Origin})\n\n");
                sb.Append(Normalize(t.Content)).Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Converts line endings to LF and strips the BOM and leading/trailing blank lines. Indentation of the first line (e.g. the Apache License heading) is kept as in the original.</summary>
    static string Normalize(string text) => text.Replace("\r\n", "\n").TrimStart('﻿', '\n').TrimEnd();

    /// <summary>Groups entries whose original texts are identical. Entries without an original text are never grouped.</summary>
    static string GroupKey(NoticeEntry e) =>
        e.Licenses.Count == 0
            ? "\u0001" + e.Title
            : string.Join("\u0000", e.Licenses.Concat(e.Notices).Select(t => Normalize(t.Content)));
}
