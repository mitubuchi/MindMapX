using System.IO;
using Avalonia.Media.Imaging;

namespace MindMapX.Abstractions.Thumbnails;

/// <summary>
/// リンク先 1 種類ぶんの小さな絵を作れることを名乗るもの。パッケージはこれを実装し、
/// マニフェスト（plugin.json）の contributes.thumbnails に型名を書いて登録する。
///
/// ビューア（<see cref="Viewers.IContentViewer"/>）が画面そのものを返すのに対して、
/// こちらは<b>絵だけ</b>を返す。ノードのどこにどう出すか（白い下地・中央寄せ・縦横比を保つ・
/// 本文と同じ表示切り替え）はホストが決めるので、どのパッケージが入っても見た目が揃う。
///
/// マニフェストにも扱える拡張子を書いておくと、該当するリンクが現れるまで DLL を読み込まずに済む。
/// <see cref="CanProvide"/> はその DLL を読んだあとの最終判断。
/// </summary>
public interface INodeThumbnailProvider
{
    /// <summary>他と重ならない名前。設定や記録に出る。</summary>
    string Id { get; }

    /// <summary>複数が扱えるときの優先順位。大きいほうが勝つ。</summary>
    int Priority { get; }

    /// <summary>この中身の絵を作れるか。ここでは重い処理をしないこと。</summary>
    bool CanProvide(ThumbnailRequest request);

    /// <summary>
    /// 絵を作る。作れなければ null を返す（例外ではなく null。絵が無いノードは
    /// ただの本文つきノードとして描かれるだけなので、ホスト側に分岐を作らない）。
    ///
    /// <b>UI スレッドでは呼ばれない。</b> ホストが用意した作業スレッドで呼ぶ。
    /// マップを開いた瞬間に何十枚も作ることがあり、UI スレッドで回すと画面が固まるため。
    /// Windows では STA にしてある（OS のサムネイル＝動画のポスターフレームが COM を使うから）。
    ///
    /// Avalonia の <see cref="Bitmap"/> はどのスレッドで作ってもそのまま UI で描ける
    /// （WPF の Freeze にあたる手順は要らない）。
    /// </summary>
    Task<Bitmap?> GetAsync(ThumbnailRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 絵を作ってほしい対象。<see cref="Viewers.ViewerContent"/> と同じく、
/// 種類を決める材料をホスト側で用意しておく（パッケージごとに拡張子の取り出し方が違うと、
/// 同じファイルでも選ばれる提供物が変わってしまうため）。
/// </summary>
/// <param name="FilePath">絶対パス。ホスト側で相対リンクを解決済み。</param>
/// <param name="Size">一辺の目安（px）。この大きさの正方形に収めて出す。</param>
public sealed record ThumbnailRequest(string FilePath, int Size)
{
    /// <summary>小文字の拡張子（"." 付き）。拡張子が無ければ空。</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
}
