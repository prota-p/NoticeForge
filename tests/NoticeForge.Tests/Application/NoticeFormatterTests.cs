using NoticeForge.Application;
using NoticeForge.Domain;

namespace NoticeForge.Tests.Application;

public class NoticeFormatterTests
{
    static NoticeEntry Entry(string name, string license, params TextFile[] licenses)
    {
        var e = new NoticeEntry { Name = name, Version = "1.0.0", Kind = ComponentKind.Package, License = license };
        e.Licenses.AddRange(licenses);
        return e;
    }

    [Fact]
    public void Same_text_is_written_once_for_all_packages_that_share_it()
    {
        var shared = new TextFile("LICENSE", "github.com/owner/repo@abc", "MIT License\r\n\r\nCopyright (c) Owner\r\n");
        var text = NoticeFormatter.Format(
        [
            Entry("Pkg.B", "MIT", shared),
            Entry("Pkg.A", "MIT", shared with { Origin = "nupkg" }),   // same content is merged even if fetched from a different place
            Entry("Other", "BSD-3-Clause", new TextFile("LICENSE", "nupkg", "BSD text")),
        ]);

        Assert.Equal(1, Count(text, "Copyright (c) Owner"));
        Assert.Equal(1, Count(text, "BSD text"));
        Assert.DoesNotContain("\r", text);
        // The list is sorted by name
        Assert.True(text.IndexOf("  Other 1.0.0", StringComparison.Ordinal) < text.IndexOf("  Pkg.A 1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbers_appear_in_the_list_and_the_section_headers()
    {
        var e = Entry("A", "MIT", new TextFile("LICENSE", "nupkg", "x"));
        e.Number = 7;
        var text = NoticeFormatter.Format([e]);
        Assert.Contains("\n  #7 A 1.0.0  (MIT)\n", text);
        Assert.Contains("\n#7 A 1.0.0\n  License: MIT\n", text);
    }

    [Fact]
    public void Excluded_entries_are_not_written()
    {
        var excluded = Entry("Build.Only", "MIT", new TextFile("LICENSE", "nupkg", "x"));
        excluded.Excluded = true;
        Assert.DoesNotContain("Build.Only", NoticeFormatter.Format([excluded]));
    }

    [Fact]
    public void Missing_text_is_marked_and_not_merged()
    {
        var text = NoticeFormatter.Format([Entry("A", "MIT"), Entry("B", "MIT")]);
        Assert.Equal(2, Count(text, "[LICENSE TEXT NOT FOUND]"));
    }

    [Fact]
    public void Indentation_of_the_original_text_is_kept()
    {
        var text = NoticeFormatter.Format([Entry("A", "Apache-2.0", new TextFile("LICENSE", "nupkg", "﻿\n                                 Apache License\n"))]);
        Assert.Contains("\n                                 Apache License\n", text);
    }

    static int Count(string text, string part)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
        return n;
    }
}
