using System.Reactive;
using System.Reactive.Linq;
using MindMap.Services.Settings;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// 設定ウィンドウ。<see cref="AppSettings.Definitions"/> を見て行を組み立てるので、
/// 設定項目を増やしてもこのクラスと画面は触らなくてよい。
///
/// 編集するのは現在の設定の控えで、OK を押すまで本物には触らない
/// （キャンセルで元に戻す処理を書かずに済む）。
/// </summary>
public sealed class SettingsViewModel : ReactiveObject
{
    private readonly AppSettings _draft;

    public SettingsViewModel(AppSettings current)
    {
        _draft = current.Clone();

        Items = AppSettings.Definitions
            .Select(definition => new SettingItemViewModel(definition, _draft[definition.Key]))
            .ToList();

        BrowseCommand = ReactiveCommand.CreateFromTask<SettingItemViewModel>(BrowseAsync);
        OkCommand = ReactiveCommand.CreateFromTask(SaveAsync);

        BrowseCommand.ThrownExceptions
            .SelectMany(ex => ShowError.Handle(ex.Message))
            .Subscribe();
    }

    public IReadOnlyList<SettingItemViewModel> Items { get; }

    /// <summary>設定の実物の場所。どこに書かれるのかを画面に出しておく。</summary>
    public string FilePath => SettingsService.FilePath;

    /// <summary>参照ボタン。出すダイアログの種類は項目の <see cref="SettingKind"/> で決まる。</summary>
    public Interaction<SettingItemViewModel, string?> ShowBrowseDialog { get; } = new();

    public Interaction<string, Unit> ShowError { get; } = new();

    public ReactiveCommand<SettingItemViewModel, Unit> BrowseCommand { get; }

    /// <summary>保存できたら true。失敗したときはウィンドウを閉じない（入力を捨てないため）。</summary>
    public ReactiveCommand<Unit, bool> OkCommand { get; }

    private async Task BrowseAsync(SettingItemViewModel item)
    {
        if (await ShowBrowseDialog.Handle(item) is { Length: > 0 } picked)
        {
            item.Value = picked;
        }
    }

    private async Task<bool> SaveAsync()
    {
        foreach (var item in Items)
        {
            _draft[item.Key] = item.Value.Trim();
        }

        try
        {
            SettingsService.Save(_draft);
            return true;
        }
        catch (Exception ex)
        {
            await ShowError.Handle($"設定を保存できませんでした。{Environment.NewLine}{SettingsService.FilePath}{Environment.NewLine}{ex.Message}");
            return false;
        }
    }
}
