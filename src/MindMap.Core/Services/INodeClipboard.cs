using MindMap.Models;

namespace MindMap.Services;

/// <summary>
/// ノードのかたまりをクリップボード経由で受け渡す。タブ内だけで持ち回さないのは、
/// 別のタブ・別ウィンドウ・別プロセスの MindMap との間でもやり取りできるようにするため。
/// 実際の読み書きは OS ごとに違うので、UI 側が実装を渡す。
/// </summary>
public interface INodeClipboard
{
    Task WriteAsync(MindMapDocument fragment);

    /// <summary>貼り付けられるノードを取り出す。無ければ null。</summary>
    Task<MindMapDocument?> ReadAsync();
}

/// <summary>
/// クリップボードを使えない場面（テストや、まだ画面につながっていない状態）向けの実装。
/// 何も起きないだけで、呼び出し側は同じように書ける。
/// </summary>
public sealed class NullNodeClipboard : INodeClipboard
{
    public static readonly NullNodeClipboard Instance = new();

    public Task WriteAsync(MindMapDocument fragment) => Task.CompletedTask;

    public Task<MindMapDocument?> ReadAsync() => Task.FromResult<MindMapDocument?>(null);
}
