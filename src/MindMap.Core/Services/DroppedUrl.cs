namespace MindMap.Services;

/// <summary>
/// ブラウザーからドロップされた、URL とページの題名の組。
/// 題名は渡されないこともある（アドレス欄からのドラッグなど）。
///
/// 取り出し方は OS とドラッグ元ごとに違うので UI 側にあり、ここには結果の形だけを置く。
/// </summary>
public sealed record DroppedUrl(string Url, string? Title);
