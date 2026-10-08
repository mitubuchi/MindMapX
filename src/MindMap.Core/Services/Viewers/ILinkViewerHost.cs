namespace MindMap.Services.Viewers;

/// <summary>
/// リンク先を描く係。種類ごとのビューア（組み込みとパッケージ）を選び、作った画面を返す。
///
/// 返る画面は画面の枠組みの部品（Avalonia の Control）なので、ここでは object で受け渡す。
/// <see cref="ViewModels.ViewerViewModel"/> は出来上がったものを枠に載せるだけで、中身を知らない。
/// </summary>
public interface ILinkViewerHost
{
    /// <summary>
    /// 中身を出して、枠に入れる画面を返す。表示できなかったときも、理由を書いた画面を返す
    /// （例外は取り消しのときだけ）。
    /// </summary>
    Task<object> ShowAsync(string path, bool isDirectory, CancellationToken cancellationToken);
}

/// <summary>何も描かない係。画面につながっていないとき（検証など）に使う。</summary>
public sealed class NullLinkViewerHost : ILinkViewerHost
{
    public static NullLinkViewerHost Instance { get; } = new();

    private NullLinkViewerHost()
    {
    }

    public Task<object> ShowAsync(string path, bool isDirectory, CancellationToken cancellationToken) =>
        Task.FromResult<object>(path);
}
