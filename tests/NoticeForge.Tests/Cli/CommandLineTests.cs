using NoticeForge.Cli;

namespace NoticeForge.Tests.Cli;

public class CommandLineTests
{
    [Fact]
    public void Parses_notices_with_options()
    {
        var o = CommandLine.Parse(["notices", "publish", "-c", "noticeforge.json", "--output", "out/N.txt", "--packages", "pkgs", "--cache", "cache"]);
        Assert.Equal(new CliOptions
        {
            Command = CliCommand.Notices,
            Paths = ["publish"],
            ConfigPath = "noticeforge.json",
            OutputPath = "out/N.txt",
            PackagesDir = "pkgs",
            CacheDir = "cache",
        }, o);
    }

    [Fact]
    public void Check_takes_several_paths()
    {
        var o = CommandLine.Parse(["check", "src", "tests/Unit/packages.lock.json", "-c", "noticeforge.json"]);
        Assert.Equal(CliCommand.Check, o.Command);
        Assert.Equal(["src", "tests/Unit/packages.lock.json"], o.Paths);
    }

    [Fact]
    public void Report_path_works_for_both_commands()
    {
        Assert.Equal("r.json", CommandLine.Parse(["check", "--report", "r.json"]).ReportPath);
        Assert.Equal("r.json", CommandLine.Parse(["notices", "publish", "--report", "r.json"]).ReportPath);
    }

    [Fact]
    public void Check_defaults_to_the_current_folder() =>
        Assert.Equal(["."], CommandLine.Parse(["check"]).Paths);

    [Fact]
    public void No_cache() => Assert.True(CommandLine.Parse(["notices", "publish", "--no-cache"]).NoCache);

    [Theory]
    [InlineData("--help")]
    [InlineData("check", "-h")]
    public void Help(params string[] args) => Assert.True(CommandLine.Parse(args).ShowHelp);

    [Theory]
    [InlineData]
    [InlineData("publish")]                                  // no command (old syntax)
    [InlineData("notices")]                                  // no folder
    [InlineData("notices", "a", "b")]
    [InlineData("notices", "publish", "-o")]
    [InlineData("notices", "publish", "--bogus")]
    [InlineData("notices", "publish", "--cache", "c", "--no-cache")]
    [InlineData("check", "-o", "x.txt")]                      // check writes no file
    public void Usage_errors(params string[] args) =>
        Assert.Throws<UsageException>(() => CommandLine.Parse(args));
}
