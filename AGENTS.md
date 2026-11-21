# NoticeForge

A dotnet tool that builds the third-party notices file for what you ship, and fails CI on problems. Usage and design are in [README.md](README.md).

## Principles

- **Keep it simple.** The first big goal is to be usable for shipping RealtimeDebugger. Before adding a feature, ask whether that needs it.
- **Never fill in by guessing.** If the original text can't be found, say so and fail CI.
- The tool itself has no NuGet dependencies (only what .NET provides). Tests use only xunit.v3. Ask a human before adding any.
- Dependency versions are pinned in `packages.lock.json` ([Directory.Build.props](Directory.Build.props)). Commit the lock files. Restore fails when it doesn't match them; only when adding or updating a dependency, run `dotnet restore -p:RestoreLockedMode=false` and show the lock diff to a human.
- Code comments and docs are in English. `README.ja.md` is the Japanese README; when you change README.md, update it too.

## Layout

| Path | Contents |
|---|---|
| `src/NoticeForge/Domain/` | Components, notice entries, license expressions, license file names, config model (no I/O) |
| `src/NoticeForge/Application/` | Deciding where to look for the original text (`NoticeResolver`) and building the notices. Talks to the outside only through the interfaces in `Ports.cs` |
| `src/NoticeForge/Infrastructure/` | Reading deps.json, the NuGet packages folder, GitHub, and noticeforge.json |
| `src/NoticeForge/Cli/` and `Program.cs` | Arguments, the console report, exit codes |
| `tests/NoticeForge.Tests/` | Unit tests (xUnit v3), same folder layout. No network access |

## Build and test

```sh
dotnet build
dotnet run --project tests/NoticeForge.Tests --no-build     # xUnit v3: use dotnet run, not dotnet test
```

The test platform (Microsoft.Testing.Platform) sends telemetry; set `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` to turn it off.

## Trying it on real projects

When running it against another repository's build output, only read from it, and write the notices (`-o`) to a scratch folder.
Fetching from GitHub happens only here (unit tests don't), so after changing anything around fetching, try it on a real project.

```sh
dotnet run --project src/NoticeForge --no-build -- notices <other-repo>/src/App/bin/Release/net10.0/win-x64 -o <scratch>/THIRD-PARTY-NOTICES.txt
dotnet run --project src/NoticeForge --no-build -- check      # checks NoticeForge's own lock files
```

Put example outputs under `artifacts/examples/<target>/` (ignored by git; a place for humans to look). Reports go to `check-report.txt/.json` and `notices-report.txt/.json`; config drafts to `ThirdPartyLicenses/noticeforge.json` and `ThirdPartyLicenses/licenses/`.

## Commits and releases

One line in English saying what changed.

main is protected: changes go in only through a pull request approved by the maintainer, after CI (`ci.yml`) passes. Push to a branch and open a PR; never push to main.
Merging to main runs `release.yml`. It publishes to nuget.org only when `<Version>` in `src/NoticeForge/NoticeForge.csproj` is not there yet, so a release is a PR that raises the version. Versions on nuget.org can't be deleted, so raise the version only when you mean to release.
