using System.Reactive;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MindMap.Services.Settings;
using MindMap.ViewModels;

namespace MindMap.Views;

/// <summary>
/// 設定ウィンドウ。中身は <see cref="SettingsViewModel"/> が
/// <see cref="AppSettings.Definitions"/> から組み立てるので、
/// 設定項目が増えてもここは変わらない。
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var viewModel = new SettingsViewModel(SettingsService.Current);
        DataContext = viewModel;

        viewModel.ShowBrowseDialog.RegisterHandler(async context =>
            context.SetOutput(await BrowseAsync(context.Input)));

        viewModel.ShowError.RegisterHandler(async context =>
        {
            await MessageDialog.ShowAsync(this, context.Input, "OK");
            context.SetOutput(Unit.Default);
        });

        // 保存できたときだけ閉じる。書き込めなかったときは入力を残して開いたままにする。
        viewModel.OkCommand.Subscribe(saved =>
        {
            if (saved)
            {
                Close(true);
            }
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>参照ボタン。いま入っている値を、選択ダイアログの開き先にする。</summary>
    private async Task<string?> BrowseAsync(SettingItemViewModel item)
    {
        var current = item.Value.Trim();

        // 消えているフォルダーが書かれていても、ダイアログが出ないと直せない。開き先は諦めるだけ。
        var startFolder = current.Length == 0
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(
                item.Kind == SettingKind.File ? Path.GetDirectoryName(current) ?? current : current);

        if (item.Kind == SettingKind.File)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = item.Label,
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = item.Label,
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
