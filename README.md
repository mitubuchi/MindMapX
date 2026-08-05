# MindMapX

マインドマップ作成アプリのマルチプラットフォーム版。C# / [Avalonia UI](https://avaloniaui.net/) で作られており、
Windows / macOS / Linux で同じコードが動きます（Android / iOS も同じ構成のまま追加できます）。

WPF 製の Windows 版 [MindMap](https://github.com/mitubuchi/MindMap) から派生したプロジェクトです。
Git の履歴もそのまま引き継いでいます。

## いまの状態

Desktop（Windows / macOS / Linux）向けの移植が動く状態です。

| 機能 | 状態 |
|---|---|
| ノードの追加（子 / 兄弟）・その場編集・部分木削除・ドラッグ移動 | ✅ |
| 複数選択（Ctrl / Shift + クリック、余白のドラッグ、Ctrl+A） | ✅ |
| 切り取り / コピー / 貼り付け（タブ・ウィンドウをまたいで可） | ✅ |
| タイトルと内容の分離表示・表示の折りたたみ | ✅ |
| ノードへのリンクと、種類に応じたアイコン | ✅ |
| 子ノードを別のファイルに切り出し（相互リンク付き） | ✅ |
| 複数ドキュメントのタブ・すべて保存 | ✅ |
| Undo / Redo、ズーム、パン | ✅ |
| `.mindmap`（JSON）の保存・読み込み | ✅ |
| ブラウザー / ファイラーからのドラッグ&ドロップ | ⏳ OS ごとの実装が必要 |
| インストーラー・`.mindmap` の関連付け | ⏳ OS ごとのパッケージングが必要 |
| Android / iOS 向けの画面 | ⏳ タッチ操作の設計から |

リンクのアイコンは、ファイルへのリンクなら関連付けられたアプリのもの（Windows のみ）、
それ以外は種類ごとの線画（マインドマップ・フォルダー・メール・Web・不明）になります。
Windows 以外ではファイルも線画のアイコンで表示されます。

## プロジェクト構成

画面の枠組みに依存しない部分を `MindMap.Core` に分けてあります。OS を増やすときは
実行形（`MindMap.Desktop` にあたるもの）を足すだけで、この 2 つはそのまま使えます。

```
src/
├─ MindMap.Core/      UI に依存しない層（どのプラットフォームでも共通）
│   ├─ Models/        保存されるデータ構造
│   ├─ Services/      ファイル入出力・リンクの判定・クリップボードの形式
│   ├─ ViewModels/    画面ロジック（アプリ全体 / ドキュメント / ノード）
│   └─ Undo/          Undo/Redo の履歴管理
├─ MindMap.App/       Avalonia の画面（すべての OS で共通）
│   ├─ Views/         ウィンドウとダイアログ
│   ├─ Resources/     アイコン（線画）とコントロールのテーマ
│   ├─ Converters/    表示のための変換
│   └─ Services/      クリップボード・リンクの起動・アイコンの取り出し
└─ MindMap.Desktop/   Windows / macOS / Linux 用の実行形（起動するだけの薄い層）
```

OS ごとに違う部分（クリップボード、リンクの開き方、アイコンの取り出し方）は
インターフェース越しに差し込む形にしてあり、`MindMap.Core` からは OS が見えません。

## 動作環境

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Windows / macOS / Linux

## ビルドと実行

```sh
dotnet run --project src/MindMap.Desktop
```

配布用（.NET のインストール不要な自己完結ビルド）は、対象を指定して publish します。

```sh
# Windows
dotnet publish src/MindMap.Desktop -c Release -r win-x64 --self-contained true -o publish/win-x64

# macOS（Apple Silicon / Intel）
dotnet publish src/MindMap.Desktop -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
dotnet publish src/MindMap.Desktop -c Release -r osx-x64 --self-contained true -o publish/osx-x64

# Linux
dotnet publish src/MindMap.Desktop -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

macOS 向けの成果物は Windows 上でも作れますが、動作確認には実機が要ります。
GitHub Actions で 3 つの OS のビルドを回しているので、Mac を持っていなくても
成果物を得られます（Actions の Artifacts から取得）。

## キーボード操作

| キー | 動作 |
|---|---|
| `Ctrl+N` | 新しいタブ |
| `Ctrl+O` | ファイルを開く |
| `Ctrl+S` / `Ctrl+Shift+S` | 保存 / 名前を付けて保存 |
| `Ctrl+Alt+S` | 開いているタブをすべて保存 |
| `Ctrl+W` | タブを閉じる |
| `Ctrl+Z` / `Ctrl+Y` | 元に戻す / やり直し |
| `Ctrl+X` / `Ctrl+C` / `Ctrl+V` | 切り取り / コピー / 貼り付け |
| `Ctrl+A` | すべてのノードを選択 |
| `Ctrl` + `+` / `-` / `0` | 拡大 / 縮小 / 等倍 |

ノードの編集中は、タイトル欄で `Enter` を押すと内容欄へ移動し、`Ctrl+Enter` で確定、
`Esc` で編集前に戻します。

複数選択は、`Ctrl+クリック` で 1 つずつ足し引き、`Shift+クリック` で追加、
何もない余白をドラッグすると範囲選択できます。複数選んだままドラッグすると、まとめて移動します。

## ファイル形式

`.mindmap` ファイルは JSON です。ノードは親子関係を `ParentId` で表すフラットな配列として
保持します。形式は Windows 版（MindMap）と同じなので、どちらのアプリでも読み書きできます。

## これから

- ドラッグ&ドロップ（OS ごとのデータ形式に対応）
- 各 OS のパッケージング（Windows: インストーラー、macOS: `.app` / `.dmg`、Linux: AppImage）
- Android / iOS の実行形と、タッチ操作に合わせた画面
