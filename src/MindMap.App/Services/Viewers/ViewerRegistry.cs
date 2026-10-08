using MindMapX.Abstractions.Viewers;

namespace MindMap.Services.Viewers;

/// <summary>
/// リンク先の種類ごとに、どのビューアで出すかを決める。
///
/// 組み込みぶんを最初から持ち、パッケージが見つかったぶんを <see cref="Add"/> で足す。
/// 種類の増え方はここに集まるので、呼ぶ側（ビューアの枠）は何が入っているかを知らない。
/// 別の種類の提供物（操作など）を足すときは、これと同じ形のレジストリをもう 1 つ作る。
/// </summary>
public sealed class ViewerRegistry
{
    private readonly List<IContentViewerFactory> _factories = [];

    public ViewerRegistry(IEnumerable<IContentViewerFactory>? factories = null)
    {
        foreach (var factory in factories ?? Builtin())
        {
            Add(factory);
        }
    }

    /// <summary>
    /// ビューアを足す。同じ <see cref="IContentViewerFactory.Id"/> は後から来たほうで置き換える
    /// （組み込みの振る舞いをパッケージで差し替えられるように）。
    /// </summary>
    public void Add(IContentViewerFactory factory)
    {
        _factories.RemoveAll(f => f.Id == factory.Id);
        _factories.Add(factory);

        // 数が少ないので、足すたびに並べ直して構わない。
        _factories.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>
    /// 扱えるビューアのうち、いちばん優先度が高いもの。
    /// 受け皿の <see cref="PlainTextViewerFactory"/> がすべてを引き受けるので、必ず 1 つ返る。
    /// </summary>
    public IContentViewerFactory Resolve(ViewerContent content) =>
        _factories.First(f => f.CanView(content));

    /// <summary>入っているビューアの名前。設定画面や記録に出すため。</summary>
    public IReadOnlyList<string> Ids => _factories.Select(f => f.Id).ToList();

    private static IEnumerable<IContentViewerFactory> Builtin()
    {
        yield return new MindMapOutlineViewerFactory();
        yield return new FolderListViewerFactory();
        yield return new PlainTextViewerFactory();
    }
}
