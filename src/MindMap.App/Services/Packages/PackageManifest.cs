using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindMap.Services.Packages;

/// <summary>
/// パッケージ 1 つぶんの plugin.json。
///
/// 提供物は種類ごとに分けて書く（<see cref="PackageContributions"/>）。
/// 種類を増やすときは、ホスト側にその種類のレジストリを 1 つ足し、ここに欄を 1 つ増やすだけで、
/// 既にあるパッケージにも読み込みの流れにも手を入れずに済む。
/// </summary>
public sealed class PackageManifest
{
    /// <summary>他と重ならない名前。"com.nwco.mdviewer" のような逆ドメイン形式にする。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>画面や記録に出す名前。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>パッケージ自身の版。ホストは中身を見ず、記録に出すだけ。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 想定している契約（MindMapX.Abstractions）の版。
    /// 大版が食い違うパッケージは読み込まない。
    /// </summary>
    public string ApiVersion { get; set; } = string.Empty;

    public PackageEntry? Entry { get; set; }

    public PackageContributions Contributes { get; set; } = new();
}

/// <summary>提供物が入っている DLL。</summary>
public sealed class PackageEntry
{
    /// <summary>パッケージのフォルダーからの相対名。依存 DLL は .deps.json から解決される。</summary>
    public string Assembly { get; set; } = string.Empty;
}

/// <summary>
/// このパッケージが提供するものを、種類ごとに分けて並べたもの。
///
/// 知らない種類は <see cref="Extra"/> に落ちるだけで、読み込みは止まらない。
/// 新しい種類に対応したパッケージを、まだ対応していないホストに入れても
/// 「その種類だけが効かない」で済むようにするため。
/// </summary>
public sealed class PackageContributions
{
    /// <summary>リンク先の表示。</summary>
    public List<ViewerContribution> Viewers { get; set; } = [];

    /// <summary>ノードに出す、リンク先の小さな絵。</summary>
    public List<ThumbnailContribution> Thumbnails { get; set; } = [];

    /// <summary>
    /// 知らない種類。WPF 版の "tools" もここに落ちる（MindMapX はツールを受け付けない）。
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>ビューアを 1 つ提供する宣言。</summary>
public sealed class ViewerContribution
{
    /// <summary><c>IContentViewerFactory</c> を実装した型の完全名。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 扱える拡張子（"." 付き）。ここに書いておくと、該当するファイルが選ばれるまで
    /// DLL を読み込まずに済む。
    /// </summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>同じ拡張子を複数のパッケージが名乗ったときの優先順位。大きいほうが勝つ。</summary>
    public int Priority { get; set; } = 100;
}

/// <summary>サムネイルを 1 つ提供する宣言。</summary>
public sealed class ThumbnailContribution
{
    /// <summary><c>INodeThumbnailProvider</c> を実装した型の完全名。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 扱える拡張子（"." 付き）。ここに書いておくと、該当するリンクが現れるまで
    /// DLL を読み込まずに済む。
    /// </summary>
    public List<string> Extensions { get; set; } = [];

    /// <summary>同じ拡張子を複数のパッケージが名乗ったときの優先順位。大きいほうが勝つ。</summary>
    public int Priority { get; set; } = 100;
}
