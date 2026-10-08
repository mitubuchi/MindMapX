namespace MindMap.Services;

/// <summary>
/// ビューアに出す 1 件ぶんの中身。読めたテキストか、読めなかった理由のどちらか一方が入る。
/// 「読めなかった」を例外ではなく戻り値にしてあるのは、見つからない・大きすぎるといった
/// 事情がビューアでは日常的に起きるもので、その場に文言として出したいため。
/// </summary>
public sealed record TextDocument(string? Text, string? Message)
{
    public static TextDocument Of(string text) => new(text, null);

    public static TextDocument Unavailable(string message) => new(null, message);
}
