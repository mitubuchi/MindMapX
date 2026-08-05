using Avalonia.Media;

namespace MindMap.Services;

/// <summary>
/// リンク先のファイルに関連付けられたアプリのアイコンを取り出す。
/// OS ごとに手段が違い、用意できない OS もあるので、実装は起動時に差し込む。
/// 用意できない場合は線画のアイコン（<see cref="Resources.Icons.File"/>）で代用する。
/// </summary>
public interface ILinkIconProvider
{
    /// <summary>種類が「ファイル」のリンクのアイコン。引けなければ null。</summary>
    IImage? ForLink(string? link);
}

/// <summary>今この OS で使えるアイコンの取り出し方。起動時に <see cref="Current"/> を差し替える。</summary>
public static class LinkIcons
{
    public static ILinkIconProvider? Current { get; set; }

    public static IImage? ForLink(string? link) => Current?.ForLink(link);
}
