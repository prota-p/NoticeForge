namespace NoticeForge.Cli;

public enum CliCommand
{
    /// <summary>Builds the notices file from the publish folder (or its deps.json). Used when distributing.</summary>
    Notices,
    /// <summary>Checks only the licenses of every dependency in the lock files. Used when adding or upgrading dependencies.</summary>
    Check,
}

/// <summary>What was received on the command line.</summary>
public sealed record CliOptions
{
    public CliCommand Command { get; init; }
    /// <summary>notices: one publish folder or deps.json file. check: one or more lock files or folders.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
    public string? ConfigPath { get; init; }
    public string? OutputPath { get; init; }
    /// <summary>Output path for the machine-readable report (JSON). Available for both commands.</summary>
    public string? ReportPath { get; init; }
    public string? PackagesDir { get; init; }
    public string? CacheDir { get; init; }
    public bool NoCache { get; init; }
    public bool ShowHelp { get; init; }

    // Paths is a list, so compare by contents (used in tests)
    public bool Equals(CliOptions? other) =>
        other != null && Command == other.Command && Paths.SequenceEqual(other.Paths) && ConfigPath == other.ConfigPath
        && OutputPath == other.OutputPath && ReportPath == other.ReportPath && PackagesDir == other.PackagesDir && CacheDir == other.CacheDir
        && NoCache == other.NoCache && ShowHelp == other.ShowHelp;

    public override int GetHashCode() => HashCode.Combine(Command, Paths.Count, ConfigPath, OutputPath);
}

public sealed class UsageException(string message) : Exception(message);

public static class CommandLine
{
    public const string Usage = """
        usage:
          noticeforge notices <publish-dir> [options]   write the notices file for what is distributed
          noticeforge notices <deps.json> -o <file>     same, for a single-file publish
          noticeforge check [<path>...] [options]       check the licenses of every locked dependency

          notices <publish-dir>  folder produced by `dotnet publish`; every *.deps.json under it is read
          notices <deps.json>    one deps.json; for PublishSingleFile, where it is bundled into the exe,
                                 use obj/<config>/<tfm>/<rid>/<App>.deps.json (-o is then required)
          check <path>           packages.lock.json, or a folder searched for them (default: .)
                                 build-only packages (source generators, weavers, analyzers, tests) are included

        options:
          -c, --config <file>  noticeforge.json (allowed licenses, per-package overrides, extra components);
                               both commands use the same file, and both check the extras
          -o, --output <file>  notices: file to write (default: <publish-dir>/THIRD-PARTY-NOTICES.txt)
          --report <file>      also write the report as JSON (for tools; the screen report stays text)
          --packages <dir>     NuGet global packages folder (default: $NUGET_PACKAGES or ~/.nuget/packages)
          --cache <dir>        where texts fetched from GitHub are kept, per commit
                               (default: %LOCALAPPDATA%/NoticeForge/cache or ~/.local/share/NoticeForge/cache)
          --no-cache           always fetch from GitHub and keep nothing

        environment:
          GITHUB_TOKEN         used for GitHub API requests (60 -> 5,000 requests per hour)

        exit code: 0 = ok, 1 = problems found, 2 = usage or input error
        """;

    /// <exception cref="UsageException">Invalid arguments.</exception>
    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) throw new UsageException("command is required (notices or check)");
        if (args[0] is "-h" or "--help") return new CliOptions { ShowHelp = true };
        var command = args[0] switch
        {
            "notices" => CliCommand.Notices,
            "check" => CliCommand.Check,
            _ => throw new UsageException($"unknown command: {args[0]} (notices or check)"),
        };

        var o = new CliOptions { Command = command };
        var paths = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new UsageException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "-c" or "--config": o = o with { ConfigPath = Value() }; break;
                case "-o" or "--output": o = o with { OutputPath = Value() }; break;
                case "--report": o = o with { ReportPath = Value() }; break;
                case "--packages": o = o with { PackagesDir = Value() }; break;
                case "--cache": o = o with { CacheDir = Value() }; break;
                case "--no-cache": o = o with { NoCache = true }; break;
                case "-h" or "--help": return new CliOptions { ShowHelp = true };
                default:
                    if (args[i].StartsWith('-')) throw new UsageException($"unknown option: {args[i]}");
                    paths.Add(args[i]);
                    break;
            }
        }

        if (o.NoCache && o.CacheDir != null) throw new UsageException("--cache and --no-cache cannot be used together");
        if (command == CliCommand.Notices)
        {
            if (paths.Count != 1) throw new UsageException("notices needs exactly one publish folder or deps.json");
        }
        else
        {
            if (o.OutputPath != null) throw new UsageException("check does not write a file (-o is for notices)");
            if (paths.Count == 0) paths.Add(".");
        }
        return o with { Paths = paths };
    }
}
