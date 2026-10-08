namespace MindMapX.Abstractions.Viewers;

/// <summary>
/// ある種類のビューアを作れることを名乗るもの。パッケージはこれを実装し、
/// マニフェスト（plugin.json）の contributes.viewers に型名を書いて登録する。
///
/// マニフェストにも扱える拡張子を書いておくと、該当するファイルが選ばれるまで
/// DLL を読み込まずに済む。<see cref="CanView"/> はその DLL を読んだあとの
/// 最終判断で、拡張子だけでは決まらない条件（中身を覗くなど）を書く場所。
/// </summary>
public interface IContentViewerFactory
{
    /// <summary>他と重ならない名前。設定や記録に出る。</summary>
    string Id { get; }

    /// <summary>
    /// 複数が扱えるときの優先順位。大きいほうが勝つ。
    /// 組み込みのテキスト表示は最小値を名乗るので、何を出しても勝てる。
    /// </summary>
    int Priority { get; }

    /// <summary>この中身を出せるか。ここでは重い処理をしないこと（選択のたびに呼ばれる）。</summary>
    bool CanView(ViewerContent content);

    /// <summary>ビューアを 1 つ作る。同じ種類が続く間は使い回されるので、毎回は呼ばれない。</summary>
    IContentViewer Create();
}
