using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

namespace MindMap.Views;

/// <summary>
/// 確認・お知らせ用の小さなダイアログ。Avalonia には OS 標準のメッセージボックスが無いので、
/// 同じ役割のものを自前で持つ（外部の部品を増やさずに済み、どの OS でも同じ見た目になる）。
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// メッセージとボタンを出し、押されたボタンの番号を返す。
    /// 閉じるボタンなどで取り消されたときは -1。
    /// </summary>
    public static async Task<int> ShowAsync(Window owner, string message, params string[] buttons)
    {
        var dialog = new MessageDialog();
        var text = dialog.FindControl<TextBlock>("MessageText")!;
        var panel = dialog.FindControl<StackPanel>("Buttons")!;

        text.Text = message;

        var result = -1;

        for (var index = 0; index < buttons.Length; index++)
        {
            var answer = index;
            var button = new Button
            {
                Content = buttons[index],
                MinWidth = 88,
                HorizontalContentAlignment = HorizontalAlignment.Center,

                // 先頭のボタンを既定にする（Enter でそのまま選べる）。
                IsDefault = index == 0,
                IsCancel = index == buttons.Length - 1,
            };

            button.Click += (_, _) =>
            {
                result = answer;
                dialog.Close();
            };

            panel.Children.Add(button);
        }

        await dialog.ShowDialog(owner);
        return result;
    }
}
