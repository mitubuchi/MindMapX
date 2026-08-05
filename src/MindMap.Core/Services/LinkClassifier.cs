using System.IO;

namespace MindMap.Services;

/// <summary>
/// リンク先（URL・ファイル・フォルダー）の種類を見分ける。
/// 種類ごとにどんなアイコンを描くかは UI 側の仕事なので、ここでは判定だけを行う。
/// OS の機能に触れないため、どのプラットフォームでも同じ結果になる。
/// </summary>
public static class LinkClassifier
{
    /// <summary>リンク先の種類。</summary>
    public static LinkKind Classify(string? link) => Resolve(link).Kind;

    /// <summary>
    /// 種類と、アイコンを引くときの単位を返す。
    /// <c>IconKey</c> は種類が <see cref="LinkKind.File"/> のときだけ入り、
    /// ふつうは拡張子、ファイルごとに固有のアイコンを持つものはそのパスになる。
    /// </summary>
    public static (LinkKind Kind, string? IconKey) Resolve(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return (LinkKind.Unknown, null);
        }

        var path = link.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
            {
                return uri.Scheme switch
                {
                    "http" or "https" => (LinkKind.Web, null),
                    "mailto" => (LinkKind.Mail, null),

                    // 独自のスキームはどう開かれるか分からないので、種類なしとして扱う。
                    _ => (LinkKind.Unknown, null),
                };
            }

            path = uri.LocalPath;
        }

        try
        {
            // 絶対パスのときだけ実物を見る。相対パスは基準の場所が分からないので、
            // 現在の作業フォルダーを基準に誤判定しないよう、名前だけで決める。
            if (Path.IsPathRooted(path))
            {
                if (Directory.Exists(path))
                {
                    return (LinkKind.Folder, null);
                }

                if (File.Exists(path) && HasOwnIcon(path))
                {
                    return (LinkKind.File, path);
                }
            }

            var extension = Path.GetExtension(path);

            // 拡張子が無いものはフォルダーとみなす。
            if (string.IsNullOrEmpty(extension))
            {
                return (LinkKind.Folder, null);
            }

            return string.Equals(extension, MindMapFileService.FileExtension, StringComparison.OrdinalIgnoreCase)
                ? (LinkKind.MindMap, null)
                : (LinkKind.File, extension);
        }
        catch (ArgumentException)
        {
            // パスに使えない文字が入っていた場合。種類は決められない。
            return (LinkKind.Unknown, null);
        }
    }

    /// <summary>
    /// 拡張子では代表させられない、ファイルごとに固有のアイコンを持つ種類か。
    /// macOS のアプリ（.app）もここに含める。
    /// </summary>
    private static bool HasOwnIcon(string path) =>
        Path.GetExtension(path).ToLowerInvariant()
            is ".exe" or ".lnk" or ".ico" or ".url" or ".msc" or ".cpl" or ".app";
}
