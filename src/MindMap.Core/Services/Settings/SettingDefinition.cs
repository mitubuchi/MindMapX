namespace MindMap.Services.Settings;

/// <summary>
/// 設定項目 1 つの決まりごと。config.txt に書く名前、設定画面での見せ方、既定値をまとめる。
///
/// 値そのものは持たない（値は <see cref="AppSettings"/> 側にある）。
/// 項目を増やすとき、触るのは <see cref="AppSettings.Definitions"/> の 1 か所だけで済むようにしてある。
/// </summary>
public sealed class SettingDefinition
{
    /// <summary>config.txt に書く名前。ファイルの中の見出しなので、一度決めたら変えない。</summary>
    public required string Key { get; init; }

    /// <summary>設定画面に出す名前。</summary>
    public required string Label { get; init; }

    /// <summary>設定画面で名前の下に添える説明。空なら何も出さない。</summary>
    public string Description { get; init; } = string.Empty;

    public SettingKind Kind { get; init; } = SettingKind.Text;

    /// <summary>config.txt に書かれていなかったときに使う値。</summary>
    public string Default { get; init; } = string.Empty;
}
