using MindMap.Services.Settings;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// 設定画面の 1 行。決まりごと（<see cref="SettingDefinition"/>）と、いま入っている値を持つ。
///
/// 値は文字列 1 本で持ち、入切の項目だけ <see cref="Flag"/> から読み書きする。
/// config.txt が文字列の集まりなので、画面側でも同じ形にしておくと、
/// 項目を増やしたときに変換の置き場所を探さずに済む。
/// </summary>
public sealed class SettingItemViewModel : ReactiveObject
{
    private string _value;

    public SettingItemViewModel(SettingDefinition definition, string value)
    {
        Definition = definition;
        _value = value;
    }

    public SettingDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    public string Description => Definition.Description;

    public SettingKind Kind => Definition.Kind;

    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    /// <summary>入切の項目をチェックボックスに繋ぐための見え方。</summary>
    public bool Flag
    {
        get => AppSettings.IsTrue(_value);
        set
        {
            if (Flag == value)
            {
                return;
            }

            Value = value ? "true" : "false";
            this.RaisePropertyChanged();
        }
    }
}
