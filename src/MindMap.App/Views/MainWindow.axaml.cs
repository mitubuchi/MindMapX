using System.Reactive;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MindMap.Services;
using MindMap.ViewModels;

namespace MindMap.Views;

public partial class MainWindow : Window
{
    /// <summary>ドラッグ中のノードと、掴んだ時点の位置。複数選択なら選択ぶんすべてが入る。</summary>
    private List<(NodeViewModel Node, double X, double Y)> _dragOrigins = new();

    private Visual? _draggingCanvas;
    private Point _dragStartPointerPosition;

    /// <summary>余白のドラッグによる範囲選択。</summary>
    private DocumentViewModel? _bandDocument;

    private Control? _bandCanvas;
    private Rectangle? _bandRectangle;
    private Point _bandStart;
    private bool _bandAdditive;

    private bool _isPanning;
    private Point _panStartPointerPosition;
    private Vector _panStartOffset;

    /// <summary>ビューアの幅をドラッグで変えている最中か。</summary>
    private bool _resizingViewer;

    private double _resizeStartWidth;
    private Point _resizeStartPointerPosition;

    /// <summary>スクロール位置を復元している間は、その動きを記録し返さないようにする。</summary>
    private bool _restoringScroll;

    /// <summary>設定ウィンドウを開いている最中か。続けて押されても 1 枚しか出さない。</summary>
    private bool _settingsPending;

