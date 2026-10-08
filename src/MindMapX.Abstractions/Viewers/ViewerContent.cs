using System.IO;

namespace MindMapX.Abstractions.Viewers;

/// <summary>
/// ビューアに渡す、出す対象。ノードのリンク先のファイル、またはフォルダー。
///
/// 種類を決める材料（<see cref="Extension"/>）をここで用意しておくのは、
/// パッケージごとに拡張子の取り出し方が違うと、同じファイルでも
/// 選ばれるビューアが変わってしまうため。
/// </summary>
/// <param name="FilePath">絶対パス。ホスト側で相対リンクを解決済み。</param>
public sealed record ViewerContent(string FilePath)
{
    /// <summary>
    /// フォルダーを指しているか。既定は false（ファイル）。
    /// ホストが判断して立てるので、ビューア側でディスクを触り直さなくてよい。
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// 小文字の拡張子（"." 付き）。拡張子が無ければ空。
    ///
    /// フォルダーのときも空にしてある。"docs.md" のような名前のフォルダーが、
    /// 拡張子だけを見るビューアに拾われないようにするため。
    /// フォルダーを扱うビューアは <see cref="IsDirectory"/> を見ること。
    /// </summary>
    public string Extension =>
        IsDirectory ? string.Empty : Path.GetExtension(FilePath).ToLowerInvariant();

    /// <summary>ファイル名（フォルダーならその名前）だけ。見出しなどに使う。</summary>
    public string FileName { get; } =
        Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            is { Length: > 0 } name
            ? name
            : FilePath;
}
