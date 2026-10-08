using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MindMapX.Abstractions.Viewers;

namespace MindMap.Services.Viewers;

/// <summary>
/// ビューアの枠 1 つぶん。種類を選び、作り、使い回す。
///
/// 同じ種類が続く間はビューアを作り直さない。文字を出すだけなら安いが、
/// ブラウザーを抱えるようなパッケージでは生成がそのまま待ち時間になるため。
/// 種類が変わったときだけ前のものを捨てる。
/// </summary>
public sealed class ViewerSession : ILinkViewerHost, IDisposable
{
    private readonly ViewerRegistry _registry;

    private IContentViewerFactory? _factory;
    private IContentViewer? _viewer;

    public ViewerSession(ViewerRegistry registry) => _registry = registry;

    /// <summary>
    /// 中身を出して、枠に入れる画面を返す。
    /// パッケージが落ちてもアプリを巻き込まないよう、ここで受け止めて枠ごと差し替える。
    /// </summary>
    /// <summary>ビューア欄（Core 側）からの入口。画面の型を知らない相手なので object で返す。</summary>
    async Task<object> ILinkViewerHost.ShowAsync(string path, bool isDirectory, CancellationToken cancellationToken) =>
        await ShowAsync(new ViewerContent(path) { IsDirectory = isDirectory }, cancellationToken);

    public async Task<Control> ShowAsync(ViewerContent content, CancellationToken cancellationToken)
    {
        IContentViewerFactory factory;

        try
        {
            factory = _registry.Resolve(content);
        }
        catch (Exception ex)
        {
            return Notice($"表示するものを決められませんでした。\n\n{ex.Message}");
        }

        if (!ReferenceEquals(factory, _factory))
        {
            Release();
        }

        try
        {
            if (_viewer is null)
            {
                _viewer = factory.Create();
                _factory = factory;
            }

            await _viewer.LoadAsync(content, cancellationToken);
            return _viewer.View;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 壊れたビューアを抱え続けない。次に選ばれたときは作り直す。
            Release();
            return Notice($"「{factory.Id}」で表示できませんでした。\n\n{ex.Message}");
        }
    }

    public void Dispose() => Release();

    private void Release()
    {
        try
        {
            _viewer?.Dispose();
        }
        catch (Exception)
        {
            // 後始末の失敗まで拾って伝えることはしない。手放せればそれでよい。
        }

        _viewer = null;
        _factory = null;
    }

    /// <summary>ビューアを用意できなかったときに、代わりに枠へ入れる画面。</summary>
    private static Control Notice(string message) => new TextBlock
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA0)),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
