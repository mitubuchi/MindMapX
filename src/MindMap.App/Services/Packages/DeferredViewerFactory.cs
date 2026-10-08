using MindMapX.Abstractions.Viewers;

namespace MindMap.Services.Packages;

/// <summary>
/// マニフェストの宣言だけで名乗り、実際に選ばれたときに初めて DLL を読むビューア。
///
/// 起動のたびに全パッケージの DLL を読むと、重い描画を抱えたものが 1 つ入っただけで
/// 起動が遅くなる。扱える拡張子はマニフェストに書いてあるので、そこまでは
/// 中身を読まずに判断できる。
/// </summary>
internal sealed class DeferredViewerFactory : IContentViewerFactory
{
    private readonly string[] _extensions;
    private readonly Func<IContentViewerFactory> _load;

    private IContentViewerFactory? _inner;

    public DeferredViewerFactory(
        string id,
        int priority,
        IEnumerable<string> extensions,
        Func<IContentViewerFactory> load)
    {
        Id = id;
        Priority = priority;
        _extensions = extensions
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToArray();
        _load = load;
    }

    public string Id { get; }

    public int Priority { get; }

    /// <summary>
    /// まずマニフェストの拡張子で見る。すでに読み込んであれば、中のファクトリにも聞く
    /// （拡張子だけでは決まらない条件を書いてある場合に効かせるため）。
    /// </summary>
    public bool CanView(ViewerContent content)
    {
        if (!_extensions.Contains(content.Extension))
        {
            return false;
        }

        return _inner is null || _inner.CanView(content);
    }

    public IContentViewer Create() => Inner().Create();

    private IContentViewerFactory Inner() => _inner ??= _load();
}
