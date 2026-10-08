
namespace MindMap.Services.Settings;

/// <summary>
/// config.txt に入っている設定ひとそろい。
///
/// 値は「名前 → 文字列」の一覧として持ち、型の付いた読み方は下の方でかぶせている。
/// こうしておくと、項目を増やすときに触るのは <see cref="Definitions"/> の 1 行と、
/// （必要なら）呼び出し側から使う小さな受け口だけで済む。設定画面は
/// <see cref="Definitions"/> を見て組み立てるので、画面側には何も足さなくてよい。
///
/// 知らない名前もそのまま持っておき、保存時に書き戻す。新しい版や別のプログラムが
/// 足した項目を、それを知らない版で設定を保存しただけで失わないため
/// （<see cref="Models.MindMapDocument"/> の Extra と同じ考え方）。
/// </summary>
public sealed class AppSettings
{
    // 名前は契約側（HostSettings）と同じものを使う。パッケージからも同じ名前で引けるようにするため、
    // 2 か所に書かない。
    // 名前は WPF 版（MindMap.Abstractions の HostSettingKeys）と揃えてある。
    // config.txt を 2 つの版で共有しても、同じ項目として読めるようにするため。
    public const string RootPathKey = "Root Path";
    public const string DataPathKey = "Data Path";
    public const string RootRelativeLinksKey = "Root Relative Links";

    /// <summary>
    /// 設定項目の一覧。config.txt に書く順と、設定画面に並ぶ順を兼ねる。
    /// </summary>
    public static IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        new SettingDefinition
        {
            Key = RootPathKey,
            Label = "Root Path",
            Kind = SettingKind.Folder,
            Description = "ノードのリンクを相対パスで書くときの基準にするフォルダー。",
        },
        new SettingDefinition
        {
            Key = DataPathKey,
            Label = "Data Path",
            Kind = SettingKind.Folder,
            Description = "パッケージが書き出したファイルを置くフォルダー。本体はここに書きません。",
        },
        new SettingDefinition
        {
            Key = RootRelativeLinksKey,
            Label = "リンクを Root Path からの相対パスで保存する",
            Kind = SettingKind.Bool,
            Default = "true",
            Description = "Root Path の下にあるリンク先だけを置き換える。外にあるものは絶対パスのまま。",
        },
    ];

    /// <summary>ファイルに出てきた順。知らない項目を、読んだ順のまま書き戻すために持つ。</summary>
    private readonly List<string> _order = [];

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 名前で引く値。書かれていない項目は既定値（知らない名前なら空文字）を返すので、
    /// 呼び出し側で「無いとき」を書き分けなくてよい。
    /// </summary>
    public string this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : DefaultOf(key);
        set
        {
            if (!_values.ContainsKey(key))
            {
                _order.Add(key);
            }

            _values[key] = value ?? string.Empty;
        }
    }

    /// <summary>相対リンクの基準にするフォルダー。未設定なら空。</summary>
    public string RootPath
    {
        get => this[RootPathKey];
        set => this[RootPathKey] = value;
    }

    public string DataPath
    {
        get => this[DataPathKey];
        set => this[DataPathKey] = value;
    }

    /// <summary>保存時に、Root Path の下のリンクを相対パスへ置き換えるか。</summary>
    public bool UseRootRelativeLinks
    {
        get => IsTrue(this[RootRelativeLinksKey]);
        set => this[RootRelativeLinksKey] = value ? "true" : "false";
    }

    /// <summary>設定画面が編集する控え。OK を押すまで元の設定には触らない。</summary>
    public AppSettings Clone()
    {
        var copy = new AppSettings();

        foreach (var key in _order)
        {
            copy[key] = _values[key];
        }

        return copy;
    }

    /// <summary>
    /// ファイルに書く順に並べた中身。既知の項目を <see cref="Definitions"/> の順に並べ、
    /// そのあとへ知らない項目を読んだ順で続ける。
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> ToEntries()
    {
        var known = new HashSet<string>(Definitions.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            yield return new KeyValuePair<string, string>(definition.Key, this[definition.Key]);
        }

        foreach (var key in _order)
        {
            if (!known.Contains(key))
            {
                yield return new KeyValuePair<string, string>(key, _values[key]);
            }
        }
    }

    public static AppSettings FromEntries(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var settings = new AppSettings();

        foreach (var (key, value) in entries)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static string DefaultOf(string key) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))?.Default
        ?? string.Empty;

    /// <summary>
    /// 入切の読み取り。手で書き換えられるファイルなので、"1" や "yes" も真として受ける。
    /// 設定画面もこの読み方を使う（画面とファイルで解釈が割れないようにするため）。
    /// </summary>
    public static bool IsTrue(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        _ => false,
    };
}
