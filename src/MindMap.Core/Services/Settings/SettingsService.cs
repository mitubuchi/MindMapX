using System.IO;

namespace MindMap.Services.Settings;

/// <summary>
/// いま効いている設定。実行ファイルと同じフォルダーの config.txt に置く
/// （持ち運べる形にしておきたいので、ユーザーのプロファイルには書かない）。
///
/// 静的に持つのは、リンクの解き方（<see cref="LinkPathResolver"/>）のように
/// 画面から遠いところからも参照するため。書き換えは設定画面からの 1 か所だけで、
/// 値は <see cref="Save"/> の瞬間に丸ごと差し替わる。
/// </summary>
public static class SettingsService
{
    public const string FileName = "config.txt";

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    public static AppSettings Current { get; private set; } = new();

    /// <summary>
    /// 起動時に必ず 1 度呼ぶ。ファイルが無ければ既定値で作る
    /// （何が設定できるのかを、ファイルを見るだけで分かるようにするため）。
    ///
    /// 読めなかったときは既定値のまま黙って続ける。設定はあくまで補助で、
    /// 読めないからといってアプリが立ち上がらないのは困るため。
    /// </summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = AppSettings.FromEntries(SettingsFile.Read(FilePath));
                return;
            }

            Current = new AppSettings();
            SettingsFile.Write(FilePath, Current.ToEntries());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 書き込めない場所に置かれている（Program Files 直下など）ことがある。
            // その場合も既定値で動かし、設定画面から保存しようとしたときに初めて知らせる。
            Current = new AppSettings();
        }
    }

    /// <summary>保存して、以後はこの内容を使う。書けなければ例外がそのまま上がる。</summary>
    public static void Save(AppSettings settings)
    {
        SettingsFile.Write(FilePath, settings.ToEntries());
        Current = settings;
    }
}
