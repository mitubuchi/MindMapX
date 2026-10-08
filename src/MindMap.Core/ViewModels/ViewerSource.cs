namespace MindMap.ViewModels;

/// <summary>
/// ビューアに出す内容の出どころ。どちらを見るかは利用者の指定として保ち、
/// 選んだ側が出せないノード（リンクが無いなど）でも勝手にもう一方へ切り替えず、
/// 理由を出すだけにする。
/// </summary>
public enum ViewerSource
{
    /// <summary>ノードの本文。ビューアの上で書き換えられる。</summary>
    Body,

    /// <summary>ノードのリンク。表示のみ。</summary>
    Link,
}
