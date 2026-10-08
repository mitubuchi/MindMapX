using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MindMap.ViewModels;

namespace MindMap.Views;

public partial class ViewerPane : UserControl
{
    public ViewerPane()
    {
        InitializeComponent();

        // 上りの KeyDown で止める。入力欄が自分で処理したキーはすでに Handled なので届かない。
        AddHandler(KeyDownEvent, ViewerPane_KeyDown, RoutingStrategies.Bubble);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// ウィンドウに登録したキー操作（Delete でノード削除、Tab で子ノード追加など）は、
    /// ビューアに入っている間に働くと事故になる。入力欄が処理し終えたあとの
    /// 上りの KeyDown で止め、ウィンドウまで届かないようにする。
    /// 入力欄が自分で処理したキー（文字の削除や Ctrl+Z など）はすでに Handled に
    /// なっていてここには来ないので、素通りしたものだけを捨てればよい。
    /// </summary>
    private void ViewerPane_KeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var swallow = e.Key switch
        {
            Key.Delete or Key.Insert or Key.Tab or Key.Enter or Key.F2 => true,
            // 入力欄の取り消しが尽きたあと、マップ側の取り消しに化けないようにする。
            Key.X or Key.C or Key.V or Key.A or Key.Z or Key.Y => control,
            _ => false,
        };

        if (swallow)
        {
            e.Handled = true;
        }
    }

    // 本文欄に入ってから抜けるまでを 1 回の Undo にまとめる。
    // 判断は ViewModel 側にあるので、ここでは出入りを伝えるだけにする。
    private void BodyEditor_GotFocus(object? sender, GotFocusEventArgs e) =>
        (DataContext as ViewerViewModel)?.BeginEdit();

    private void BodyEditor_LostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as ViewerViewModel)?.EndEdit();

    /// <summary>ノードのリンクアイコンを押したときと同じ処理（関連付けられたアプリで開く）。</summary>
    private void OpenLink_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewerViewModel { Node: { } node }
            && TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel window)
        {
            window.OpenLinkCommand.Execute(node).Subscribe();
        }
    }
}
