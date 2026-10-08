using MindMapX.Abstractions.Thumbnails;

namespace MindMap.Services.Thumbnails;

/// <summary>
/// リンク先の種類ごとに、どの提供物が絵を作るかを決める。
///
/// <see cref="Viewers.ViewerRegistry"/> と同じ形。違うのは<b>組み込みが 1 つも無い</b>ことで、
/// パッケージが 1 つも入っていなければ誰も名乗らず、ノードは今までどおり本文だけを出す。
/// 絵を出すこと自体がパッケージの持ち込む機能なので、受け皿は要らない。
/// </summary>
public sealed class ThumbnailRegistry
{
    private readonly List<INodeThumbnailProvider> _providers = [];

    /// <summary>
    /// 提供物を足す。同じ <see cref="INodeThumbnailProvider.Id"/> は後から来たほうで置き換える。
    /// </summary>
    public void Add(INodeThumbnailProvider provider)
    {
        _providers.RemoveAll(p => p.Id == provider.Id);
        _providers.Add(provider);

        // 数が少ないので、足すたびに並べ直して構わない。
        _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>
    /// 作れる提供物のうち、いちばん優先度が高いもの。誰も名乗らなければ null
    /// （そのノードには絵が付かない、というだけ）。
    /// </summary>
    public INodeThumbnailProvider? Resolve(ThumbnailRequest request) =>
        _providers.FirstOrDefault(p => p.CanProvide(request));

    /// <summary>1 つでも入っているか。空なら、そもそも絵を作りにいかない。</summary>
    public bool IsEmpty => _providers.Count == 0;

    /// <summary>入っている提供物の名前。記録に出すため。</summary>
    public IReadOnlyList<string> Ids => _providers.Select(p => p.Id).ToList();
}
