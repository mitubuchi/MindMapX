using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace MindMap.Services;

/// <summary>
/// ビューアに出すためにファイルをテキストとして読む。
/// 表示できないものは中身の代わりに理由を返すので、呼ぶ側で分岐を書かなくてよい。
/// </summary>
public static class TextDocumentReader
{
    /// <summary>
    /// これより大きいファイルは読まない。全文を一度に読んで描くつくりなので、
    /// 上限を置かないと巨大なログを選んだだけで画面が止まってしまう。
    /// </summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>バイナリかどうかを見分けるために覗く先頭の長さ。</summary>
    private const int SniffLength = 8000;

    public static async Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxBytes)
            {
                return TextDocument.Unavailable(
                    $"ファイルが大きいため表示しません。({length:N0} バイト / 上限 {MaxBytes:N0} バイト)");
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

            if (TryReadBom(bytes) is { } bom)
            {
                return TextDocument.Of(bom.Encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length));
            }

            // BOM が無い UTF-16 は判別が難しいので追わない。ここで NUL が出るのは
            // 実行ファイルや画像とみなし、文字化けを見せる代わりに断る。
            if (bytes.AsSpan(0, Math.Min(SniffLength, bytes.Length)).IndexOf((byte)0) >= 0)
            {
                return TextDocument.Unavailable("テキストとして表示できないファイルです。");
            }

            return TextDocument.Of(Decode(bytes));
        }
        catch (OperationCanceledException)
        {
            // 次のノードに移ったことによる打ち切り。呼ぶ側が捨てるのでそのまま投げ返す。
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or SecurityException)
        {
            return TextDocument.Unavailable($"ファイルを読めませんでした。\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// BOM が無いテキストの文字コードを決める。まず UTF-8 として厳密に解釈してみて、
    /// 通らなければ Windows の既定コードページ（日本語環境なら Shift_JIS）で読み直す。
    /// 手元の古いテキストは Shift_JIS のことが多く、決め打ちでは文字化けするため。
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Ansi.GetString(bytes);
        }
    }

    /// <summary>
    /// OS の既定コードページ。<c>CodePagesEncodingProvider</c> を登録していないと引けないので、
    /// 引けなかった場合は文字化けを許容して UTF-8 に落とす。
    /// </summary>
    private static Encoding Ansi { get; } = ResolveAnsi();

    private static Encoding ResolveAnsi()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new UTF8Encoding(false);
        }
    }

    /// <summary>BOM が付いていればその文字コードと長さ。無ければ null。</summary>
    private static (Encoding Encoding, int Length)? TryReadBom(byte[] bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false), 3),
        [0xFF, 0xFE, ..] => (Encoding.Unicode, 2),
        [0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2),
        _ => null,
    };
}
