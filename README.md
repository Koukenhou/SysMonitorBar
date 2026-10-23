# SysMonitorBar

Windows 向けの常駐トレイ型・1 行リソースモニター（CPU/GPU/RAM/VRAM/ディスク）。WPF / .NET 10。

```text
CPU 18% | GPU  6% | RAM  9.2/15.9 GB | VRAM 1.8/4.0 GB | C: 62%
```

> 開発中（MVP の仕上げ段階）：CPU / GPU / RAM / VRAM / ディスク容量の実測値を表示します。

値はタスクマネージャーと同じ Windows のパフォーマンスカウンターから取得します（管理者権限やドライバーは不要）。VRAM は専用 GPU メモリだけを表示し、共有 GPU メモリは含みません。

## 使い方

- 起動すると、通知領域にアイコンが出て、画面上部にバーが表示されます。
- バーは左ボタンでドラッグして動かせます。位置は保存されます。
- トレイアイコンを右クリックすると、次の操作ができます。
  - バーの表示/非表示
  - 設定
  - 終了
- トレイアイコンをダブルクリックすると、バーの表示/非表示が切り替わります。
- Windows 起動時に自動で起動させたいときは、トレイメニューの「Windows 起動時に開始」をオンにします（初期状態はオフ）。登録するのは現在のユーザーのスタートアップだけです。
- 設定は `%LocalAppData%\SysMonitorBar\settings.json` に保存されます。
- 動作の記録（ログ）は `%LocalAppData%\SysMonitorBar\logs\` にあります。通信は一切行いません。

## 動作環境

Windows 10 / 11 x64。管理者権限は不要です。

## ビルド

.NET 10 SDK が必要です。

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet publish src/SysMonitorBar -p:PublishProfile=win-x64
```

発行すると、自己完結型の単一 exe（`publish/win-x64/SysMonitorBar.exe`）ができます。

## License

MIT（`LICENSE` を参照）。

## Author

Sole author: `koukenhou <koukenhou.gpt@gmail.com>`. Commits in this repository use only this identity and carry no co-author or tool attribution. After cloning, enable the guard hook:

```bash
git config core.hooksPath .githooks
git config user.name "koukenhou"
git config user.email "koukenhou.gpt@gmail.com"
```
