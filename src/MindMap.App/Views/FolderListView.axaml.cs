using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MindMap.Services;
using MindMap.Services.Viewers;

namespace MindMap.Views;

public partial class FolderListView : UserControl
{
    public FolderListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>行をダブルクリックしたら、関連付けられたアプリ（フォルダーならファイラー）で開く。</summary>
    private void Row_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FolderEntry entry })
        {
            _ = OpenAsync(entry);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Enter でも開く。ここで受け止めておかないと、ウィンドウに登録した
    /// 「兄弟ノードを追加」に届いてしまう（ビューア側で止めてはいるが、
    /// 選んでいる行があるならそれを開くのが自然）。
    /// </summary>
    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ListBox { SelectedItem: FolderEntry entry })
        {
            return;
        }

        _ = OpenAsync(entry);
        e.Handled = true;
    }

    private async Task OpenAsync(FolderEntry entry)
    {
        if (LinkLauncher.Open(entry.Link) is { } error && TopLevel.GetTopLevel(this) is Window owner)
        {
            await MessageDialog.ShowAsync(owner, $"開けませんでした。\n\n{entry.Link}\n\n{error}", "OK");
        }
    }
}
