namespace MindMap.Services.Settings;

/// <summary>
/// 設定 1 つぶんの値の種類。設定画面がどの入力欄を出すかだけを決める
/// （config.txt の中では、どれも文字列として書かれる）。
///
/// 設定を増やすときは、まず <see cref="AppSettings.Definitions"/> に 1 行足す。
/// 種類がここに無いものなら、この列挙とテンプレート（SettingsWindow.xaml）の両方に足す。
/// </summary>
public enum SettingKind
{
    /// <summary>フォルダーのパス。参照ボタンでフォルダー選択を出す。</summary>
    Folder,

    /// <summary>ファイルのパス。参照ボタンでファイル選択を出す。</summary>
    File,

    /// <summary>ただの文字列。</summary>
    Text,

    /// <summary>入切。チェックボックスで見せ、"true" / "false" として書く。</summary>
    Bool,
}
