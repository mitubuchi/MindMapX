using Avalonia.Input;
using Avalonia.Input.Platform;
using MindMap.Models;

namespace MindMap.Services;

/// <summary>
/// Avalonia のクリップボードを使った <see cref="INodeClipboard"/> の実装。
/// MindMap 専用の形式（JSON）と、他のアプリに貼るための箇条書きの両方を載せる。
/// 専用形式を扱えない相手でも、箇条書きの方から 1 ノードとして貼り付けられる。
/// </summary>
public sealed class AvaloniaNodeClipboard(Func<IClipboard?> clipboard) : INodeClipboard
{
    /// <summary>MindMap どうしでノードをやり取りするための、このアプリ専用の形式。</summary>
    private static readonly DataFormat<string> NodesFormat =
        DataFormat.CreateStringApplicationFormat(NodeClipboardFormat.ClipboardFormat);

    public async Task WriteAsync(MindMapDocument fragment)
    {
        if (clipboard() is not { } board)
        {
            return;
        }

        var item = new DataTransferItem();
        item.Set(NodesFormat, NodeClipboardFormat.Serialize(fragment));
        item.SetText(NodeClipboardFormat.ToOutline(fragment));

        var transfer = new DataTransfer();
        transfer.Add(item);

        try
        {
            await board.SetDataAsync(transfer);
        }
        catch (Exception)
        {
            // クリップボードは OS 全体で 1 つしかなく、他のアプリが握っていると失敗することがある。
            // コピー操作そのものを落とすほどではないので、黙って諦める。
        }
    }

    public async Task<MindMapDocument?> ReadAsync()
    {
        if (clipboard() is not { } board)
        {
            return null;
        }

        try
        {
            var data = await board.TryGetDataAsync();
            if (data is null)
            {
                return null;
            }

            // MindMap が載せた JSON があればそれを使う。
            if (NodeClipboardFormat.Deserialize(await data.TryGetValueAsync(NodesFormat)) is { } fragment)
            {
                return fragment;
            }

            // 他のアプリからコピーされた文字列は、1 つのノードとして受け取る。
            return NodeClipboardFormat.FromText(await data.TryGetTextAsync());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
