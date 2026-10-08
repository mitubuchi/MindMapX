using Avalonia.Media.Imaging;
using MindMapX.Abstractions.Thumbnails;

namespace MindMap.Services.Packages;

/// <summary>
/// マニフェストの宣言だけで名乗り、実際に絵を求められたときに初めて DLL を読む提供物。
/// 考え方は <see cref="DeferredViewerFactory"/> と同じ（起動時に全パッケージを読まないため）。
/// </summary>
internal sealed class DeferredThumbnailProvider : INodeThumbnailProvider
{
    private readonly string[] _extensions;
    private readonly Func<INodeThumbnailProvider> _load;

    private INodeThumbnailProvider? _inner;

    public DeferredThumbnailProvider(
        string id,
        int priority,
        IEnumerable<string> extensions,
        Func<INodeThumbnailProvider> load)
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
    /// まずマニフェストの拡張子で見る。すでに読み込んであれば、中の提供物にも聞く
    /// （拡張子だけでは決まらない条件を書いてある場合に効かせるため）。
    /// </summary>
    public bool CanProvide(ThumbnailRequest request)
    {
        if (!_extensions.Contains(request.Extension))
        {
            return false;
        }

        return _inner is null || _inner.CanProvide(request);
    }

    public Task<Bitmap?> GetAsync(ThumbnailRequest request, CancellationToken cancellationToken) =>
        Inner().GetAsync(request, cancellationToken);

    private INodeThumbnailProvider Inner() => _inner ??= _load();
}
