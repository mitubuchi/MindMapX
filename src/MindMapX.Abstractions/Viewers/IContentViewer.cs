using Avalonia.Controls;

namespace MindMapX.Abstractions.Viewers;

/// <summary>
/// リンク先を 1 種類ぶん描くもの。<see cref="View"/> がそのままビューアの枠に入る。
///
/// 生成は <see cref="IContentViewerFactory"/> が行い、同じ種類が続く間は
/// 作り直さず使い回される（ブラウザーのような重い描画を毎回作らないため）。
/// 別の種類に切り替わるときに <see cref="IDisposable.Dispose"/> が呼ばれる。
/// </summary>
public interface IContentViewer : IDisposable
{
    /// <summary>
    /// 描画そのもの。生成してから捨てられるまで、同じものを返し続けること
    /// （ホストは 1 度だけ枠に入れ、あとは <see cref="LoadAsync"/> で中身が
    /// 差し替わることを期待する）。
    /// </summary>
    Control View { get; }

    /// <summary>
    /// 中身を読み込んで <see cref="View"/> に反映する。UI スレッドで呼ばれる。
    ///
    /// 読めなかったときは例外を投げず、その理由を <see cref="View"/> の中に出すこと。
    /// ビューアの都合はビューアの枠内で完結させ、ホスト側に分岐を作らないため。
    /// 例外が出た場合はホストが捕まえて、枠ごと差し替えて知らせる。
    ///
    /// 選択が次に移ると <paramref name="cancellationToken"/> が切られる。
    /// 時間のかかる読み込みは適宜見ること。
    /// </summary>
    Task LoadAsync(ViewerContent content, CancellationToken cancellationToken);
}
