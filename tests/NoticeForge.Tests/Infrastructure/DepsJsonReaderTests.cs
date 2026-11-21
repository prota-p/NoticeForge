using System.Text;
using NoticeForge.Domain;
using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

public class DepsJsonReaderTests
{
    const string Deps = """
        {
          "libraries": {
            "MyApp/1.0.0": { "type": "project" },
            "Serilog/4.2.0": { "type": "package" },
            "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.15": { "type": "runtimepack" },
            "Some.Reference/1.0.0": { "type": "reference" }
          }
        }
        """;

    [Fact]
    public void Takes_packages_and_runtime_packs_but_not_projects()
    {
        var components = DepsJsonReader.Parse(Encoding.UTF8.GetBytes(Deps)).ToList();
        Assert.Equal(
            [
                new Component("Serilog", "4.2.0", ComponentKind.Package),
                new Component("Microsoft.NETCore.App.Runtime.win-x64", "10.0.15", ComponentKind.RuntimePack),
            ],
            components);
    }

    [Fact]
    public void Reads_every_deps_json_under_the_folder_and_merges_duplicates()
    {
        using var dir = new TempDir();
        dir.Write("App.deps.json", """{ "libraries": { "Google.Protobuf/3.33.5": { "type": "package" }, "Serilog/4.2.0": { "type": "package" } } }""");
        // Plugins in a separate folder (RealtimeDebugger's capture/)
        dir.Write("capture/Capture.deps.json", """{ "libraries": { "Google.Protobuf/3.33.5": { "type": "package" }, "Vortice.DXGI/3.6.2": { "type": "package" } } }""");

        var ids = DepsJsonReader.Read(dir.Path).Select(c => c.Id).ToList();
        Assert.Equal(["Google.Protobuf", "Serilog", "Vortice.DXGI"], ids);
    }

    [Fact]
    public void Reads_a_single_deps_json_file()
    {
        // For a single-file publish, the deps.json left in obj is passed directly. Other deps.json files in the same folder are not read
        using var dir = new TempDir();
        var app = dir.Write("App.deps.json", """{ "libraries": { "Serilog/4.2.0": { "type": "package" } } }""");
        dir.Write("Other.deps.json", """{ "libraries": { "Vortice.DXGI/3.6.2": { "type": "package" } } }""");

        Assert.Equal(["Serilog"], DepsJsonReader.Read(app).Select(c => c.Id));
    }

    [Fact]
    public void Missing_path_is_an_error()
    {
        using var dir = new TempDir();
        Assert.Throws<FileNotFoundException>(() => DepsJsonReader.Read(System.IO.Path.Combine(dir.Path, "nothing")));
    }
}
