using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindMap.Models;

/// <summary>
/// ファイルに保存されるマインドマップ全体。ノードは親子関係を <see cref="MindMapNodeDto.ParentId"/> で表す
/// フラットな配列として持つ（入れ子にしないことで、後から親を付け替えても差分が小さく済む）。
/// </summary>
public sealed class MindMapDocument
{
    /// <summary>
    /// ファイル形式のバージョン。
    /// 1 = タイトルのみ（Text 欄）／2 = タイトルと内容に分離（Title / Body 欄）／
    /// 3 = リンク（Link 欄）を追加／4 = 小さく表示するか（Collapsed 欄）を追加／
    /// 5 = 制作日・更新日（CreatedAt / UpdatedAt 欄）を追加／
    /// 7 = 知らない欄をそのまま持ち越す（<see cref="Extra"/>）／
    /// 8 = パッケージのツールが作ったノードに識別子を持たせる
    /// （<see cref="MindMapNodeDto.Extra"/> の中の Extensions 欄）／
    /// 9 = 置き方を親からの相対で持つ（<see cref="MindMapNodeDto.Transform"/>）。
    /// X / Y も今までどおり書き続けるので、9 を知らない版で開いても位置は保たれる。
    /// 子ノードを畳む欄（<see cref="MindMapNodeDto.ChildrenCollapsed"/>）も同じ版で足した。
    ///
    /// 6 は、この形式を共有する別のプログラムが独自の欄のために使っているので飛ばしてある。
    /// 同じ番号が 2 つの意味を持つと、ファイルを見ただけでどちらか分からなくなるため。
    ///
    /// なお、読み込みはこの番号で分岐していない。欠けている欄は既定値、知らない欄は
    /// <see cref="Extra"/> に入るので、どの版のファイルもそのまま読める。
    /// 番号は「何を足したか」の記録として持つ。
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 9;

    public List<MindMapNodeDto> Nodes { get; set; } = new();

    /// <summary>
    /// このアプリが知らない、ファイル全体に付いていた欄。読んだままの形で持っておき、
    /// 保存時にそのまま書き戻す（<see cref="MindMapNodeDto.Extra"/> と同じ考え方）。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MindMapNodeDto
{
    public Guid Id { get; set; }

    /// <summary>ルートノードの場合は null。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>1 行の見出し。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>改行を含められる本文。空でもよい。</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>URL またはファイルパス。空ならリンクなし。</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>
    /// 本文を隠して小さく表示するなら true。
    ///
    /// 1.6 から、新しく作るノードはこれが true で始まる（本文は開いたときだけ見せる）。
    /// 既存のファイルにはこの欄が必ず書かれているので、開いても見た目は変わらない。
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// 子ノードを畳んで隠すなら true。<see cref="Collapsed"/>（本文を隠す）とは別の話なので欄を分けてある。
    /// Version 9 で追加。無ければ false（子は見えている）。
    /// </summary>
    public bool ChildrenCollapsed { get; set; }

    /// <summary>
    /// 制作日と更新日。UI には出さず、後のデータ処理のために持つ。
    /// Version 5 より前の古いファイルには無いので null になり、読み込み時に補う。
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// バージョン 1 でタイトルが入っていた欄。古いファイルを読むためだけに残してあり、
    /// 保存時は常に null なので書き出されない。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>形式の違いを吸収してタイトルを取り出す。</summary>
    public string ResolveTitle() => string.IsNullOrEmpty(Title) ? Text ?? string.Empty : Title;

    /// <summary>
    /// 合成したあとのキャンバス上の絶対位置。<see cref="Transform"/> があってもこちらを書き続ける。
    ///
    /// この形式を共有する別のプログラム（形式 6）や、Version 9 より前の版がこの欄しか見ないため。
    /// 読み込み時に <see cref="Transform"/> と食い違っていたら、こちらを正として相対位置を計算し直す
    /// （＝知らない版で動かして保存された、と解釈する）。
    /// </summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// 親からの相対の置き方。Version 9 より前のファイルには無いので null になり、
    /// その場合は <see cref="X"/> / <see cref="Y"/> から組み立てる。
    /// </summary>
    public NodeTransform? Transform { get; set; }

    /// <summary>
    /// このアプリが知らない欄。読んだままの形で持っておき、保存時にそのまま書き戻す。
    ///
    /// 新しい版や、あとから足したパッケージが書いた欄を、それを知らない版で開いて
    /// 保存しただけで失わないため。中身は解釈しない（知らないものは知らないまま運ぶ）。
    /// 新しく欄を足す側は、他と衝突しないよう "Extensions": { "&lt;パッケージ ID&gt;": { ... } }
    /// のように名前空間を切ること。
    ///
    /// パッケージのツールが作ったノードに付ける識別子も、この決まりに沿って
    /// "Extensions" の下へ置いている（<c>Services/Tools/NodeToolKey.cs</c>）。
    /// 本体の欄を増やさないので、パッケージを入れていない版で開いて保存し直しても、
    /// ツールは次にそのノードを見つけ直せる。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
