using System.Text.Json;
using NoticeForge.Cli;
using NoticeForge.Domain;

namespace NoticeForge.Tests.Cli;

public class JsonReportTests
{
    [Fact]
    public void Writes_status_sources_and_reasons_but_not_the_texts()
    {
        var ok = new NoticeEntry { Name = "Serilog", Version = "4.2.0", Kind = ComponentKind.Package, License = "Apache-2.0", Number = 1, UsedBy = ["src/App"] };
        ok.Licenses.Add(new TextFile("LICENSE", "https://github.com/serilog/serilog/blob/abc/LICENSE", "long license text"));
        var ng = new NoticeEntry { Name = "Mystery", Version = "1.0.0", Kind = ComponentKind.Package, Number = 2 };
        ng.Problems.Add("license is unknown");
        ng.Notes.Add("理由は日本語でも読める");
        var cf = new NoticeEntry { Name = "Old.Package", Version = "1.0.0", Kind = ComponentKind.Package, License = "MIT", Number = 3 };
        cf.Licenses.Add(new TextFile("LICENSE.TXT", "nupkg", "MIT"));
        cf.Config.Add("license MIT: nuspec has only licenseUrl");

        var json = JsonReport.Format("check", [ok, ng, cf], noticesPath: null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("check", root.GetProperty("command").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal([3, 2, 1, 1, 1], new[] { "components", "ok", "auto", "config", "ng" }.Select(k => summary.GetProperty(k).GetInt32()));
        var first = root.GetProperty("components")[0];
        Assert.Equal("ok", first.GetProperty("status").GetString());
        Assert.Equal("auto", first.GetProperty("by").GetString());
        Assert.Equal("https://github.com/serilog/serilog/blob/abc/LICENSE", first.GetProperty("licenses")[0].GetProperty("origin").GetString());
        Assert.Equal("src/App", first.GetProperty("usedBy")[0].GetString());
        var second = root.GetProperty("components")[1];
        Assert.Equal("ng", second.GetProperty("status").GetString());
        Assert.Equal("license is unknown", second.GetProperty("problems")[0].GetString());
        var third = root.GetProperty("components")[2];
        Assert.Equal("ok", third.GetProperty("status").GetString());
        Assert.Equal("config", third.GetProperty("by").GetString());
        Assert.Equal("license MIT: nuspec has only licenseUrl", third.GetProperty("config")[0].GetString());

        Assert.DoesNotContain("long license text", json);   // no original license text
        Assert.Contains("理由は日本語でも読める", json);       // not escaped as \uXXXX
    }
}
