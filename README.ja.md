# NoticeForge

[English](README.md) | 日本語

配布物に同梱する第三者ライセンスの告知ファイル（`THIRD-PARTY-NOTICES.txt`）を、**原本のライセンス文**から作る dotnet tool。
許可リストにないライセンスや、原本が取れないもの、GPL 系の文が紛れ込んだものがあれば、終了コードで CI を落とす。

開発と CI でだけ使う。配布物の依存にはならない（入るのは生成した告知ファイルだけ）。

## なぜ作ったか

NuGet のパッケージの多くは、ライセンスを SPDX 式（`MIT` など）でだけ申告し、原本の文を nupkg に入れていない。
告知を作るツールの多くは、そのときひな形の文で埋めるので、著作権表示が `<year> <copyright holders>` のまま残る。

NoticeForge は、nuspec に記録されたリポジトリの**そのコミット**から原本の文を取る。取れなければ、ひな形で埋めずに止める。

## 考え方

check と notices の両方が通らないとリリースしない。止まったら、足りないところを人が `noticeforge.json` に書いて埋め、通るようにする。

- 機械で取れるもの（原本・種類）は自動。人は何もしない
- 取れないものは、人が一度判断して書く。パッケージ名で結び付くので、版を上げても書き直さない
- 人の判断が古くなったら（nuspec の申告と食い違ったら）、また止まって知らせる

## 入れ方

```sh
dotnet new tool-manifest                          # まだ無ければ（dotnet-tools.json。SDK 10 より前は .config/ の下）
dotnet tool install NoticeForge --prerelease      # 版がマニフェストに固定される。CI では dotnet tool restore
dotnet tool run noticeforge check
```

## 使い方

コマンドは 2 つ。違うのは起点の一覧と、告知を書くかだけ。辿る先・人の判断・判定の決まり・報告の形は共通。

| コマンド | いつ | 起点 | 問い | 出力 |
|---|---|---|---|---|
| `check` | 依存を足す・上げるとき（ふだんの CI でも） | `packages.lock.json` の全部（間接の依存、ソース生成器・Fody のようなビルド用の道具、テスト用も）＋ extras の `in` が `repository`・`both` のもの | 使ってよいか。自分のリポジトリのライセンスと整合するか（allow の `both`・`repository` で通す） | 報告・終了コード |
| `notices` | 配るとき（リリースの CI） | publish 先の `*.deps.json`（配布物に実際に入っているもの）＋ extras の `in` が `distribution`・`both` のもの | 配ってよいか。告知の義務を果たしているか（allow の `both` だけで通す） | 報告・終了コード・**告知のテキスト** |

```sh
noticeforge check -c noticeforge.json                 # カレントの下の packages.lock.json を全部見る（告知は書かない）
dotnet publish App/App.csproj -c Release -r win-x64 -o publish
noticeforge notices publish -c noticeforge.json       # publish/THIRD-PARTY-NOTICES.txt を書く

# 単一ファイルの publish（PublishSingleFile）は deps.json が exe に埋め込まれるので、obj に残るものを渡す（-o が要る）
dotnet publish App/App.csproj -c Release -r win-x64 -p:PublishSingleFile=true -o publish
noticeforge notices App/obj/Release/net10.0/win-x64/App.deps.json -c noticeforge.json -o publish/THIRD-PARTY-NOTICES.txt
```

lock を locked mode（`RestoreLockedMode`）で使うとき、`-p:PublishSingleFile=true` は `Microsoft.NET.ILLink.Tasks` の参照を足す。普通の restore で作った lock にはないので、publish が NU1004 で落ちる。アプリの csproj に `<EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>` を書いて参照を常に入れ、`-p:PublishSingleFile=true` は今までどおりコマンドで渡す。csproj に `<PublishSingleFile>` は書かない。SDK がビルドするマシン自身の RID を足すので、その RID が `RuntimeIdentifiers` にないマシン（GitHub の macos-latest の osx-arm64 など）で restore が NU1004（「runtime identifiers have changed」）で落ちる。

