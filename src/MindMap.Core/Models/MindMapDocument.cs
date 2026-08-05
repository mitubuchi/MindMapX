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
    /// 5 = 制作日・更新日（CreatedAt / UpdatedAt 欄）を追加。
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    public const int CurrentVersion = 5;

    public List<MindMapNodeDto> Nodes { get; set; } = new();
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

    /// <summary>小さく表示する（タイトルのみ）なら true。既定は false。</summary>
    public bool Collapsed { get; set; }

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

    public double X { get; set; }

    public double Y { get; set; }
}