    /// <summary>未保存確認のために閉じるのを一度キャンセルするので、二周目を見分けるフラグ。</summary>
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        // ファイラーやブラウザーからのドロップ。ノードの上ならリンクの差し替え、
        // 余白ならその場所に新しいノードを作る（どちらに落ちたかは落ちた先から辿って決める）。
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>今ユーザーが見ているタブ。ノードの操作はすべてこのタブに属する。</summary>
    private DocumentViewModel? ActiveDocument => ViewModel?.ActiveDocument;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel is null)
        {
            base.OnClosing(e);
            return;
        }

        // 確認ダイアログを挟むためにいったん閉じるのを取り消し、答えが出てから閉じ直す。
        e.Cancel = true;
        base.OnClosing(e);
        _ = ConfirmAndCloseAsync();
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (ViewModel is not null && await ViewModel.CanCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>ViewModel からのダイアログ要求を、実際の画面につなぐ。</summary>
    public void RegisterInteractionHandlers(MainWindowViewModel viewModel)
    {
        viewModel.ShowOpenFileDialog.RegisterHandler(async context =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "マインドマップを開く",
                AllowMultiple = false,
                FileTypeFilter = [MindMapFileType, FilePickerFileTypes.All],
            });

            context.SetOutput(files.Count > 0 ? files[0].TryGetLocalPath() : null);
        });

        viewModel.ShowSaveFileDialog.RegisterHandler(async context =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "名前を付けて保存",
                SuggestedFileName = context.Input,
                DefaultExtension = MindMapFileService.FileExtension.TrimStart('.'),
                FileTypeChoices = [MindMapFileType],
            });

            context.SetOutput(file?.TryGetLocalPath());
        });

        viewModel.ShowLinkFileDialog.RegisterHandler(async context =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "リンク先のファイルを選択",
                AllowMultiple = false,
            });

            context.SetOutput(files.Count > 0 ? files[0].TryGetLocalPath() : null);
        });

        viewModel.ConfirmSaveChanges.RegisterHandler(async context =>
        {
            var answer = await MessageDialog.ShowAsync(
                this,
                $"「{context.Input}」の変更が保存されていません。\n保存しますか？",
                "保存する",
                "保存しない",
                "キャンセル");

            context.SetOutput(answer switch
            {
                0 => SaveChangesResult.Save,
                1 => SaveChangesResult.Discard,
                _ => SaveChangesResult.Cancel,
            });
        });

        viewModel.ShowError.RegisterHandler(async context =>
        {
            await MessageDialog.ShowAsync(this, context.Input, "OK");
            context.SetOutput(Unit.Default);
        });

        viewModel.ShowSettings.RegisterHandler(async context =>
        {
            // 設定ウィンドウを出している間も呼び出し元は待たせない（WPF 版と同じく、閉じるのを待たない）。
            context.SetOutput(Unit.Default);

            if (_settingsPending)
            {
                return;
            }

            _settingsPending = true;
            try
            {
                await new SettingsWindow().ShowDialog<bool>(this);
            }
            finally
            {
                _settingsPending = false;
            }
        });

        viewModel.MeasureNodes.RegisterHandler(context =>
        {
            // ノードの幅と高さは、描いてみて初めて決まる（Node_SizeChanged が返す）。
            // 本文を隠した直後はまだ古い値なので、ここで一度レイアウトを回して
            // SizeChanged を出させ、新しい大きさを ViewModel に届ける。
            UpdateLayout();
            context.SetOutput(Unit.Default);
        });

        viewModel.OpenExternal.RegisterHandler(async context =>
        {
            if (LinkLauncher.Open(context.Input) is { } error)
            {
                await MessageDialog.ShowAsync(
                    this,
                    $"リンクを開けませんでした。\n\n{context.Input}\n\n{error}",
                    "OK");
            }

            context.SetOutput(Unit.Default);
        });
    }

    private static FilePickerFileType MindMapFileType => new("マインドマップ")
    {
        Patterns = [$"*{MindMapFileService.FileExtension}"],
    };

    // ------------------------------------------------------------ ノードの操作

    /// <summary>
    /// ノードの実寸を ViewModel に返す。接続線の端点とドラッグの移動範囲が、
    /// 中身の量で変わる実際の大きさを知る必要があるため。
    /// </summary>
    private void Node_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control { DataContext: NodeViewModel node })
        {
            node.Width = e.NewSize.Width;
            node.Height = e.NewSize.Height;
        }
    }

    private void Node_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: NodeViewModel node } element
            || ActiveDocument is not { } document)
        {
            return;
        }

        // 右クリックではそのノードを選んでおく（コンテキストメニューの対象を確定させる）。
        // すでに複数選択に入っているノードなら、選択を崩さずそのまま対象にする。
        // メニューを開くのは Avalonia に任せるので、ここでは押されたことを止めない。
        if (e.GetCurrentPoint(element).Properties.IsRightButtonPressed)
        {
            if (!document.SelectedNodes.Contains(node))
            {
                document.SelectedNode = node;
            }

            return;
        }

        if (!e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;

        // Ctrl / Shift 付きのクリックは選択の足し引きだけ。移動も編集も始めない。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            document.ToggleSelection(node);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            document.AddToSelection(node);
            return;
        }

        if (e.ClickCount >= 2)
        {
            document.SelectedNode = node;
            node.IsEditing = true;
            return;
        }

        // 複数選択のうちの 1 つを掴んだときは選択を崩さない（まとめて動かすため）。
        if (!document.SelectedNodes.Contains(node))
        {
            document.SelectedNode = node;
        }

        // 編集中のノードはドラッグせず、クリックはカーソル移動に任せる。
        if (node.IsEditing)
        {
            return;
        }

        // キャンバスはタブの中身のテンプレートにあるので、名前ではなく visual tree から辿る。
        if (FindNamedAncestor(element, "CanvasRoot") is not { } canvas)
        {
            return;
        }

        // 親を動かすと子も付いてくるので、親と子を両方選んだままドラッグしても
        // 動かすのは最上位だけにする（両方動かすと子に移動量が二重に掛かる）。
        var moving = document.SelectedNodes.Contains(node)
            ? document.SelectionRoots()
            : [node];

        _dragOrigins = moving.Select(n => (Node: n, n.X, n.Y)).ToList();
        _draggingCanvas = canvas;
        _dragStartPointerPosition = e.GetPosition(canvas);
        e.Pointer.Capture(element);
    }

    private void Node_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragOrigins.Count == 0
            || _draggingCanvas is not Control canvas
            || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var current = e.GetPosition(canvas);
        var deltaX = current.X - _dragStartPointerPosition.X;
        var deltaY = current.Y - _dragStartPointerPosition.Y;

        // キャンバスの外にノードが出て行方不明にならないよう内側に留める。
        // 複数を動かすときは、位置関係が崩れないよう移動量そのものを制限する。
        var minX = _dragOrigins.Max(o => -o.X);
        var maxX = _dragOrigins.Min(o => Math.Max(0, canvas.Width - o.Node.WorldWidth) - o.X);
        var minY = _dragOrigins.Max(o => -o.Y);
        var maxY = _dragOrigins.Min(o => Math.Max(0, canvas.Height - o.Node.WorldHeight) - o.Y);

        deltaX = Math.Clamp(deltaX, minX, Math.Max(minX, maxX));
        deltaY = Math.Clamp(deltaY, minY, Math.Max(minY, maxY));

        foreach (var (node, originX, originY) in _dragOrigins)
        {
            node.X = originX + deltaX;
            node.Y = originY + deltaY;
        }
    }

    private void Node_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);

        if (_dragOrigins.Count > 0 && ActiveDocument is { } document)
        {
            // ドラッグ全体を 1 回の Undo にまとめる（動いていなければ何も積まれない）。
            document.CompleteNodeDrag(_dragOrigins);

            // 複数選択のノードを動かさずに離したら、そのノードだけの選択に絞る。
            // 押した時点で絞ってしまうと、まとめて動かすドラッグが始められない。
            var moved = _dragOrigins.Any(o => o.Node.X != o.X || o.Node.Y != o.Y);
            if (!moved && _dragOrigins.Count > 1 && sender is Control { DataContext: NodeViewModel node })
            {
                document.SelectedNode = node;
            }
        }

        _dragOrigins = new();
        _draggingCanvas = null;
    }

    private void NodeLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NodeViewModel node })
        {
            ViewModel?.OpenLinkCommand.Execute(node).Subscribe();
        }
    }

    private void NodeToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NodeViewModel node })
        {
            ActiveDocument?.ToggleCollapseCommand.Execute(node).Subscribe();
        }
    }

    private void NodeChildren_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NodeViewModel node })
        {
            ActiveDocument?.ToggleChildrenCommand.Execute(node).Subscribe();
        }
    }

    // ------------------------------------------------------------ ドラッグ&ドロップ

    /// <summary>落とせるものかを見て、カーソルの形を決める。</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // ドロップの中身を形式名で引けるのは IDataObject だけ
        var data = e.Data;
