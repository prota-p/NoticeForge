# NoticeForge

English | [日本語](https://github.com/prota-p/NoticeForge/blob/main/README.ja.md)

A dotnet tool that builds the third-party notices file (`THIRD-PARTY-NOTICES.txt`) for what you ship, from the **original license texts**.
It fails CI (by exit code) on licenses that are not on your allow list, on licenses whose original text can't be found, and on GPL-family text that slips in.

It runs only in development and CI. It never becomes a dependency of what you ship; only the generated notices file does.

## Why

Many NuGet packages declare their license only as an SPDX expression (such as `MIT`) and don't put the license text in the nupkg.
Most notice generators then fill in a template text, so the copyright line stays as `<year> <copyright holders>`.

NoticeForge takes the original text from **the exact commit** of the repository recorded in the nuspec. If it can't find it, it stops instead of filling in a template.

## Approach

Don't release unless both `check` and `notices` pass. When one stops, a human fills the gap in `noticeforge.json` so it passes.

- What a machine can find (the original text, the license type) is automatic. Nothing for a human to do.
- What it can't find, a human decides once and writes down. It is keyed by package name, so it survives version bumps.
- When a human decision goes stale (it no longer matches what the nuspec declares), it stops again and tells you.

## Install

```sh
dotnet new tool-manifest                          # if you don't have one yet (.config/dotnet-tools.json)
dotnet tool install NoticeForge --prerelease      # pins the version in the manifest; in CI, run dotnet tool restore
dotnet tool run noticeforge check
```

## Usage

There are two commands. They differ only in where they start and whether they write the notices file. How they follow dependencies, the human decisions, the rules, and the report are shared.

| Command | When | Starts from | Question | Output |
|---|---|---|---|---|
| `check` | When you add or update dependencies (and in everyday CI) | Everything in `packages.lock.json` (transitive dependencies, build-time tools such as source generators and Fody, test dependencies) + extras with `in` set to `repository` or `both` | May we use it? Is it consistent with our repository's own license? (passes allow entries marked `both` or `repository`) | Report, exit code |
| `notices` | When you ship (release CI) | `*.deps.json` in the publish folder (what is actually shipped) + extras with `in` set to `distribution` or `both` | May we ship it? Do we meet the notice obligations? (passes only allow entries marked `both`) | Report, exit code, **the notices text** |

```sh
noticeforge check -c noticeforge.json                 # looks at every packages.lock.json under the current folder (writes no notices)
dotnet publish App/App.csproj -c Release -r win-x64 -o publish
noticeforge notices publish -c noticeforge.json       # writes publish/THIRD-PARTY-NOTICES.txt

# A single-file publish (PublishSingleFile) embeds deps.json in the exe, so pass the one left in obj (-o is required)
dotnet publish App/App.csproj -c Release -r win-x64 -p:PublishSingleFile=true -o publish
noticeforge notices App/obj/Release/net10.0/win-x64/App.deps.json -c noticeforge.json -o publish/THIRD-PARTY-NOTICES.txt
```

| | Input | Output |
|---|---|---|
| Both | The NuGet packages folder (nuspec, LICENSE/NOTICE inside the nupkg), the root of the recorded commit on GitHub (from the cache if present), `noticeforge.json` and the files it points to, `GITHUB_TOKEN` (if set) | The console report (ok/NG per item, where each text came from, reasons), the exit code, JSON with `--report` (the same content for machines), GitHub texts saved to the cache |
| `check` only | `packages.lock.json` (after restore) | In the report, the projects that use each package (`used by:`) |
| `notices` only | `*.deps.json` in the publish folder (after publish) | `THIRD-PARTY-NOTICES.txt` |

What `notices` starts from is almost a subset of what `check` starts from. The exception is runtime packs (the .NET runtime bundled by a self-contained publish, the Windows SDK projection).
The SDK fetches them separately from restore, so they appear only in deps.json, not in the lock file. They are Microsoft's own packages and go through the same rules in `notices`, so nothing slips through.

| Argument | Meaning |
|---|---|
| `notices <publish-dir>` | The output of `dotnet publish`. Reads every `*.deps.json` in it (including plugins in subfolders) |
| `notices <deps.json>` | Reads a single deps.json. For single-file publish (it stays in `obj/<configuration>/<tfm>/<rid>/`; `-o` is required) |
| `check [<path>...]` | A `packages.lock.json`, or a folder to search under (default `.`; skips `bin` and `obj`) |
| `-c, --config` | `noticeforge.json` (allow list, manual overrides, components outside NuGet) |
| `-o, --output` | Where `notices` writes the notices file (default `<publish-dir>/THIRD-PARTY-NOTICES.txt`) |
| `--report` | Also write the report as JSON (for diffing with the last run, PR comments, drafting overrides; license texts are not included) |
| `--packages` | The NuGet packages folder (default `$NUGET_PACKAGES` or `~/.nuget/packages`) |
| `--cache` | Where texts fetched from GitHub are saved (default `%LOCALAPPDATA%/NoticeForge/cache`, or `~/.local/share/NoticeForge/cache` outside Windows) |
| `--no-cache` | Don't save; fetch from GitHub every time |

Texts fetched from GitHub are saved per repository and commit, and are not fetched again. A commit (a 40-character SHA) never changes, so neither does its content.
Failed fetches (rate limits, network errors) are not saved. In CI, keep the cache folder with `actions/cache` or similar to speed things up.

If the `GITHUB_TOKEN` environment variable is set, it is used for the GitHub API. Without it, the limit is 60 requests per hour (the first run uses one per repository-and-commit pair); with it, 5,000 (1,000 for the `GITHUB_TOKEN` in Actions).

Exit codes: `0` no problems / `1` problems (fail CI) / `2` usage or input error.

## Reading the report

There are two results. For what stopped (NG), fix it, or have a human decide and write it in the config.

| Console | JSON (`status` / `by`) | Meaning | What a human does |
|---|---|---|---|
| `[ok] auto` | `ok` / `auto` | Passed on machine checks alone | Nothing |
| `[ok] config` | `ok` / `config` | Passed with something a human wrote (`license`, `file`, `reviewed`, `exclude`, extras) | Nothing (the reason is in the config; JSON `config` lists what was supplied) |
| `[NG]` | `ng` | Stopped (no original text, unknown license, not on the allow list, not reviewed by a human, an override is no longer needed) | Fix it, or write it in the config |

The console shows the full list (one line per item, for looking things up later), then the NG list (reasons and the projects using them), then a summary.
In a CI log or a terminal, the last few lines tell you whether it passed and what to do.

```
[ok] auto     #1 Avalonia 11.3.23     MIT   https://github.com/AvaloniaUI/Avalonia/blob/…/licence.md +1 notice
[ok] config   #2 Avalonia.Angle.Windows.Natives 2.1.25547.20250602   BSD-3-Clause   nupkg
…
== NG (1) ==
  #29 Ignore 0.2.1  (license unknown)
       ERROR: license is unknown (add "license" for this package in the config)
       used by: App, Tests

71 components: 70 ok (60 auto, 10 config), 1 NG.
```

## Where each thing comes from

| What | From |
|---|---|
| What is shipped | `package` and `runtimepack` entries in `*.deps.json` in the publish folder (build-only packages are not included; the self-contained .NET runtime is) |
| License type | `<license type="expression">` in the nuspec; otherwise `license` in the config |
| Original license text | (1) `LICENSE*` / `COPYING*` in the nupkg (2) if the nuspec's `<repository url commit>` is on GitHub, `LICENSE*` at the root of **that commit** (3) only if (1) and (2) found nothing, the override's `file` in the config |
| Notices (NOTICE, THIRD-PARTY-NOTICES) | Inside the nupkg; otherwise the same place as (2) |
| Copyright | Inside the original text. The nuspec `<copyright>` is added at the top of each section for reference |
| Where the source is | The nuspec repository (otherwise projectUrl) |

If (1)–(3) find nothing, it stops with "no original text". It never fills in a template.
Components with identical texts are grouped in the notices file (the text appears once, with the components that use it listed above it).

## noticeforge.json

```jsonc
{
  // Licenses to allow, and where. If omitted: MIT / Apache-2.0 / BSD-2-Clause / BSD-3-Clause as both
  //   both = may be shipped (passes both check and notices)
  //   repository = may only be used (passes check only; stops in notices if it gets shipped). E.g. licenses that forbid redistribution
  "allow": {
    "MIT": "both",
    "Apache-2.0": "both",
    "BSD-3-Clause": "both",
    "LicenseRef-Vendor-NoRedistribution": "repository"
  },
  // Keyed by package name (no version, so it survives version bumps)
  // A trailing * is a pattern ("SkiaSharp*" matches every package starting with SkiaSharp; an exact name wins, then the longest match)
  "packages": {
    // file is used only when no original text was found automatically. origin is where it came from (shown as the source in the notices and report; omit for text a human wrote)
    "Some.Package": { "license": "MIT", "file": "licenses/Some.Package.txt", "origin": "https://github.com/owner/repo/blob/<commit>/LICENSE", "reason": "the nuspec has no commit" },
    "Not.Distributed":  { "exclude": true, "reason": "not shipped" },
    // reviewed records the conclusion of a human review where a machine can't decide (GPL names in a bundled notice, several licenses at the repository root)
    "SkiaSharp*": { "reviewed": "None of the GPL/LGPL options in the notice need to be chosen (FTL for FreeType). libmicrohttpd is not shipped" }
  },
  // Components outside NuGet (icons, fonts, JS, etc. copied into the repository)
  "extras": [
    // in (required): repository = only in the repository (seen by check) / distribution = only in what is shipped (seen by notices) / both
    // sourceUrl is where the component's source lives (shown as Source: in the notices)
    { "name": "VS Code Codicons", "license": "CC-BY-4.0", "file": "licenses/codicons.txt", "origin": "https://github.com/microsoft/vscode-codicons/blob/<commit>/LICENSE", "sourceUrl": "https://github.com/microsoft/vscode-codicons", "in": "both" },
    { "name": "Bootstrap (demo)", "license": "MIT", "file": "licenses/bootstrap.txt", "in": "repository" }
  ]
}
```

The place words are shared by `allow` and the extras' `in` (repository, distribution, both). `allow` can't take `distribution` alone: what you ship always comes from the repository's dependencies, so "may ship but may not use" makes no sense.

Naming: `origin` is where the **license text** came from (same as `origin` in the report); `sourceUrl` is where the **component's source** lives (same as `Source:` in the notices and `sourceUrl` in the report).

If an override's `license` disagrees with the nuspec expression, it stops (so you notice when a package changes its license).
An override's `license` and `file` are used only when nothing was found automatically. If some versions provide them (another version of the same package, say), the automatic result is used for those.
A `license`, `file`, or `reviewed` that no version needed stops `check` (for a pattern, it counts as needed if any matching package used it). This keeps the config in line with reality. `notices` doesn't stop on this, since it sees only part of the dependencies.
Paths are relative to the config file.

Commit the config and the files `file` points to (`licenses/` etc.) to your repository. Don't commit the texts from nupkgs and GitHub; they can be fetched again at any time.

## License

[MIT](https://github.com/prota-p/NoticeForge/blob/main/LICENSE)