| | 入力 | 出力 |
|---|---|---|
| 共通 | NuGet の packages フォルダ（nuspec・nupkg の中の LICENSE/NOTICE）、GitHub のそのコミットの直下（保存先にあればそこから）、`noticeforge.json` と、そこから指すファイル、`GITHUB_TOKEN`（あれば） | 画面の報告（テキスト。各件の ok/NG、取った場所、理由）、終了コード、`--report` の JSON（同じ中身を機械向けに）、保存先への GitHub の文 |
| check だけ | `packages.lock.json`（restore 済みであること） | 報告に、各パッケージを使っている project（`used by:`） |
| notices だけ | publish 先の `*.deps.json`（publish 済みであること） | `THIRD-PARTY-NOTICES.txt` |

notices の起点は、ほぼ check の起点の部分集合になる。例外はランタイムパック（自己完結で同梱する .NET ランタイム、Windows SDK の橋渡し）で、
SDK が restore とは別に取ってくるので lock に載らず、deps.json にだけ出る。Microsoft の純正品で、notices でも同じ判定を通るので漏れにはならない。

| 引数 | 意味 |
|---|---|
| `notices <publish-dir>` | `dotnet publish` の出力。中の `*.deps.json` を全部読む（サブフォルダのプラグインも） |
| `notices <deps.json>` | deps.json を 1 つだけ読む。単一ファイルの publish 向け（`obj/<構成>/<tfm>/<rid>/` に残る。`-o` が要る） |
| `check [<path>...]` | `packages.lock.json` か、その下を探すフォルダ（既定は `.`。`bin`・`obj` の下は見ない） |
| `-c, --config` | `noticeforge.json`（許可リスト・手での上書き・NuGet の外のもの） |
| `-o, --output` | notices の告知ファイルの出力先（既定は `<publish-dir>/THIRD-PARTY-NOTICES.txt`） |
| `--report` | 報告を JSON でも書く（前回との差分・PR へのコメント・上書きの下書きづくり向け。原本の文は入れない） |
| `--packages` | NuGet の packages フォルダ（既定は `$NUGET_PACKAGES` か `~/.nuget/packages`） |
| `--cache` | GitHub から取った文の保存先（既定は `%LOCALAPPDATA%/NoticeForge/cache`、Windows 以外は `~/.local/share/NoticeForge/cache`） |
| `--no-cache` | 保存せず、毎回 GitHub から取る |

GitHub から取った文は、リポジトリとコミットごとに保存し、2 回目からは取りに行かない。コミット（40 桁の SHA）で指しているので中身は変わらない。
取れなかったとき（回数の制限・通信の失敗）は保存しない。CI では保存先を `actions/cache` などで残すと速い。

環境変数 `GITHUB_TOKEN` があれば GitHub API に使う。無いと 1 時間 60 回まで（初回に、リポジトリとコミットの組の数だけ使う）、あれば 5,000 回（Actions の `GITHUB_TOKEN` は 1,000 回）。

終了コード: `0` 問題なし / `1` 問題あり（CI を落とす） / `2` 使い方・入力の誤り。

## 報告の読み方

結果は 2 つ。止めたもの（NG）は、直すか、人が判断して設定に書く。

| 画面 | JSON（`status` / `by`） | 意味 | 人がすること |
|---|---|---|---|
| `[ok] auto` | `ok` / `auto` | 機械だけで通った | なし |
| `[ok] config` | `ok` / `config` | 人の書いたもの（`license`・`file`・`reviewed`・`exclude`・extras）で補って通った | なし（理由は設定ファイルにある。JSON の `config` に何を補ったか） |
| `[NG]` | `ng` | 止めた（原本がない・種類が分からない・許可リストにない・人が見ていない・設定が要らなくなった） | 直すか、設定に書く |

画面は、全件の一覧（1 件 1 行。あとで引くため）→ NG の一覧（理由と、使っている project）→ 要約の順。
CI のログでも端末でも、最後の数行で「通ったか、何をすればよいか」が分かる。

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

## どこから何を取るか

| 取りたいもの | 取る場所 |
|---|---|
| 何が同梱されているか | publish 先の `*.deps.json` の `package` と `runtimepack`（ビルドでだけ使うものは入らない。自己完結の .NET ランタイムも入る） |
| ライセンスの種類 | nuspec の `<license type="expression">`。無ければ設定の `license` |
| ライセンスの原本 | ① nupkg の中の `LICENSE*` `COPYING*` ② nuspec の `<repository url commit>` が GitHub なら、**そのコミット**の直下の `LICENSE*` ③ ①② で取れなかったときだけ、設定の上書きの `file` |
| 告知（NOTICE・THIRD-PARTY-NOTICES） | nupkg の中。無ければ ② と同じ場所 |
| 著作権表示 | 原本の中。nuspec の `<copyright>` は参考として各節の頭に添える |
| ソースの入手先 | nuspec のリポジトリ（無ければ projectUrl） |

