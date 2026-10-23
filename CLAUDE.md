# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git と作者（要約）

個人プロジェクト共通の規則は、上位フォルダ `C:\KOU\CLAUDE.md` から自動で継承される。このリポジトリを別の場所で開いた場合に備えて、要点だけここにも書く。

- 唯一の作者は `koukenhou <koukenhou.gpt@gmail.com>`。共同作者・ツール署名（`Co-Authored-By`、`Generated with Claude Code` など）を付けない。
- `--author` で上書きしない。`git config user.name` / `user.email` を変更しない。`--no-verify` を使わない。
- `.githooks/commit-msg` が他の作者と署名の混入を拒否する。clone し直したら `git config core.hooksPath .githooks` を実行する。
- コミット・プッシュは依頼されたときだけ。プッシュ前に `gh auth status` で `Koukenhou` が有効か確認する。

## 概要

Windows 10/11 x64 向けの常駐トレイアプリ。メインウィンドウは無く、ドラッグできる 1 行のバーだけを表示する（例：`CPU 18% | GPU  6% | RAM  9.2/15.9 GB | VRAM 1.8/4.0 GB | C: 62%`）。C# / .NET 10 / WPF。トレイは WinForms の `NotifyIcon`。画面の言語は日本語。他の人にも配布する（MIT、win-x64 の自己完結型の単一 exe）。

### 仕様の文書と、どれを優先するか

1. `docs/DesignChanges.md` — v1.0 からの変更点と、KOU が決めた事項。**最優先**。
2. `SysMonitorBar_Design_v1.0.md` — 詳細設計の基準（簡体字中国語）。既定値・テスト項目 T01〜T18・MVP の完了定義（§15）はここにある。
3. `SysMonitorBar_Codex_Implementation_Prompt.md` — 段階ごとの進め方の指示。

v1.0 から変わった大きな点：GPU / VRAM / CPU の値は **LibreHardwareMonitor ではなく、Windows のパフォーマンスカウンターと DXGI** から取る（値はタスクマネージャーと同じ基準になる）。指標 ID の扱い、位置の保存方法、`showSeparator` も変わった。詳しくは `docs/DesignChanges.md`。

### 進み具合

- 段階 A（UI の最小構成）：完了。KOU が 2026-10-22 に確認した。
- 段階 B（実データの取得）：実装済みだが、KOU による確認はまだ。
  - CPU（PDH の `% Processor Utility`）、GPU と VRAM（PDH と DXGI）、RAM（`GlobalMemoryStatusEx`）、ディスク（`DriveInfo`）を取得する。
  - 設定画面で、使用する GPU を選べる。
  - `--simulate` を付けて起動すると、模擬データで動く。
- 段階 C（品質の仕上げ）：実装済みだが、KOU による確認はまだ。
  - ログを書き出す。
  - Windows 起動時の自動起動に対応した。
  - モニターごとの DPI に対応した（PerMonitorV2）。
  - スリープからの復帰に対応した。
  - 起動時に `settings.json` を書き出す。
  - 長時間運転を確認するスクリプト（`tools/soak.ps1`）を用意した。
- 残っている作業：KOU による手動確認、8 時間の連続運転、DPI の異なるモニターでの確認。2 期の機能（温度、ディスク I/O、ネットワーク）は MVP の対象外。

