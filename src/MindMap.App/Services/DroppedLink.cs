using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;

#pragma warning disable CS0618 // IDataObject は 11.3 で旧 API 扱いだが、ドロップの中身を形式名で引けるのはこちらだけ

namespace MindMap.Services;

/// <summary>
/// ドラッグ&ドロップされたデータからリンク文字列（URL かファイルパス）を取り出す。
/// ブラウザーやファイラーが渡してくる形式はばらばらなので、扱える順に試す（WPF 版と同じ考え方）。
///
/// ブラウザーの形式名（UniformResourceLocatorW など）は Windows のもの。他の OS では
/// text/uri-list か文字列で届くので、同じ順番で試せば拾える。
/// </summary>
public static class DroppedLink
{
    /// <summary>ブラウザーがリンクをドラッグしたときに使う形式（優先順）。</summary>
    private static readonly (string Format, Encoding Encoding)[] UrlFormats =
    [
        ("text/x-moz-url", Encoding.Unicode), // Firefox 系。"URL\nタイトル" の並び。
        ("UniformResourceLocatorW", Encoding.Unicode),
        ("UniformResourceLocator", Encoding.ASCII),
        ("text/uri-list", Encoding.UTF8),
        (DataFormats.Text, Encoding.UTF8),
    ];

    /// <summary>ノードのリンクに使える種類。ここに無いものは落としても何も作らない。</summary>
    private static readonly HashSet<string> LinkSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeFtp, Uri.UriSchemeMailto,
    };

    /// <summary>
    /// ドロップされた URL をすべて返す。ファイルのドロップでは空を返す
    /// （ファイルは <see cref="ExtractFiles"/> が扱う）。
    /// 題名まで返すのは、ノードを新しく作るときに 1 行目に使うため。
    /// </summary>
    public static IReadOnlyList<DroppedUrl> ExtractUrls(IDataObject data)
    {
        if (ExtractFiles(data).Count > 0)
        {
            return [];
        }

        foreach (var (format, encoding) in UrlFormats)
        {
            if (ReadLines(data, format, encoding) is not { Count: > 0 } lines)
            {
                continue;
            }

            // "URL\n題名" の繰り返しで渡す形式（text/x-moz-url）がある。
            // 2 行目が URL として読めなければ題名とみなす、という見分け方で両方に当たる。
            var urls = new List<DroppedUrl>();
            for (var i = 0; i < lines.Count; i++)
            {
                var url = lines[i];
                if (!IsLink(url))
                {
                    continue;
                }

                string? title = null;
                if (i + 1 < lines.Count && !IsLink(lines[i + 1]))
                {
                    title = lines[i + 1];
                    i++;
                }

                urls.Add(new DroppedUrl(url, title));
            }

            if (urls.Count > 0)
            {
                return urls;
            }
        }

        return [];
    }

    /// <summary>
    /// ドロップされたファイル（フォルダーを含む）のパスをすべて返す。
    /// 1 つ目だけを使う <see cref="Extract"/> と違い、まとめてノード化する用。
    /// </summary>
    public static IReadOnlyList<string> ExtractFiles(IDataObject data)
    {
        if (data.GetFiles() is not { } items)
        {
            return [];
        }

        return items
            .Select(item => item.TryGetLocalPath()?.Trim())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    /// <summary>ノードに落とされたときのリンク。ファイルならそのフルパス、URL なら先頭の 1 つ。</summary>
    public static string? Extract(IDataObject data)
    {
        if (ExtractFiles(data) is [var file, ..])
        {
            return file;
        }

        foreach (var (format, encoding) in UrlFormats)
        {
            if (ReadLines(data, format, encoding) is [var first, ..])
            {
                return first;
            }
        }

        return null;
    }

    /// <summary>ノードのリンクとして扱える文字列か。</summary>
    private static bool IsLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && LinkSchemes.Contains(uri.Scheme);

    /// <summary>形式 1 つぶんを行に分けて読む。題名や 2 つ目以降の URL も残す。</summary>
    private static IReadOnlyList<string>? ReadLines(IDataObject data, string format, Encoding encoding)
    {
        if (ReadRaw(data, format, encoding) is not { } text)
        {
            return null;
        }

        return text
            .Split('\n', '\r')
            .Select(line => line.Trim().Trim('\0').Trim())
            // text/uri-list は # で始まる行を注釈として使う。
            .Where(line => line.Length > 0 && line[0] != '#')
            .ToList();
    }

    /// <summary>形式 1 つぶんを文字列にして返す。渡され方（文字列 / バイト列 / ストリーム）を吸収する。</summary>
    private static string? ReadRaw(IDataObject data, string format, Encoding encoding)
    {
        try
        {
            if (!data.Contains(format))
            {
                return null;
            }

            var text = data.Get(format) switch
            {
                string s => s,
                byte[] bytes => encoding.GetString(bytes),
                Stream stream => ReadAll(stream, encoding),
                var other => other?.ToString(),
            };

            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception)
        {
            // 形式によっては取り出す途中で失敗する（ドラッグ元が渡し方を誤るなど）。次の形式を試す。
            return null;
        }
    }

    private static string ReadAll(Stream stream, Encoding encoding)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return encoding.GetString(copy.ToArray());
    }
}
