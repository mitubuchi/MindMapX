namespace MindMap.Services;

/// <summary>
/// ノードに出す、リンク先の小さな絵を作る係。
///
/// 絵の型は画面の枠組みのもの（Avalonia の Bitmap）なので、ここでは object で受け渡す。
/// 作り方（どのパッケージに頼むか・どのスレッドで作るか・控えをどう持つか）は UI 側が決める。
/// </summary>
public interface INodeThumbnailSource
{
    /// <summary>リンク先の絶対パスから絵を作る。作れなければ null。</summary>
    Task<object?> GetAsync(string? absolutePath);
}

/// <summary>絵を作らない係。画面につながっていないとき（検証など）に使う。</summary>
public sealed class NullNodeThumbnailSource : INodeThumbnailSource
{
    public static NullNodeThumbnailSource Instance { get; } = new();

    private NullNodeThumbnailSource()
    {
    }

    public Task<object?> GetAsync(string? absolutePath) => Task.FromResult<object?>(null);
}
