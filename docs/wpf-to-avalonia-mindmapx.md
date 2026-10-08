# WPF → Avalonia 移植のノウハウと注意点（MindMapX の実例から）

WPF 製の MindMap を、Windows / macOS / Linux で動く MindMapX（Avalonia 11.3.9）へ
作り直したときに分かったことをまとめる。一般論ではなく、**実際にやって効いたこと・踏んだこと**だけを書く。

- 対象: このリポジトリ（コミット `60c286c` → `3766100` → `1f93403` → リリース 1.7.0）
- 関連: [MdViewer](https://github.com/mitubuchi/MdViewer) の `MdAvalonia` / `SvgAvalonia` / `ImgAvalonia`（Avalonia 版の描画）
- 2026-10-08 時点で WPF 版と同等の機能まで追いついた（ツールのパッケージを除く）

---

## 1. 全体の進め方

### 1.1 先に「UI に依存しない層」を切り出す

いちばん効いたのはこれ。移植は UI の書き換えではなく、**層の切り分け**から始める。

```
src/
├─ MindMap.Core/          UI も OS も見えない層（Models / ViewModels / Undo / ファイル入出力）
├─ MindMap.App/           Avalonia の画面（全 OS 共通）
├─ MindMap.Desktop/       Windows / macOS / Linux 用の実行形（起動するだけの薄い層）
└─ MindMapX.Abstractions/ パッケージとの契約
```

- **Models・ViewModels・Undo は WPF 版からほぼそのまま持ってこられた。**
  ReactiveUI で書いた ViewModel は UI 枠組みに依存しないので、移植の大半はここで済む
- `MindMap.Core` の `RootNamespace` を WPF 版と同じ `MindMap` にしておくと、
  **ファイルをコピーするだけで using を直さずに済む**
- `MindMap.Core.csproj` には UI 部品を一切入れない（`ReactiveUI` だけ）。入れた瞬間に層が崩れる
- 実行形（`MindMap.Desktop`）は `Program.cs` だけ。Android / iOS を足すときはここを並べるだけになる

### 1.2 OS ごとに違うものはインターフェースで差し込む

Core から OS が見えないように、違いはすべて UI 側から注入する。

| 違うもの | やり方 |
|---|---|
| クリップボード | `INodeClipboard`（Core）を `AvaloniaNodeClipboard`（App）で実装し、ViewModel のコンストラクタに渡す |
| リンクを開く | `LinkLauncher` で `UseShellExecute`（Windows）/ `open`（macOS）/ `xdg-open`（Linux）に分岐 |
| 関連付けアプリのアイコン | `ILinkIconProvider`。Windows のときだけ `WindowsLinkIconProvider` を入れ、他は null で線画に落とす |
| 画像・映像の詳細 | `FileNodeContent.DescribeMedia` にデリゲートを差す。Windows 以外は差さない（行が増えないだけ） |

組み立ては `App.OnFrameworkInitializationCompleted` の 1 か所に集める。`OperatingSystem.IsWindows()` で
分岐するのもここだけにすると、OS の違いがどこにあるかが一目で分かる。

### 1.3 段階を分けて追いつく

一度に全部移さず、次の順で進めた。各段で動く状態を保つ。

1. **骨格**: Core の切り出し、画面、クリップボード、ファイル選択、ダイアログ（`60c286c`）
2. **ViewModel を WPF 版の最新に写す** + ビューア欄とパッケージ読み込み（`3766100`）。画面はまだ付けない
3. **ViewModel に入った機能へ画面を付ける**（サムネイル・折りたたみ・整列・設定・D&D）（`1f93403`）

ViewModel を先に揃えてから画面を付けると、「機能が無いのか画面が無いのか」で迷わない。

---

## 2. XAML の置き換え

### 2.1 Trigger / DataTrigger は無い

Avalonia には WPF の `Style.Triggers` / `DataTrigger` が無い。置き換えは 2 通り。

| WPF | Avalonia |
|---|---|
| `Trigger Property="IsMouseOver"` など**状態** | セレクタの擬似クラス（`:pointerover` `:pressed` `:checked` `:disabled` …） |
| `DataTrigger Binding="{Binding Kind}" Value="Folder"` など**値の出し分け** | コンバーターで値に落とす |

コンバーターは `static readonly` のフィールドにまとめ、`{x:Static conv:AppConverters.LinkGlyph}` で引く。
`FuncValueConverter<TIn, TOut>` / `FuncMultiValueConverter` を使えばクラスを作らずに 1 行で書ける。

```csharp
// WPF の DataTrigger（列挙の値で入力欄を出し分け）にあたるもの
public static readonly IValueConverter EnumIn = new FuncValueConverter<object?, string?, bool>(
    (value, names) => value is not null && names is not null && names.Split('|').Contains(value.ToString()));
```

### 2.2 RadioButton と列挙のバインドは「選ばれた側だけ書き戻す」

`IsChecked` を列挙プロパティにつなぐコンバーターで、`ConvertBack` が `false` のときも書くと
**外れた側が選んだ値を上書きする**。外れた側は `BindingOperations.DoNothing` を返す。

### 2.3 スタイルは `ControlTheme` で、色はスタイルにだけ書く

- WPF の `Style x:Key` 相当は `ControlTheme x:Key="..." TargetType="Button"`
- ⚠️ **選んだノードの枠の色が変わらなかった。** 原因は `Border` に `BorderBrush` を直接書いていたこと。
  Avalonia でも**ローカル値はスタイルより優先**される。状態で変わる値は要素に書かず、スタイル側で決める

### 2.4 アイコンは C# の Geometry 定数に

XAML のリソースではなく `Resources/Icons.cs` に `Geometry.Parse(...)` の定数で並べ、
`{x:Static res:Icons.New}` で参照した。ツールバー・リンクの記号・コンバーターが**同じ 1 か所**から引ける。
24×24 の線画（塗りなし）にしておくと、小さくても潰れにくく、色を 1 か所で変えられる。

### 2.5 ツールバーのボタンは `Focusable=False`

⚠️ WPF の `ToolBar` 内ボタンはフォーカスを取らないが、Avalonia の普通の `Button` は取る。
そのままだと **Tab がマップ操作ではなくボタン間の移動に使われ、Enter でボタンが押される**。
ツールバー用の `ControlTheme` で `Focusable=False` を設定する。

### 2.6 既定のコンテキストメニューは英語で項目も少ない

Fluent テーマの `TextBox` の右クリックメニューは英語で、「元に戻す」「すべて選択」が無い。
日本語のメニューを自前で持たせる。メニューは開くたびに右クリックされた欄に付くので、**1 つを複数の欄で使い回せる**。

---

## 3. バインディング

### 3.1 コンパイル済みバインディングと型変換

`MindMap.App.csproj` は `AvaloniaUseCompiledBindingsByDefault=false`（WPF 版の XAML を移しやすくするため）。

⚠️ **起動直後に必ず落ちていた。** タブの × ボタンのバインドに型変換を書いていて、
コンパイルされないバインドでは実行時に型を解決できなかった。リフレクションバインドで
型変換や `x:Static` 以外の型参照を書くときは、**実際に起動して確かめる**（ビルドは通る）。

### 3.2 DataTemplate の中の名前はフィールドにならない

`x:Name` を付けても、`DataTemplate` 内の要素はウィンドウのフィールドにならない。
visual tree を下って名前で探すヘルパー（`FindNamedDescendant` / `FindNamedAncestor`）を用意した。

### 3.3 ReactiveUI の `Interaction` でダイアログを出す

ViewModel から画面を直接触らず、`Interaction<TIn, TOut>` を ViewModel に持たせ、
ウィンドウ側の `RegisterInteractionHandlers` でつなぐ。WPF 版の作りがそのまま使えた。

- `Program.cs` で `.UseReactiveUI()` を忘れない（通知を UI スレッドに載せる）
- ⚠️ `ReactiveUI` の版: Core は **20.1.63**（`Avalonia.ReactiveUI 11.3.9` が要求する系列）。
  WPF 版 MindMap は `ReactiveUI.WPF` 23.2.28 で、系列が違う。片方に合わせて上げ下げしない
  （24.x は System.Reactive への依存をやめたので、上げる前にそこを確かめる）

---

## 4. WPF には有って Avalonia には無いもの

| WPF | Avalonia での代わり |
|---|---|
| `MessageBox` | **無い。** `MessageDialog`（自前の小さな Window）を作り `ShowAsync(owner, message, buttons...)` で返す。先頭を `IsDefault`、末尾を `IsCancel` に |
| `OpenFileDialog` / `SaveFileDialog` | `StorageProvider.OpenFilePickerAsync` / `SaveFilePickerAsync`。ローカルパスは `TryGetLocalPath()` |
| `Clipboard`（同期・静的） | `TopLevel.Clipboard`（**非同期・ウィンドウにぶら下がる**）。都度取れるよう `Func<IClipboard?>` で渡す |
| `FlowDocument` / `Hyperlink` | 無い。MdViewer はブロックごとに部品を作って縦に並べ、リンクは文字範囲＋`TextLayout` で当たり判定 |
| `GridSplitter` | 有るが、DockPanel のまま済ませたかったので、帯のドラッグで幅を変える処理を自前で書いた |
| `StartupUri` | 無い。`App.OnFrameworkInitializationCompleted` で `desktop.MainWindow` に入れる |

### 4.1 クリップボードは非同期になる

読み書きに待ちが入る OS があるので、**切り取り・コピー・貼り付けのコマンドを非同期にした**（`INodeClipboard` は `Task` を返す）。

- 自前形式（JSON）とテキスト（箇条書き）を 1 つの `DataTransferItem` に両方載せる。
  専用形式を知らない相手にもテキストで貼れる
- 他のアプリがクリップボードを握っていると失敗することがある。**例外は飲み込んで諦める**（操作全体は落とさない）

### 4.2 ウィンドウを閉じる前の確認は「一度キャンセルしてから閉じ直す」

`Closing` で非同期のダイアログは待てない。いったん `e.Cancel = true` で取り消し、
答えが出てから `Close()` し直す。二周目を見分けるフラグを持つ。

---

## 5. フォーカスと入力（いちばん時間を使ったところ）

### 5.1 `Focus()` はレイアウト前には効かない

編集欄が見えた直後に `Focus()` しても効かない。`Dispatcher.UIThread.Post(..., DispatcherPriority.Input)` で一拍置く。
欄が「隠れているだけで最初から存在する」場合は、`GetObservable(IsVisibleProperty)` で表示の変化を見張る。

### 5.2 LostFocus の時点では次のフォーカス先が決まっていない

タイトル欄 → 内容欄の移動でも LostFocus が出る。**個々の欄ではなくパネル全体**で受け、
`Post` で一拍置いてから「フォーカスがまだパネル内か」を確かめて、出ていたときだけ編集を確定する。

### 5.3 ⚠️ 右クリックメニューを開くと編集が確定して欄が消える

WPF 版でも起きた問題が、Avalonia でも同じ形で出た。メニューを開くとフォーカスがメニューへ移り、
上の LostFocus 判定で編集が確定 → 欄が消えて、メニューの「貼り付け」が届かない。

- メニューの描かれ方は環境で違う（デスクトップは別ウィンドウ、そうでなければ重ね描き）ので
  **visual tree では辿れない**
- **論理ツリー**なら、どちらでも上に `Popup` がいて `PlacementTarget` が開いた元の欄を指す。
  そこが編集パネル内の欄なら確定しない
- メニューの `Closed` で、編集が続いていれば欄にフォーカスを戻す

```csharp
focused is ILogical logical
    && logical.GetLogicalAncestors().OfType<Popup>().FirstOrDefault() is { PlacementTarget: TextBox box }
    && panel.IsVisualAncestorOf(box)
```

### 5.4 キー割り当ては明示する

⚠️ 最初の移植では Tab / Insert / Enter / Delete / F2 の割り当てが抜けていて、Tab はボタン間の移動に使われていた。
WPF の `InputBindings` を移し忘れていないか、**キー一覧を作って 1 つずつ押して確かめる**。

### 5.5 レイアウトが確定してから計算する

- ノードの実寸は描いてみて初めて決まる。本文を隠した直後は古い値なので、`UpdateLayout()` を回してから ViewModel に返す
- Ctrl+ホイールのズームでカーソル下を固定するスクロール補正は、新しい拡大率のレイアウト後
  （`DispatcherPriority.Loaded`）に行う
- タブ切り替え後のスクロール位置の復元も同じ。復元中の動きを記録し返さないようにフラグを持つ

---

## 6. ドラッグ&ドロップ

- ⚠️ Avalonia 11.3 で `DragEventArgs.Data`（`IDataObject`）は旧 API 扱いだが、
  **ドロップの中身を形式名で引けるのはまだこちらだけ**。`#pragma warning disable CS0618` で囲んで使っている
- ブラウザーが渡す形式は OS で違う。Windows は `UniformResourceLocatorW` など、他の OS は `text/uri-list` か文字列。
  **扱える順に並べて順に試せば、どの OS でも拾える**
- `text/uri-list` は `#` 始まりの行が注釈。`text/x-moz-url` は「URL\n題名」の繰り返し
- 中身は文字列 / バイト列 / ストリームのどれで届くか分からない。全部吸収し、取り出しで失敗したら次の形式へ
- 落ちた先は `e.Source` から visual tree を上って判定（ノードの枠 `Classes="node"` か、キャンバス `CanvasRoot` か）

---

## 7. パッケージ（プラグイン）の読み込み — Avalonia 固有の大きな落とし穴

### 7.1 ⚠️ ホストが持つアセンブリは必ずホストのものに寄せる

WPF はフレームワーク側にあるので、契約（`MindMap.Abstractions`）だけ共有すれば済んだ。
**Avalonia は NuGet で配るので、パッケージのフォルダーにも `Avalonia.*.dll` が入る。**
それを別に読み込むと「同じ名前の別の型」になり、**パッケージの `Control` をホストの画面に載せられない**
（SkiaSharp の Bitmap も同じ）。

`PackageLoadContext.Load` で、ホストが起動時から持っているアセンブリ
（`AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")`）に名前があれば `null` を返して既定の読み込み先に回す。

```csharp
if (assemblyName.Name is { } name && HostAssemblies.Value.Contains(name))
    return null;   // ホストのものを使う
```

- `isCollectible: false`。画面が参照を握るので、collectible にしても実際には外れない
- 結果として**描画系の版はホストが決める**。パッケージ側が新しい API を使うと実行時に「メソッドが無い」で落ちる

### 7.2 SkiaSharp の版はホストで上げる

- `Avalonia.Svg.Skia 11.3.0`（MdViewer の SVG）が **SkiaSharp 3.116.1** を要求する。
  Avalonia 11.3 の既定は 2.88.9 なので、`MindMap.Desktop.csproj` で明示的に上げた（Avalonia 11.3 は 3.x で動く。実測）
- **Linux のネイティブ部品（`SkiaSharp.NativeAssets.Linux`）は本体から辿られない**ので、同じ版を明示する
- MdViewer 側で SkiaSharp を上げたら、MindMapX の `MindMap.Desktop.csproj` も揃える

### 7.3 契約のアセンブリ名を WPF 版と分ける

`MindMapX.Abstractions` と別名にした。同じ名前だと MindMapPackages の 1 ソリューションに
WPF 用・Avalonia 用を並べたとき**出力 DLL がぶつかる**。インターフェースの並びは揃えておく。
画面の型（ビューアの画面・サムネイル）は Core では `object` で受け渡し、Core に Avalonia を持ち込まない。

### 7.4 壊れたパッケージは黙って無視しない

読めたぶんは使い、エラーはウィンドウが開いた後（`window.Opened`）に 1 度だけダイアログで知らせる。
「入れたのに効かない」がいちばん分かりにくい。

---

## 8. 描画ライブラリ（MdViewer の Avalonia 版）で踏んだもの

MdViewer リポジトリで記録しているもののうち、移植に関わる要点だけ。

- **クラス名・プロパティ名を WPF 版と揃え、名前空間だけ変える**。片方を直したらもう片方も確かめる
- 描画ライブラリは `netstandard2.0` にすると 1 つの DLL で全 OS が動く（MdViewer は C# 7.3 固定）
- リンクの文字位置の数え方が 2 通りある（`LineBreak` は Windows で 2 文字、`InlineUIContainer` は選択では 0・レイアウトでは 1）。
  確かめるときは `SelectedText` ではなく `TextLayout.TextLines` から組み立てた文字列と比べる
- `TextLayout.HitTestPoint` は折り返したリンクの最終行で範囲外を返した。リンクの矩形に点が入るかで判定する
- SVG の大きさの基準が違う（SharpVectors は描かれた範囲、Svg.Skia は width / height）
- EXIF の向きを 8 通り実測したら、**WPF 版（ImgWpf）の 5 と 7 が入れ替わっていた**。移植は旧版の検算の機会にもなる
- ライセンス: Svg.Skia の依存のうち `Svg.Custom` だけ MS-PL

---

## 9. 配布（OS ごと）

GitHub Actions の `release` ワークフローで作る（Mac を持っていなくても macOS 版ができる）。どれも自己完結（.NET 不要）。

| OS | 形 | 注意 |
|---|---|---|
| Windows | ZIP | `ApplicationIcon` は Windows 以外では無視されるだけ |
| macOS | `MindMap.app` を `ditto -c -k --keepParent` で ZIP | `Info.plist` を自前で書く。アイコンは `sips` で .ico → .icns。**Apple Silicon は署名の無い実行ファイルを起動できない**ので `codesign --force --deep --sign -`（ad-hoc）。プラグインは .app に入れてから署名する（後から足すと署名が壊れる） |
| Linux | tar.gz | **ZIP だと実行権限が落ちる**。ARM64（Raspberry Pi 4/5 の 64 ビット OS）も同じ形で出せる |

macOS の利用者向けに、「開発元を確認できない」→ 右クリック「開く」、「壊れている」→
`xattr -dr com.apple.quarantine MindMap.app` を README に書いた。
Linux は日本語フォント（`fonts-noto-cjk`）と ICU（`libicu*`）が無いと困る。

インストーラーと `.mindmap` の関連付けは OS ごとのパッケージングが要るので未着手。

---

## 10. 確かめ方

- **本物のウィンドウを画面なしで組み立て、クリックとキー入力を流して確かめた**（`1f93403`）。
  フォーカスやキー割り当ての不具合は、こうしないと見つからなかった
- Windows 以外は、ビルドと配布物の中身までは確認済みで、実機の確認はまだ。
  試す人が同じ手順で見られるよう、README に OS ごとの起動手順と共通の確認項目（番号付き）を書いた

---

## 11. チェックリスト（次に WPF アプリを Avalonia へ移すとき）

- [ ] Models / ViewModels / Undo を UI 非依存のプロジェクトへ。名前空間は変えない
- [ ] OS 依存（クリップボード・リンク起動・アイコン・メディア情報）をインターフェース化して App で注入
- [ ] `Trigger` → 擬似クラス、`DataTrigger` → コンバーター
- [ ] 状態で変わる値（枠の色など）を要素に直書きしていないか
- [ ] ツールバーのボタンは `Focusable=False`
- [ ] `MessageBox` / ファイルダイアログ / クリップボードの代替（非同期化）
- [ ] キー割り当てを一覧にして 1 つずつ押す（Tab / Enter / Delete / F2 / Insert …）
- [ ] 編集欄の右クリックメニューで編集が確定しないか
- [ ] リフレクションバインドの型変換は起動して確かめる
- [ ] プラグインがあるなら、Avalonia / SkiaSharp をホストに寄せる `AssemblyLoadContext`
- [ ] SkiaSharp の版と、Linux のネイティブ部品の版をホストで明示
- [ ] macOS は ad-hoc 署名、Linux は tar.gz
- [ ] `.gitignore` のフォルダー指定は先頭に `/`（`packages/` と書くと、大文字小文字を区別しない Windows では `Services/Packages/` まで追跡から外れた）
