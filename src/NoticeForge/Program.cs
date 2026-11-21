using System.Text;
using NoticeForge.Application;
using NoticeForge.Cli;
using NoticeForge.Domain;
using NoticeForge.Infrastructure;

// noticeforge notices <publish-dir | deps.json> : when distributing. Builds the notices file for what is in the distribution (deps.json)
// noticeforge check [<path>...]     : when adding or upgrading dependencies. Checks the licenses of everything in the lock files (writes no file)
// Exit code: 0 = ok / 1 = problems found (fails CI) / 2 = usage or input error

CliOptions options;
try
{
    options = CommandLine.Parse(args);
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(CommandLine.Usage);
    return 2;
}
if (options.ShowHelp)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

// The default output goes in the publish folder. A deps.json argument (single-file publish) lives under obj, so -o is required
if (options.Command == CliCommand.Notices && options.OutputPath == null && File.Exists(options.Paths[0]))
{
    Console.Error.WriteLine("error: -o is required when a deps.json file is given (its folder is not the publish folder)");
    return 2;
}

NoticePolicy policy;
try
{
    policy = options.ConfigPath != null ? PolicyFile.Load(options.ConfigPath) : NoticePolicy.Default;
}
catch (PolicyFileException ex)
{
    Console.Error.WriteLine($"error: config: {ex.Message}");
    return 2;
}

List<Component> components;
try
{
    components = options.Command == CliCommand.Notices
        ? DepsJsonReader.Read(options.Paths[0])
        : LockFileReader.Read(options.Paths, Directory.GetCurrentDirectory());
}
catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
if (components.Count == 0 && policy.Extras.Count == 0)
    Console.Error.WriteLine(options.Command == CliCommand.Notices
        ? $"warning: no packages found in the deps.json ({options.Paths[0]})"
        : "warning: no packages found in packages.lock.json (is RestorePackagesWithLockFile on?)");

using var github = new GitHubRepositorySource(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
IRepositorySource repositories = options.NoCache
    ? github
    : new CachedRepositorySource(github, options.CacheDir ?? CachedRepositorySource.DefaultDir);
var resolver = new NoticeResolver(new NuGetPackageFolder(options.PackagesDir ?? NuGetPackageFolder.DefaultRoot), repositories, policy);

// Add only the extras in the current scope (written by a human in "in").
// check looks at the repository (consistency with the repository's license); notices looks at the distribution (the notices file)
var entries = await resolver.ResolveAllAsync(components, options.Command == CliCommand.Check ? Scope.Repository : Scope.Distribution);

string? noticesPath = null;
if (options.Command == CliCommand.Notices)
{
    noticesPath = Path.GetFullPath(options.OutputPath ?? Path.Combine(options.Paths[0], "THIRD-PARTY-NOTICES.txt"));
    WriteText(noticesPath, NoticeFormatter.Format(entries));
}
if (options.ReportPath != null)
    WriteText(Path.GetFullPath(options.ReportPath), JsonReport.Format(options.Command.ToString().ToLowerInvariant(), entries, noticesPath));

ConsoleReport.Print(Console.Out, entries, noticesPath);
return entries.Any(e => e.Problems.Count > 0) ? 1 : 0;

static void WriteText(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
