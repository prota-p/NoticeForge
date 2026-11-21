namespace NoticeForge.Domain;

/// <summary>One entry in the notices file, with the results of resolving it.</summary>
public sealed class NoticeEntry
{
    public required string Name { get; init; }
    public string? Version { get; init; }
    public required ComponentKind Kind { get; init; }
    /// <summary>SPDX expression.</summary>
    public string? License { get; set; }
    /// <summary>From the nuspec (for reference; the original license text is authoritative).</summary>
    public string? Copyright { get; set; }
    /// <summary>Where to get the source (needed for MPL and similar).</summary>
    public string? SourceUrl { get; set; }
    public List<TextFile> Licenses { get; } = [];
    public List<TextFile> Notices { get; } = [];
    /// <summary>Any entry here fails CI (NG).</summary>
    public List<string> Problems { get; } = [];
    /// <summary>Explanations (why the original was not found, what a human review covered, etc.).</summary>
    public List<string> Notes { get; } = [];
    /// <summary>What human-written config (noticeforge.json) supplied. If any, a pass is a config ok.</summary>
    public List<string> Config { get; } = [];
    public bool HasProblems => Problems.Count > 0;
    /// <summary>How it passed. auto = automatically only, config = with help from human-written config.</summary>
    public string By => Config.Count > 0 ? "config" : "auto";
    /// <summary>For check, which projects use it.</summary>
    public IReadOnlyList<string> UsedBy { get; init; } = [];
    public bool Excluded { get; set; }
    public string? ExcludeReason { get; set; }

    /// <summary>Sequence number (from 1) that refers to the same entry in the report and the notices file. A handle for discussion; may change between runs.</summary>
    public int Number { get; set; }

    public string Title => Version != null ? $"{Name} {Version}" : Name;

    /// <summary>In the form "#3 Serilog 4.2.0". Just Title before numbering.</summary>
    public string NumberedTitle => Number > 0 ? $"#{Number} {Title}" : Title;
}
