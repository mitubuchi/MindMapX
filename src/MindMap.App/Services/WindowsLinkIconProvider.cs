using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MindMap.Services;

/// <summary>
/// Windows の関連付けから、ファイルの種類に応じたアプリのアイコンを取り出す。
/// PowerPoint やテキストなど、利用者が見慣れたアイコンをそのまま出せる。
/// 他の OS では使わない（<see cref="LinkIcons.Current"/> に差し込まない）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLinkIconProvider : ILinkIconProvider
{
    /// <summary>
    /// 拡張子ごとに 1 回だけ引く。ノードの数だけシェルに問い合わせると重く、
    /// 関連付けはアプリの起動中に変わることも稀なため。
    /// </summary>
    private readonly Dictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IImage? ForLink(string? link)
    {
        if (LinkClassifier.Resolve(link) is not { Kind: LinkKind.File, IconKey: { } key })
        {
            return null;
        }

        if (!_cache.TryGetValue(key, out var icon))
        {
            icon = Load(key);
            _cache[key] = icon;
        }

        return icon;
    }

    private static IImage? Load(string key)
    {
        var info = default(ShFileInfo);

        // 拡張子だけを渡すときは USEFILEATTRIBUTES を付ける。実在しないパスでも
        // 「その拡張子のアイコン」を引けて、ディスクにも触らないので速い。
        var useAttributes = !Path.IsPathRooted(key);
        var flags = ShgfiIcon | ShgfiLargeIcon | (useAttributes ? ShgfiUseFileAttributes : 0);

        if (SHGetFileInfo(key, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == 0
            || info.hIcon == nint.Zero)
        {
            return null;
        }

        try
        {
            // アイコンのハンドルを PNG に起こしてから Avalonia の画像にする。
            using var icon = Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();

            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception)
        {
            // 壊れたアイコンを持つファイルもある。その場合は線画のアイコンに任せる。
            return null;
        }
        finally
        {
            // ハンドルは OS の資源なので、画像に写した時点で返す。
            DestroyIcon(info.hIcon);
        }
    }

    // ------------------------------------------------------------ Win32

    private const uint FileAttributeNormal = 0x80;

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