## コマンド

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet test --filter "FullyQualifiedName~MetricFormatterTests"
```

```bash
dotnet run --project src/SysMonitorBar
```

```bash
dotnet run --project src/SysMonitorBar -- --simulate
```

```bash
dotnet publish src/SysMonitorBar -p:PublishProfile=win-x64
```

- 発行すると `publish/win-x64/SysMonitorBar.exe`（約 165 MB）ができる。
- 単一 exe の圧縮はわざと無効にしている。有効にするとメモリ使用量が約 2 倍になるため（`docs/DesignChanges.md`）。
- 実行中のアプリは `bin/Debug` の exe をロックする。再ビルドの前に、トレイの「終了」で止める。
- 2 つ目のプロセスは、自分では何もせずに終了する（単一インスタンスのため）。
- トレイアイコンは `python tools/make_icon.py` で作り直せる（Pillow が必要）。
- ログは `%LocalAppData%\SysMonitorBar\logs\SysMonitorBar.log`。動作がおかしいときは、まずここを見る。
- 長時間運転の確認：アプリを動かしたまま `powershell -ExecutionPolicy Bypass -File tools\soak.ps1 -Minutes 480` を実行すると、CSV に記録される。

## 構成

- `src/SysMonitorBar.Core`（`net10.0`、WPF に依存しない）
  - ドメイン：`MetricKey`、`MetricReading`、`MetricsSnapshot`
  - `MetricFormatter`
  - 設定：`AppSettings`、`SettingsService`
  - 配置の計算：`BarPlacement`
  - サンプリング：`MonitorCoordinator`、`IMetricsSource`
  - 画面の文言：`Resources/Strings.resx`
  - ログ：`Logging/Log.cs`（`Log` と `RollingFileLog`）
  - 自動起動の判定：`Startup/StartupCommand.cs`
  - テストできるロジックは、すべてこのプロジェクトに置く。
- `src/SysMonitorBar`（`net10.0-windows`、WPF＋WinForms）
  - `AppController`：起動・終了、トレイ、バー、設定画面、サンプリングをまとめて持つ。
  - `Views/`、`ViewModels/`
  - `Infrastructure/`：トレイ、単一インスタンス、画面の情報、自動起動のレジストリ（`StartupRegistration`）、Win32 の呼び出し。
  - `Infrastructure/Metrics/`：Windows から値を取る各取得元（`IMetricProvider`）と、PDH / DXGI の P/Invoke。
    - 計算ロジック（GPU の集計 `GpuCounterMath`、GPU の選択 `GpuSelector`、失敗時の待ち `FailureBackoff`）は Core に置き、テストする。
    - Windows の API を呼ぶ部分は、このフォルダーに置く。
- `tests/SysMonitorBar.Tests`（xUnit、Core だけを参照する）

### データの流れとスレッド

- `MonitorCoordinator` は、バックグラウンドのループ 1 本だけで `IMetricsSource.Sample` を呼ぶ。同時に呼ばれることはない。
- 次のサンプリングまでの待機は、`SemaphoreSlim.WaitAsync(interval)` で行う。`Configure` / `Resume` が呼ばれると待機を抜け、すぐに次のサンプリングを行う。
- サンプリング中の例外は `onError` に渡し、ループは止めない。
- できたスナップショットは `AppController.OnSnapshotFromLoop` が UI スレッドに渡す。UI 側への受け渡しが溜まらないよう、最新の 1 件だけを残す。
- UI 側では `MetricFormatter` で文字列にして、`MonitorBarViewModel.RenderedText` に入れる。
- Provider は UI に触らない。View はハードウェアを読まない。

### 守るべき決まり

- 値が取れないときは `--` を表示し、0 にしない（`MetricQuality`）。値が取れない理由（`Reason`）と取得元（`Source`）はツールチップに出す。
- 取得元は `CompositeMetricsSource` にまとめる。要求された指標を扱う取得元だけを呼ぶ。例外を出した取得元は 5〜60 秒のあいだ呼ばない（その間、その指標は `--`）。
- パフォーマンスカウンターは `PdhQuery`（`PdhAddEnglishCounter`）で読む。.NET の `PerformanceCounter` は使わない。表示言語に合わせた名前が必要で、日本語版 Windows では GPU のカテゴリーが見えないため。
- 変化量を表すカウンター（CPU、GPU エンジン）は、最初の 1 回だけ「収集 → 200 ms 待つ → 収集」が必要。この処理は取得元の中で、サンプリングのスレッドから行う。
- VRAM は「専用」メモリだけを数え、共有 GPU メモリは使わない。使用量は `GPU Adapter Memory\Dedicated Usage`、総量は DXGI の `DedicatedVideoMemory` から取る。
- 背景の不透明度は `Border.Background` のブラシにだけ効かせる。ウィンドウの `Opacity` は使わない。
- 数値は U+2007（フィギュアスペース）で桁をそろえ、バーの幅を安定させる。
- `ShutdownMode=OnExplicitShutdown` にする。
- バーを隠すときは `Hide()` を使う。バーの `OnClosing` は、`AllowClose` が true のとき以外はキャンセルする。
- 終了は `AppController.ExitAsync` で行う。順序は、サンプリングを止める → ソースを破棄する → トレイを破棄する → `Shutdown`。
- 起動時はトレイを最初に作る。
- 設定画面での変更は、すぐバーに反映する（プレビュー。`ShowOnBar`）。ディスクに書くのは「適用」か「OK」を押したときだけ（`ApplySettings`）。
- 設定画面を閉じたら、適用済みの設定に戻す。
- 設定画面は位置と表示状態を編集しない。この 2 つは `ApplySettings` で、今の値を引き継ぐ。
- 位置を保存するのは、ドラッグが終わったとき（`DragMove()` から戻ったとき）だけ。保存するのはモニター名と、左上の**物理ピクセル**座標（`leftPx` / `topPx`）。
- `SettingsService.Save` は、一時ファイルに書いて内容を確認してから置き換える。読み込めなかったファイルは `settings.corrupt-*.json` に退避し、既定値で起動する。
- 単一インスタンス：Mutex は `Local\SysMonitorBar.SingleInstance`、名前付きパイプは `SysMonitorBar.Activate.<ユーザーの SID>`。2 回目に起動されたら、起動中のインスタンスがバーを表示する。
- バーには `WS_EX_TOOLWINDOW` を付け、Alt+Tab に出ないようにする。
- DPI はモニターごと（PerMonitorV2、`app.manifest`）。バーの配置は WPF の `Left` / `Top` を使わない。`GetWindowRect` / `SetWindowPos` で、物理ピクセルのまま扱う（`NativeMethods`）。DPI が違うモニターの間では、DIP の大きさがモニターごとに変わるため。
  - `ScreenService` は作業領域をピクセルで返す。
  - DIP が必要なのはバーの最大幅だけ。ピクセル ÷ `VisualTreeHelper.GetDpi(bar)` で求める。
  - `DpiChanged` のときに、最大幅と位置を計算し直す。
- 自動起動はレジストリ（`HKCU\...\Run` と `StartupApproved\Run`）を正とする。`settings.json` の `startup.startWithWindows` は、その写しにすぎない。起動時・トレイメニューを開いたとき・設定画面を開いたときに、レジストリの状態を反映する（`SyncStartWithWindows`）。HKLM には書かない。
- スリープに入ったらサンプリングを止める（`_suspended`）。復帰したら `MonitorCoordinator.RequestReset()` を呼び、サンプリングのスレッドで取得元を作り直す。
- 一時停止と再開は `UpdateSampling()` にまとめる。スリープ中、または「隠している間は止める」がオンでバーが隠れているときだけ止める。
- ログは `Log.Info` / `Log.Warning` / `Log.Error` で書く。毎秒の測定値は書かない（ファイルが膨らむため）。取得元の失敗と回復は、`CompositeMetricsSource` のコールバックから記録する。
- 起動時には毎回 `settings.json` を書き出す（KOU の決定。ファイルが常にある状態にするため）。

## 実装上の注意

- WPF と WinForms で同じ名前の型（`Application` など）がある。そのため、暗黙の using から `System.Windows.Forms` と `System.Drawing` を外している。WinForms の型は、完全修飾名か `Forms` エイリアスで書く。
- XAML のクラスは `x:ClassModifier="internal"` にする（ViewModel が internal のため）。
- 文言を足すときは `Strings.resx` に書く。`dotnet build` が強く型付けされたクラス `Strings` を生成する。XAML からは `{x:Static res:Strings.Xxx}` で参照する。
- `MetricFormatterTests` は日本語の文言を直接比較している。文言を変えたらテストも直す。
- 設定ファイル：`%LocalAppData%\SysMonitorBar\settings.json`。
- 識別子とコメントは英語。画面の文言は日本語。
