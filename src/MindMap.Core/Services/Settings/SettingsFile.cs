using System.IO;
using System.Text;

namespace MindMap.Services.Settings;

/// <summary>
/// config.txt の読み書き。1 行 1 項目で <c>名前 : "値"</c> の形で書く。
///
/// テキストエディタで開いて直せる形にしてある（JSON にしないのはそのため）。
/// <c>#</c> で始まる行と空行は読み飛ばすので、覚え書きを書き足しても壊れない。
/// </summary>
public static class SettingsFile
{
    /// <summary>
    /// 読み込む。名前は最初の <c>:</c> までで、そこから先はすべて値
    /// （<c>C:\...</c> のように値の中に <c>:</c> が入るため、分けるのは 1 回だけ）。
    /// 値を囲む二重引用符は外す。
    /// </summary>
    public static List<KeyValuePair<string, string>> Read(string path)
    {
        var entries = new List<KeyValuePair<string, string>>();

        // BOM があればそれに従い、無ければ UTF-8 として読む。
        foreach (var line in File.ReadAllLines(path))
        {
            var text = line.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            var separator = text.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            var key = text[..separator].Trim();
            var value = Unquote(text[(separator + 1)..].Trim());

            if (key.Length > 0)
            {
                entries.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        return entries;
    }

    /// <summary>
    /// 書き出す。値は常に二重引用符で囲む（前後の空白や空の値がそのまま往復するため）。
    /// </summary>
    public static void Write(string path, IEnumerable<KeyValuePair<string, string>> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MindMap の設定。行の形は  名前 : \"値\"  で、# から始まる行は覚え書き。");
        builder.AppendLine("# 設定画面（ツールバー右端）で書き換えると、この覚え書きより下がすべて書き直される。");
        builder.AppendLine();

        foreach (var (key, value) in entries)
        {
            builder.Append(key).Append(" : \"").Append(value).AppendLine("\"");
        }

        // BOM 無しの UTF-8 で書く。BOM を付けると、他のプログラムが名前の頭に
        // 見えない文字を読んでしまう。
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
}
