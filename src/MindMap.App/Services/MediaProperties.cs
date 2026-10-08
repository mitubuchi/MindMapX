using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MindMap.Services;

/// <summary>
/// 画像・映像・音声ファイルの「詳細」プロパティを、エクスプローラーと同じ言い回しで取り出す。
///
/// 値の整形（"f/2.8"、"1/125 秒"、"30.00 フレーム/秒" など）は Windows のプロパティ機構に
/// 任せている。単位や桁の付け方を自前で持つと、エクスプローラーの表示とずれるため。
///
/// 拡張子では絞らない。写真の欄を持たないファイルは、引いても何も返って来ないだけなので、
/// 「音声だけの .mp3」「動画の入った .gif」のような境目を気にせずに済む。
///
/// Windows のプロパティシステムを使うので Windows でだけ動く。起動時に
/// <see cref="FileNodeContent.DescribeMedia"/> へ差し込み、他の OS ではその行が出ないだけにしてある。
/// </summary>
[SupportedOSPlatform("windows")]
public static class MediaProperties
{
    private const string DateFormat = "yyyy/MM/dd HH:mm:ss";

    /// <summary>
    /// 撮影時の設定。エクスプローラーの「詳細」に並ぶ順に合わせてある。
    /// 値の無い欄は行そのものを出さないので、写真以外では丸ごと消える。
    /// </summary>
    private static readonly (string Name, string Label)[] Camera =
    [
        ("System.Photo.CameraManufacturer", "カメラのメーカー"),
        ("System.Photo.CameraModel", "カメラのモデル"),
        ("System.Photo.LensModel", "レンズ"),
        ("System.Photo.FNumber", "F値"),
        ("System.Photo.ExposureTime", "露出時間"),
        ("System.Photo.ISOSpeed", "ISO 速度"),
        ("System.Photo.ExposureBias", "露出補正"),
        ("System.Photo.FocalLength", "焦点距離"),
        ("System.Photo.FocalLengthInFilm", "35 mm 換算焦点距離"),
        ("System.Photo.ExposureProgram", "露出プログラム"),
        ("System.Photo.MeteringMode", "測光モード"),
        ("System.Photo.WhiteBalance", "ホワイトバランス"),
        ("System.Photo.Flash", "フラッシュ"),
    ];

    /// <summary>
    /// ノードの本文に足す行を組み立てる。読めない・持っていないときは空。
    /// </summary>
    /// <param name="path">対象のファイル。フォルダーを渡してはいけない。</param>
    public static IReadOnlyList<string> Describe(string path)
    {
        var lines = new List<string>();

        try
        {
            using var store = PropertyStore.Open(path);
            if (store is null)
            {
                return lines;
            }

            // 大きさは、静止画なら「幅 x 高さ」が 1 つの欄に入っている。映像は幅と高さが
            // 別の欄なので、ここで同じ形に揃える。幅・高さ・解像度(dpi)を別々に並べても
            // 同じことの言い換えにしかならないため、この 1 行に集約している。
            Add(lines, "大きさ", store.Text("System.Image.Dimensions") ?? FrameSize(store));
            Add(lines, "ビットの深さ", store.Text("System.Image.BitDepth"));
            Add(lines, "長さ", store.Text("System.Media.Duration"));
            Add(lines, "フレームレート", store.Text("System.Video.FrameRate"));
            Add(lines, "ビットレート", store.Text("System.Video.TotalBitrate"));
            Add(lines, "音声", Audio(store));

            // 撮影日時はファイルの日時と別物（コピーすると作成日時は今になる）なので、
            // 値があるときだけ、ファイルの日時と同じ書き方で並べる。
            Add(lines, "撮影日時", store.Time("System.Photo.DateTaken")?.ToString(DateFormat));
            Add(lines, "記録日時", store.Time("System.Media.DateEncoded")?.ToString(DateFormat));

            foreach (var (name, label) in Camera)
            {
                Add(lines, label, store.Text(name));
            }

            Add(lines, "位置情報", Location(store));
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException)
        {
            // プロパティを扱えない場所（壊れたファイル、応答しないネットワーク先）でも、
            // ノード自体は作れるようにする。詳細だけを諦める。
            return lines;
        }

        return lines;
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }

    /// <summary>映像の「幅 x 高さ」。静止画の大きさと同じ見た目に揃える。</summary>
    private static string? FrameSize(PropertyStore store)
    {
        var width = store.Text("System.Video.FrameWidth");
        var height = store.Text("System.Video.FrameHeight");

        return width is null || height is null ? null : $"{width} x {height}";
    }