①〜③ で取れなければ「原本がない」として止める。ひな形の文で埋めない。
同じ文を持つ部品は、告知の中で一つにまとめる（文を 1 回だけ載せ、その上に使う部品を並べる）。

## noticeforge.json

```jsonc
{
  // 許すライセンスと、どこで許すか。書かなければ MIT / Apache-2.0 / BSD-2-Clause / BSD-3-Clause を both
  //   both = 配ってもよい（check と notices の両方で通す）
  //   repository = 使うだけならよい（check だけで通す。配布物に入ったら notices で止める）。配布を禁じる許諾など
  "allow": {
    "MIT": "both",
    "Apache-2.0": "both",
    "BSD-3-Clause": "both",
    "LicenseRef-Vendor-NoRedistribution": "repository"
  },
  // パッケージ名で結び付ける（版は書かない。版を上げても外れない）
  // 末尾の * はまとめ書き（"SkiaSharp*" は SkiaSharp で始まるもの全部。名前がぴったり合うものが先、次に長く合うもの）
  "packages": {
    // file は機械で原本が取れなかったときだけ使う。origin はその元（告知と報告の「どこから取ったか」に出る。人が書いた文なら省略）
    "Some.Package": { "license": "MIT", "file": "licenses/Some.Package.txt", "origin": "https://github.com/owner/repo/blob/<commit>/LICENSE", "reason": "nuspec にコミットがない" },
    "Not.Distributed":  { "exclude": true, "reason": "配布物に入らない" },
    // reviewed は、機械では決められず人が見て判断したことの結論（告知に GPL の名前がある・直下に許諾が複数ある）
    "SkiaSharp*": { "reviewed": "告知の GPL・LGPL はどれも選ばなくてよい（FreeType は FTL を選ぶ）。libmicrohttpd は配布物に入らない" }
  },
  // NuGet の外のもの（リポジトリに取り込んだアイコン・フォント・JS など）
  "extras": [
    // in（省略不可）: repository = リポジトリにだけある（check で見る） / distribution = 配布物にだけ入る（notices で見る） / both = 両方
    // sourceUrl は部品のソースの場所（告知の Source: に出る）
    { "name": "VS Code Codicons", "license": "CC-BY-4.0", "file": "licenses/codicons.txt", "origin": "https://github.com/microsoft/vscode-codicons/blob/<commit>/LICENSE", "sourceUrl": "https://github.com/microsoft/vscode-codicons", "in": "both" },
    { "name": "Bootstrap (デモ)", "license": "MIT", "file": "licenses/bootstrap.txt", "in": "repository" }
  ]
}
```

場所の言葉は allow と extras の `in` で共通（repository・distribution・both）。allow に `distribution` だけは書けない（配るものは必ずリポジトリの依存から来るので、「配ってよいが使ってはいけない」は成り立たない）。

名前の使い分け: `origin` は**原本の文**をどこから取ったか（報告の `origin` と同じ）、`sourceUrl` は**部品のソース**がどこにあるか（告知の `Source:`、報告の `sourceUrl` と同じ）。

上書きの `license` が nuspec の式と食い違えば止める（パッケージのライセンスが変わったことに気づくため）。
上書きの `license` と `file` は、機械で取れなかったときだけ使う。版によって取れるとき（同じパッケージの別の版など）は機械で取れたものを使う。
どの版にも要らなかった `license`・`file`・`reviewed` は、check で止める（まとめ書きは、合うパッケージのどれかで使われていれば要るとみなす）（設定を実際に合わせておくため。notices は配布物の一部しか見ないので止めない）。
パスは設定ファイルの置き場からの相対。

設定と、`file` で指すファイル（`licenses/` など）はリポジトリに入れる。nupkg と GitHub から取る文は、毎回取り直せるので入れない。

## ライセンス

[MIT](LICENSE)
