using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MindMapX.Abstractions.Viewers;

namespace MindMap.Services.Viewers;

/// <summary>
/// 文字で中身を出す組み込みビューアの土台。何を出すかは派生が決める。
///
/// 読めなかった理由もこの枠の中に出す。ホスト側に「ビューアが失敗したとき」の
/// 分岐を作らずに済ませるため（<see cref="IContentViewer"/> の約束）。
/// </summary>
public abstract class TextContentViewer : IContentViewer
{
    private readonly Panel _root;
    private readonly TextBox _text;
    private readonly TextBlock _notice;

    protected TextContentViewer()
    {
        // 読み取り専用だが、選んでコピーはできるようにしておく。
        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x3C, 0x46, 0x52)),
            FontFamily = new FontFamily("Consolas, Yu Gothic UI, Meiryo, Menlo, Hiragino Sans, monospace"),
            FontSize = 12,
            Padding = new Thickness(0),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_text, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, ScrollBarVisibility.Disabled);

        // Fluent のテーマは、フォーカスやマウスが乗ったときに入力欄の背景と枠を描き直す。
        // 読むだけの欄なので、どの状態でも素の見た目のままにする。
        _text.Styles.Add(new Style(x => x.OfType<TextBox>().Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
            },
        });

        _notice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA0)),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };

        _root = new Panel();
        _root.Children.Add(_text);
        _root.Children.Add(_notice);
    }

    public Control View => _root;

    public async Task LoadAsync(ViewerContent content, CancellationToken cancellationToken)
    {
        // 読み込みの続きが UI スレッドに戻るかどうかは、呼ばれ方（同期コンテキストの有無）で
        // 変わる。画面を触るところだけは、戻り先を当てにせず明示的に渡す。
        var document = await BuildAsync(content, cancellationToken).ConfigureAwait(false);

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(document);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Apply(document));
    }

    private void Apply(TextDocument document)
    {
        if (document.Message is { } message)
        {
            _text.IsVisible = false;
            _notice.IsVisible = true;
            _notice.Text = message;
            return;
        }

        _notice.IsVisible = false;
        _text.IsVisible = true;
        _text.Text = document.Text ?? string.Empty;

        // 前のファイルを読んだ位置が残らないよう、先頭に戻す。
        _text.CaretIndex = 0;
    }

    /// <summary>中身を組み立てる。出せないときは理由を返す（例外は投げない）。</summary>
    protected abstract Task<TextDocument> BuildAsync(ViewerContent content, CancellationToken cancellationToken);

    public virtual void Dispose()
    {
        // 文字を出しているだけなので、手放すものはない。
    }
}