    /// <summary>音声は 3 つ並べても行が増えるだけなので、1 行にまとめる。</summary>
    private static string? Audio(PropertyStore store)
    {
        string?[] parts =
        [
            store.Text("System.Audio.ChannelCount"),
            store.Text("System.Audio.SampleRate"),
            store.Text("System.Audio.EncodingBitrate"),
        ];

        var known = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        return known.Length == 0 ? null : string.Join(" / ", known);
    }

    /// <summary>
    /// 撮影地。度分秒のままだと地図に貼れないので、十進の値と向き（N/S/E/W）で出す。
    /// </summary>
    private static string? Location(PropertyStore store)
    {
        if (store.Text("System.GPS.LatitudeDecimal") is not { } latitude
            || store.Text("System.GPS.LongitudeDecimal") is not { } longitude)
        {
            return null;
        }

        var ns = store.Text("System.GPS.LatitudeRef");
        var ew = store.Text("System.GPS.LongitudeRef");

        return $"{Trim(latitude)}{ns}, {Trim(longitude)}{ew}";

        // シェルは小数を長く返す（139.750333333333344）。地図に渡すには 6 桁で足りる。
        static string Trim(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var d)
                ? d.ToString("0.######", CultureInfo.InvariantCulture)
                : value;
    }

    /// <summary>
    /// ファイル 1 つ分のプロパティ。開くのに費用がかかるので、
    /// 欄を引くたびに開き直さず、ここに握っておく。
    /// </summary>
    private sealed class PropertyStore : IDisposable
    {
        private readonly IPropertyStore _store;

        private PropertyStore(IPropertyStore store) => _store = store;

        public static PropertyStore? Open(string path)
        {
            var iid = typeof(IPropertyStore).GUID;

            // BESTEFFORT を付けると、専用の読み手が居ない種類でもファイル自身の欄は返る。
            return SHGetPropertyStoreFromParsingName(path, nint.Zero, GpsReadOnly | GpsBestEffort, ref iid, out var store) == 0
                ? new PropertyStore(store)
                : null;
        }

        /// <summary>欄の値を、エクスプローラーに出るのと同じ文字列で返す。</summary>
        public string? Text(string canonicalName)
        {
            if (!TryGet(canonicalName, out var key, out var value))
            {
                return null;
            }

            try
            {
                if (PSFormatForDisplayAlloc(ref key, ref value, PdffDefault, out var text) != 0)
                {
                    return null;
                }

                var formatted = Marshal.PtrToStringUni(text);
                Marshal.FreeCoTaskMem(text);

                return Clean(formatted);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        /// <summary>日時の欄。ファイルの日時と同じ書き方に揃えたいので、生の値で受け取る。</summary>
        public DateTimeOffset? Time(string canonicalName)
        {
            if (!TryGet(canonicalName, out _, out var value))
            {
                return null;
            }

            try
            {
                // 写真の撮影日時は世界時で入っている。手元の時刻に直して返す。
                return PropVariantToFileTime(ref value, PstfUtc, out var time) == 0
                    ? new DateTimeOffset(DateTime.FromFileTimeUtc(ToLong(time)).ToLocalTime())
                    : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        public void Dispose() => Marshal.ReleaseComObject(_store);

        private bool TryGet(string canonicalName, out PropertyKey key, out PropVariant value)
        {
            value = default;

            if (PSGetPropertyKeyFromName(canonicalName, out key) != 0
                || _store.GetValue(ref key, out value) != 0)
            {
                return false;
            }

            // VT_EMPTY / VT_NULL は「その欄を持っていない」。
            return value.vt is not (0 or 1);
        }

        /// <summary>
        /// シェルの整形は、並びを保つための見えない記号（左横書きの指示など）を混ぜてくる。
        /// 本文はただの文字列として持ち回るので、ここで落としておく。
        /// </summary>
        private static string? Clean(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var kept = value.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format);

            return string.Concat(kept).Trim();
        }

        private static long ToLong(System.Runtime.InteropServices.ComTypes.FILETIME time) =>
            ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
    }

    // ------------------------------------------------------------ Win32

    private const int GpsReadOnly = 0x0;
    private const int GpsBestEffort = 0x40;
    private const int PdffDefault = 0x0;
    private const int PstfUtc = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>
    /// PROPVARIANT。中身は自分では読まず、シェルの整形関数にそのまま渡すだけなので、
    /// 大きさ（16 バイトの共用体）だけを合わせてある。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public nint value1;
        public nint value2;
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetAt(uint index, out PropertyKey key);

        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path,
        nint bindContext,
        int flags,
        ref Guid riid,
        out IPropertyStore store);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    private static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    private static extern int PSFormatForDisplayAlloc(ref PropertyKey key, ref PropVariant value, int flags, out nint text);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToFileTime(
        ref PropVariant value,
        int flags,
        out System.Runtime.InteropServices.ComTypes.FILETIME time);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
