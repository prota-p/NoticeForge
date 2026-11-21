using System.Text;
using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

public class LockFileReaderTests
{
    const string AppLock = """
        {
          "version": 1,
          "dependencies": {
            "net10.0": {
              "Serilog.Sinks.File": { "type": "Direct", "requested": "[7.0.0, )", "resolved": "7.0.0" },
              "Serilog": { "type": "Transitive", "resolved": "4.2.0" },
              "Fody": { "type": "Direct", "requested": "[6.8.0, )", "resolved": "6.8.0" },
              "mylib": { "type": "Project" }
            },
            "net10.0/win-x64": {
              "Microsoft.NETCore.App.Runtime.win-x64": { "type": "Direct", "requested": "[10.0.15, )", "resolved": "10.0.15" }
            }
          }
        }
        """;

    [Fact]
    public void Takes_direct_transitive_and_build_only_packages_from_every_target_but_not_projects()
    {
        var deps = LockFileReader.Parse(Encoding.UTF8.GetBytes(AppLock)).ToList();
        Assert.Equal(
            [("Serilog.Sinks.File", "7.0.0"), ("Serilog", "4.2.0"), ("Fody", "6.8.0"), ("Microsoft.NETCore.App.Runtime.win-x64", "10.0.15")],
            deps);
    }

    [Fact]
    public void Finds_lock_files_under_a_folder_skipping_build_output_and_merges_users()
    {
        using var dir = new TempDir();
        dir.Write("src/App/packages.lock.json", AppLock);
        dir.Write("tests/Unit/packages.lock.json", """
            { "version": 1, "dependencies": { "net10.0": {
              "xunit.v3": { "type": "Direct", "resolved": "4.0.3" },
              "serilog": { "type": "Transitive", "resolved": "4.2.0" } } } }
            """);
        // Lock files under bin or obj are not read
        dir.Write("src/App/bin/Debug/packages.lock.json", """{ "dependencies": { "net10.0": { "Bogus": { "type": "Direct", "resolved": "1.0.0" } } } }""");

        var components = LockFileReader.Read([dir.Path], dir.Path);

        Assert.DoesNotContain(components, c => c.Id == "Bogus");
        var serilog = Assert.Single(components, c => c.Id.Equals("Serilog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Serilog", serilog.Id);   // the spelling seen first
        Assert.Equal(["src/App", "tests/Unit"], serilog.UsedBy);
        Assert.Equal(["tests/Unit"], Assert.Single(components, c => c.Id == "xunit.v3").UsedBy);
    }

    [Fact]
    public void Accepts_a_lock_file_path_directly()
    {
        using var dir = new TempDir();
        var file = dir.Write("App/packages.lock.json", AppLock);
        Assert.Equal(4, LockFileReader.Read([file], dir.Path).Count);
    }

    [Fact]
    public void Missing_path_throws()
    {
        using var dir = new TempDir();
        Assert.Throws<DirectoryNotFoundException>(() => LockFileReader.Read([Path.Combine(dir.Path, "nope")], dir.Path));
    }
}
