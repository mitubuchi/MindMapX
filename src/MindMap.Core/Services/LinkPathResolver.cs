using System.IO;
using MindMap.Services.Settings;

namespace MindMap.Services;

/// <summary>
/// ノードのリンクを、実際のファイルの場所に直す。
///
/// 相対パスの基準は 2 つある。設定の Root Path と、リンク元のタブが置かれた場所。
/// 保存時は Root からの相対で書く（<see cref="ToStoredLink"/>）が、それより前に作った
/// ファイルにはタブ基準の相対パスが入っているので、読むときは両方を試して
/// 実在する方を採る。ビューアとノードのサムネイルが別々の解き方をすると、
/// 同じリンクなのに片方だけ出る、という食い違いが起きるので 1 か所にまとめてある。
/// </summary>
public static class LinkPathResolver
{
    /// <param name="link">ノードに設定されたリンク。</param>
    /// <param name="baseFilePath">
    /// 相対パスの基準にするマップのファイル。まだ保存していないタブでは null で、
    /// その場合は Root Path だけが基準になる（Root も未設定なら相対パスを解けない）。
    /// </param>
    public static string? Resolve(string? link, string? baseFilePath)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        var trimmed = link.Trim();

        try
        {
            // file:/// 形式で書かれたときだけ Uri 経由で直す。ふつうのパスまで通すと、
            // # などがフラグメントの記号として解釈されて途中で切れてしまう。
            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                uri.IsFile)
            {
                return uri.LocalPath;
            }

            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            var settings = SettingsService.Current;
            var fromRoot = Combine(settings.RootPath, trimmed);
            var fromBase = baseFilePath is { Length: > 0 } baseFile
                ? Combine(Path.GetDirectoryName(baseFile), trimmed)
                : null;

            // 実在する方を採る。ふつうはどちらか一方にしか無いので、これで決まる。
            if (fromRoot is not null && Exists(fromRoot))
            {
                return fromRoot;
            }

            if (fromBase is not null && Exists(fromBase))
            {
                return fromBase;
            }

            // どちらにも無いとき（リンク先が消えているなど）は、このアプリが書く形を
            // 名乗る方が、出るメッセージのパスと保存されている内容が食い違わない。
            return settings.UseRootRelativeLinks && fromRoot is not null
                ? fromRoot
                : fromBase ?? fromRoot;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // パスに使えない文字が入っているなど。場所が決められなかったものとして扱う。
            return null;
        }
    }

    /// <summary>
    /// ファイルに書くときのリンク。Root Path の下を指す絶対パスだけを、Root からの
    /// 相対パスに置き換える。
    ///
    /// Root の外や別ドライブのものは絶対パスのままにする（<c>..\..\..\</c> が並ぶと
    /// 目で追えなくなるうえ、Root を動かしても得をしないため）。
    /// すでに相対で書かれているリンクには触らない。基準を勝手に読み替えると、
    /// 「子ノードを別のファイルに保存」が張る相互リンクのように、タブ基準で
    /// 書かれたものの意味が変わってしまう。
    /// </summary>
    public static string ToStoredLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return link ?? string.Empty;
        }

        var settings = SettingsService.Current;
        var trimmed = link.Trim();

        if (!settings.UseRootRelativeLinks ||
            settings.RootPath is not { Length: > 0 } root ||
            !Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(trimmed));

            // Root の外だと ..\ が付き、別ドライブだと絶対パスがそのまま返る。
            // どちらも置き換えない。
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? link
                : relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return link;
        }
    }

    private static string? Combine(string? baseDirectory, string relative) =>
        baseDirectory is { Length: > 0 } directory
            ? Path.GetFullPath(Path.Combine(directory, relative))
            : null;

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
