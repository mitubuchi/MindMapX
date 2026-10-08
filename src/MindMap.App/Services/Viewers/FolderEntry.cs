namespace MindMap.Services.Viewers;

/// <summary>
/// フォルダーの一覧の 1 行。
///
/// <see cref="Link"/> という名前にしてあるのは、ノードの脇でリンクの絵柄を出している
/// <c>LinkGlyph</c> スタイルをそのまま使うため（あれは DataContext の Link を見る）。
/// おかげでキャンバス上のアイコンと一覧のアイコンが必ず同じものになる。
/// </summary>
public sealed class FolderEntry
{
    public required string Link { get; init; }

    public required string Name { get; init; }

    /// <summary>整形済みのサイズ。フォルダーは空にする。</summary>
    public required string Size { get; init; }

    /// <summary>整形済みの更新日時。読めなければ空。</summary>
    public required string Modified { get; init; }
}
