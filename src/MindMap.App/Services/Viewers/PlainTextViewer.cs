using MindMapX.Abstractions.Viewers;

namespace MindMap.Services.Viewers;

/// <summary>
/// 種類ごとのビューアが見つからなかったときの受け皿。テキストとして読めれば出す。
/// これがあるおかげで、呼ぶ側は「対応するビューアが無い場合」を書かなくて済む。
/// </summary>
public sealed class PlainTextViewerFactory : IContentViewerFactory
{
    public string Id => "builtin.text";

    /// <summary>受け皿なので、他のどのビューアにも譲る。</summary>
    public int Priority => int.MinValue;

    public bool CanView(ViewerContent content) => true;

    public IContentViewer Create() => new PlainTextViewer();
}

internal sealed class PlainTextViewer : TextContentViewer
{
    protected override Task<TextDocument> BuildAsync(
        ViewerContent content,
        CancellationToken cancellationToken) =>
        TextDocumentReader.ReadAsync(content.FilePath, cancellationToken);
}