#pragma warning restore CS0618
        var accept = DropTargetNode(e) is not null
            ? DroppedLink.Extract(data) is not null
            : DropTargetCanvas(e) is not null
              && (DroppedLink.ExtractFiles(data).Count > 0 || DroppedLink.ExtractUrls(data).Count > 0);

        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// ノードの上なら、ブラウザーの URL やファイルをそのノードのリンクにする。
    /// 余白なら、落とされたファイルや URL をその場所にノードとして作る。
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

#pragma warning disable CS0618
        var data = e.Data;
#pragma warning restore CS0618

        if (DropTargetNode(e) is { } node)
        {
            if (DroppedLink.Extract(data) is { } link)
            {
                document.SetLink(node, link);
                document.SelectedNode = node;
            }

            e.Handled = true;
            return;
        }

        if (DropTargetCanvas(e) is not { } canvas)
        {
            return;
        }

        // ファイルが来ているときは ExtractUrls が空を返すので、取り違えは起きない。
        var files = DroppedLink.ExtractFiles(data);
        var urls = DroppedLink.ExtractUrls(data);

        if (files.Count == 0 && urls.Count == 0)
        {
            return;
        }

        // 落とした位置に置く。キャンバスの外にはみ出して行方不明にならないよう内側に留める。
        var point = e.GetPosition(canvas);
        var x = Math.Clamp(point.X, 0, Math.Max(0, canvas.Width - NodeViewModel.DefaultWidth));
        var y = Math.Clamp(point.Y, 0, Math.Max(0, canvas.Height - NodeViewModel.DefaultHeight));

        if (files.Count > 0)
        {
            document.AddFileNodes(files, x, y);
        }
        else
        {
            document.AddLinkNodes(urls, x, y);
        }

        e.Handled = true;
    }

    /// <summary>落とされた先のノード。ノードの枠（Classes="node"）の中でなければ null。</summary>
    private static NodeViewModel? DropTargetNode(DragEventArgs e)
    {
        for (var current = e.Source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Border { DataContext: NodeViewModel node } border && border.Classes.Contains("node"))
            {
                return node;
            }

            if (current is Control { Name: "CanvasRoot" })
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>落とされた先のキャンバス。タブの中身の外（ツールバーやビューア欄）なら null。</summary>
    private static Control? DropTargetCanvas(DragEventArgs e) =>
        e.Source is Visual source ? FindNamedAncestor(source, "CanvasRoot") : null;

    // ------------------------------------------------------------ 範囲選択

    /// <summary>余白を押したところから、囲んだノードをまとめて選ぶドラッグを始める。</summary>
    private void CanvasRoot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DocumentViewModel document } canvas
            || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Ctrl / Shift を押していれば今の選択に足す。押していなければ、余白のクリックで選択解除。
        _bandAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!_bandAdditive)
        {
            document.ClearSelection();
        }

        _bandDocument = document;
        _bandCanvas = canvas;
        _bandStart = e.GetPosition(canvas);
        _bandRectangle = canvas.GetVisualDescendants().OfType<Rectangle>().FirstOrDefault(r => r.Name == "SelectionBox");

        e.Pointer.Capture(canvas);
        FocusManager?.ClearFocus();
    }

    private void CanvasRoot_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_bandCanvas is not { } canvas
            || _bandRectangle is not { } box
            || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var band = BandRect(e.GetPosition(canvas));

        Canvas.SetLeft(box, band.X);
        Canvas.SetTop(box, band.Y);
        box.Width = band.Width;
        box.Height = band.Height;
        box.IsVisible = true;
    }

    private void CanvasRoot_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_bandCanvas is not { } canvas)
        {
            return;
        }

        e.Pointer.Capture(null);

        if (_bandRectangle is { } box)
        {
            box.IsVisible = false;
        }

        // 枠を出さないまま離した（ただのクリック）ときは選択を変えない。
        var band = BandRect(e.GetPosition(canvas));
        if (_bandDocument is { } document && (band.Width >= 3 || band.Height >= 3))
        {
            var hits = document.Nodes
                .Where(n => n.IsVisible)
                .Where(n => band.Intersects(new Rect(n.X, n.Y, n.WorldWidth, n.WorldHeight)))
                .ToList();

            document.SelectNodes(hits, _bandAdditive);
        }

        _bandDocument = null;
        _bandCanvas = null;
        _bandRectangle = null;
    }

    private Rect BandRect(Point current) => new(
        Math.Min(_bandStart.X, current.X),
        Math.Min(_bandStart.Y, current.Y),
        Math.Abs(current.X - _bandStart.X),
        Math.Abs(current.Y - _bandStart.Y));

    // ------------------------------------------------------------ コンテキストメニュー

    private void ContextCut_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ActiveDocument?.CutCommand.Execute().Subscribe();

    private void ContextCopy_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ActiveDocument?.CopyCommand.Execute().Subscribe();

    private void ContextPaste_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ActiveDocument?.PasteCommand.Execute().Subscribe();

    private async void ContextExtractChildren_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node && ActiveDocument is { } document)
        {
            await document.ExtractChildrenToFileAsync(node);
        }
    }

    private void ContextOpenLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node)
        {
            ViewModel?.OpenLinkCommand.Execute(node).Subscribe();
        }
    }

    private async void ContextSetFileLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node && ActiveDocument is { } document)
        {
            await document.SetFileLinkAsync(node);
        }
    }

    private void ContextRemoveLink_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node && ActiveDocument is { } document)
        {
            document.SetLink(node, string.Empty);
        }
    }

    /// <summary>コンテキストメニューの項目から、対象のノードを取り出す。</summary>
    private static NodeViewModel? NodeOf(object? sender) => (sender as Control)?.DataContext as NodeViewModel;

    // ------------------------------------------------------------ ノードの編集

    /// <summary>
    /// 編集が始まってタイトル欄が現れたら、そこへフォーカスを移す。
    /// 欄自体は隠れているだけで最初から存在するので、表示状態の変化を見張る。
    /// </summary>
    private void TitleBox_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        box.GetObservable(IsVisibleProperty).Subscribe(visible =>
        {
            if (!visible)
            {
                return;
            }

            // レイアウトが済む前は Focus() が効かないので一拍置く。
            Dispatcher.UIThread.Post(
                () =>
                {
                    box.Focus();
                    box.SelectAll();
                },
                DispatcherPriority.Input);
        });
    }

    /// <summary>タイトル欄と内容欄で共通のキー操作。振る舞いの違いは AcceptsReturn から導く。</summary>
    private void EditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: NodeViewModel node } textBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ActiveDocument?.CancelTextEdit();
                FocusManager?.ClearFocus();
                e.Handled = true;
                break;

            // Ctrl+Enter はどちらの欄からでも編集を確定する。
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                node.IsEditing = false;
                FocusManager?.ClearFocus();
                e.Handled = true;
                break;

            // タイトルは 1 行だけなので、Enter は改行ではなく内容欄への移動にあてる。
            // 内容欄（AcceptsReturn=True）ではそのまま改行として通す。
            case Key.Enter when !textBox.AcceptsReturn:
                KeyboardNavigationHandler.GetNext(textBox, NavigationDirection.Next)?.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 編集の終了判定。タイトル欄と内容欄の行き来ではフォーカスが箱の中に留まるので、
    /// 個々の欄ではなくパネル全体から出たときだけ確定する。
    /// </summary>
    private void EditPanel_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NodeViewModel node } panel)
        {
            return;
        }

        // 次にどこへ移るかはこの時点ではまだ決まっていないので、一拍置いてから確かめる。
        Dispatcher.UIThread.Post(
            () =>
            {
                var focused = FocusManager?.GetFocusedElement() as Visual;
                if (focused is not null && panel.IsVisualAncestorOf(focused))
                {
                    return;
                }

                // 編集欄の右クリックメニューを開くと、フォーカスはメニューへ移る。
                // ここで確定すると欄が消えて、メニューの切り取りや貼り付けが届かなくなる。
                // 編集は続けたままにして、メニューを閉じたら欄にフォーカスを戻す。
                if (focused is not null && EditMenuTarget(panel, focused) is { } box)
                {
                    ReturnFocusWhenMenuCloses(box, node);
                    return;
                }

                node.IsEditing = false;
            },
            DispatcherPriority.Input);
    }

    /// <summary>
    /// フォーカスのある要素が、このパネル内の欄から開いた右クリックメニューの中にあれば、その欄。
    /// メニューの描かれ方は環境で違う（デスクトップでは別の窓、そうでなければウィンドウの
    /// 重ね描き）ので、visual tree では辿れない。論理ツリーなら、どちらでもメニューの上に
    /// 「どの部品に付いて開いたか」を持つ Popup がいる。
    /// </summary>
    private static TextBox? EditMenuTarget(Control panel, Visual focused) =>
        focused is ILogical logical
        && logical.GetLogicalAncestors().OfType<Popup>().FirstOrDefault() is { PlacementTarget: TextBox box }
        && panel.IsVisualAncestorOf(box)
            ? box
            : null;

    /// <summary>右クリックメニューが閉じたら、編集が続いている限り元の欄にフォーカスを戻す。</summary>
    private static void ReturnFocusWhenMenuCloses(TextBox box, NodeViewModel node)
    {
        if (box.ContextFlyout is not { } flyout)
        {
            return;
        }

        void OnClosed(object? sender, EventArgs e)
        {
            flyout.Closed -= OnClosed;
            if (node.IsEditing)
            {
                box.Focus();
            }
        }

        flyout.Closed += OnClosed;
    }

    // ------------------------------------------------------------ スクロールとズーム

    /// <summary>タブを切り替えたとき、そのドキュメントの表示位置に戻す。</summary>
    private void Scroller_DataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not ScrollViewer { DataContext: DocumentViewModel document } scroller)
        {
            return;
        }

        _restoringScroll = true;

        // 新しい中身のレイアウトが終わるまではスクロールできない。
        Dispatcher.UIThread.Post(
            () =>
            {
                scroller.Offset = new Vector(document.ScrollOffsetX, document.ScrollOffsetY);
                _restoringScroll = false;
            },
            DispatcherPriority.Loaded);
    }

    private void Scroller_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_restoringScroll)
        {
            return;
        }

        if (sender is ScrollViewer { DataContext: DocumentViewModel document } scroller)
        {
            document.ScrollOffsetX = scroller.Offset.X;
            document.ScrollOffsetY = scroller.Offset.Y;
        }
    }

    /// <summary>Ctrl + ホイールで拡大縮小。カーソルの下にある位置が動かないようスクロール位置も補正する。</summary>
    private void Scroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer { DataContext: DocumentViewModel document } scroller
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || FindNamedDescendant(scroller, "CanvasRoot") is not { } canvas)
        {
            return;
        }

        var canvasPoint = e.GetPosition(canvas);
        var viewportPoint = e.GetPosition(scroller);

        document.Zoom *= e.Delta.Y > 0 ? 1.1 : 1 / 1.1;

        // 新しい拡大率でのレイアウトが確定してからでないとスクロール位置を計算できない。
        Dispatcher.UIThread.Post(
            () => scroller.Offset = new Vector(
                canvasPoint.X * document.Zoom - viewportPoint.X,
                canvasPoint.Y * document.Zoom - viewportPoint.Y),
            DispatcherPriority.Loaded);

        e.Handled = true;
    }

    private void Scroller_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer scroller
            || !e.GetCurrentPoint(scroller).Properties.IsMiddleButtonPressed)
        {
            return;
        }

        _isPanning = true;
        _panStartPointerPosition = e.GetPosition(scroller);
        _panStartOffset = scroller.Offset;

        e.Pointer.Capture(scroller);
        scroller.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void Scroller_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || sender is not ScrollViewer scroller)
        {
            return;
        }

        var current = e.GetPosition(scroller);
        scroller.Offset = new Vector(
            _panStartOffset.X - (current.X - _panStartPointerPosition.X),
            _panStartOffset.Y - (current.Y - _panStartPointerPosition.Y));
    }

    private void Scroller_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning || sender is not ScrollViewer scroller)
        {
            return;
        }

        _isPanning = false;
        e.Pointer.Capture(null);
        scroller.Cursor = Cursor.Default;
        e.Handled = true;
    }

    // ------------------------------------------------------------ ビューアの幅

    /// <summary>
    /// ビューアとキャンバスの間の帯をドラッグして幅を変える。
    /// GridSplitter を使わないのは、DockPanel のままで済ませるため。
    /// 掴んだ時点を基準に測るので、上限に当たっても位置がずれない。
    /// </summary>
    private void ViewerSplitter_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is null || sender is not Control splitter
            || !e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _resizingViewer = true;
        _resizeStartWidth = ViewModel.Viewer.Width;
        _resizeStartPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(splitter);
        e.Handled = true;
    }

    private void ViewerSplitter_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizingViewer || ViewModel is null)
        {
            return;
        }

        // 左へ動かすほど広がる。行き過ぎてキャンバスが潰れないよう、幅は ViewModel 側で丸める。
        var delta = _resizeStartPointerPosition.X - e.GetPosition(this).X;
        ViewModel.Viewer.Resize(_resizeStartWidth + delta, Bounds.Width);
    }

    private void ViewerSplitter_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizingViewer)
        {
            return;
        }

        _resizingViewer = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ------------------------------------------------------------ visual tree の探索

    /// <summary>
    /// テンプレートの中にある要素を名前で探す。DataTemplate 内の名前は
    /// ウィンドウのフィールドにならないので、visual tree を下って見つける。
    /// </summary>
    private static Control? FindNamedDescendant(Visual start, string name) =>
        start.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

    private static Control? FindNamedAncestor(Visual start, string name)
    {
        for (var current = start; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && control.Name == name)
            {
                return control;
            }
        }

        return null;
    }
}
