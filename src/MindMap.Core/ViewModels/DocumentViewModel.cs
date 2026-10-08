using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using MindMap.Models;
using MindMap.Services;
using MindMap.Services.Layout;
using MindMap.Undo;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// タブ 1 枚ぶん、つまりマインドマップ 1 つ分の状態と操作。
/// 保存先・編集履歴・拡大率までドキュメントごとに独立して持つ。
/// </summary>
public sealed class DocumentViewModel : ReactiveObject
{
    private const double HorizontalGap = 72;
    private const double VerticalGap = 24;

    /// <summary>別ファイルへ切り出した部分木を、新しいキャンバスの左上から離しておく余白。</summary>
    private const double ExtractedMargin = 120;

    /// <summary>
    /// 保存された絶対位置と、相対位置から計算し直した位置が「同じ」とみなせる差。
    /// 掛け算と足し算を通るので完全一致にはならない。1 ピクセルよりずっと細かく取る。
    /// </summary>
    private const double PositionTolerance = 0.001;

    public const double MinZoom = 0.3;
    public const double MaxZoom = 3.0;
    private const double ZoomStep = 1.2;

    /// <summary>ノードごとの購読（未保存フラグと編集セッションの監視）。</summary>
    private readonly Dictionary<Guid, IDisposable> _nodeSubscriptions = new();

    private readonly UndoStack _history = new();

    private readonly Interaction<string?, string?> _showSaveFileDialog;
    private readonly Interaction<Unit, string?> _showLinkFileDialog;
    private readonly Interaction<string, SaveChangesResult> _confirmSaveChanges;
    private readonly Interaction<string, Unit> _showError;

    /// <summary>並べる前に、ノードの大きさを測り直してもらう相手。</summary>
    private readonly Interaction<Unit, Unit> _measureNodes;

    /// <summary>リンク先から小さな絵を作る係。ウィンドウで 1 つを共有する。</summary>
    private readonly INodeThumbnailSource _thumbnails;

    /// <summary>クリップボードの読み書き。OS ごとに違うので UI 側から受け取る。</summary>
    private readonly INodeClipboard _clipboard;

    /// <summary>まだ保存していないドキュメントのタブ名。</summary>
    private readonly string _untitledName;

    private readonly ObservableAsPropertyHelper<string> _displayName;

    /// <summary>編集中のノードと、編集を始めた時点の内容。Undo と Escape の取り消しに使う。</summary>
    private (NodeViewModel Node, string Title, string Body, string Link)? _activeEdit;

    /// <summary>ビューアの本文欄で編集している 1 回ぶん。キャンバス上の編集とは別に持つ。</summary>
    private (NodeViewModel Node, string Title, string Body, string Link)? _externalEdit;

    private NodeViewModel? _selectedNode;
    private string? _currentFilePath;
    private bool _isDirty;
    private bool _canUndo;
    private bool _canRedo;
    private double _zoom = 1.0;

    /// <summary>読み込んだファイルにあった、ファイル全体に付く知らない欄。保存時に書き戻す。</summary>
    private Dictionary<string, JsonElement>? _documentExtra;

    public DocumentViewModel(
        string untitledName,
        Interaction<string?, string?> showSaveFileDialog,
        Interaction<Unit, string?> showLinkFileDialog,
        Interaction<string, SaveChangesResult> confirmSaveChanges,
        Interaction<string, Unit> showError,
        Interaction<Unit, Unit> measureNodes,
        INodeThumbnailSource thumbnails,
        INodeClipboard clipboard)
    {
        _untitledName = untitledName;
        _showSaveFileDialog = showSaveFileDialog;
        _showLinkFileDialog = showLinkFileDialog;
        _confirmSaveChanges = confirmSaveChanges;
        _showError = showError;
        _measureNodes = measureNodes;
        _thumbnails = thumbnails;
        _clipboard = clipboard;

        var hasSelection = this.WhenAnyValue(x => x.SelectedNode).Select(node => node is not null);

        // Tab / Enter / Delete はテキスト編集中も押されるので、編集中は無効にして
        // キー入力をテキストボックスに素通しさせる（Ctrl+C なども同じ理由で編集中は譲る）。
        var isEditing = this.WhenAnyValue(
            x => x.SelectedNode,
            x => x.SelectedNode!.IsEditing,
            (node, editing) => node is not null && editing);

        var notEditing = isEditing.Select(editing => !editing);

        var canEditStructure = this.WhenAnyValue(
            x => x.SelectedNode,
            x => x.SelectedNode!.IsEditing,
            (node, editing) => node is not null && !editing);

        // 選択は複数になり得るので、SelectedNode を見ているだけでは
        // 「2 つ目を選び足した／外した」変化を取りこぼす。集合そのものの変化も見る。
        var selectionChanged = Observable
            .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => SelectedNodes.CollectionChanged += h,
                h => SelectedNodes.CollectionChanged -= h)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        var canCopy = selectionChanged
            .CombineLatest(notEditing, (_, ready) => ready && SelectedNodes.Count > 0);

        // ルートを消すとマップが空になってしまうので、そもそも実行できないようにする
        // （押せるボタンを押させてからエラーで断るより、無効にして見せた方が分かりやすい）。
        // 複数選択のときは、ルート以外が 1 つでも入っていれば実行できる。
        var canDelete = selectionChanged
            .CombineLatest(notEditing, (_, ready) => ready && SelectedNodes.Any(n => n.Parent is not null));

        // 並べる相手は「選んだノードの直接の子」なので、子がいなければ押せない。
        var canArrange = this.WhenAnyValue(
            x => x.SelectedNode,
            x => x.SelectedNode!.HasChildren,
            x => x.SelectedNode!.IsEditing,
            (node, hasChildren, editing) => node is not null && hasChildren && !editing);

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        AddChildCommand = ReactiveCommand.Create(AddChild, canEditStructure);
        AddSiblingCommand = ReactiveCommand.Create(AddSibling, canEditStructure);
        DeleteNodeCommand = ReactiveCommand.Create(DeleteSelection, canDelete);
        BeginEditCommand = ReactiveCommand.Create(BeginEditSelectedNode, hasSelection);
        // クリップボードの読み書きは OS によっては待ちが入るので、非同期のコマンドにする。
        CutCommand = ReactiveCommand.CreateFromTask(CutSelectionAsync, canDelete);
        CopyCommand = ReactiveCommand.CreateFromTask(CopySelectionAsync, canCopy);
        PasteCommand = ReactiveCommand.CreateFromTask(PasteAsync, notEditing);
        SelectAllCommand = ReactiveCommand.Create(SelectAll, notEditing);
        ToggleCollapseCommand = ReactiveCommand.Create<NodeViewModel>(ToggleCollapse);
        ToggleChildrenCommand = ReactiveCommand.Create<NodeViewModel>(ToggleChildren);
        ArrangeChildrenVerticalCommand = ReactiveCommand.CreateFromTask(
            () => ArrangeChildrenAsync(LayoutOrientation.Vertical), canArrange);
        ArrangeChildrenHorizontalCommand = ReactiveCommand.CreateFromTask(
            () => ArrangeChildrenAsync(LayoutOrientation.Horizontal), canArrange);
        UndoCommand = ReactiveCommand.Create(_history.Undo, this.WhenAnyValue(x => x.CanUndo));
        RedoCommand = ReactiveCommand.Create(_history.Redo, this.WhenAnyValue(x => x.CanRedo));
        ZoomInCommand = ReactiveCommand.Create(() => Zoom *= ZoomStep);
        ZoomOutCommand = ReactiveCommand.Create(() => Zoom /= ZoomStep);
        ResetZoomCommand = ReactiveCommand.Create(() => Zoom = 1.0);

        // コマンド内で投げられた例外はダイアログで見せる（未処理のままだとアプリが落ちる）。
        Observable
            .Merge(
                SaveCommand.ThrownExceptions,
                SaveAsCommand.ThrownExceptions,
                AddChildCommand.ThrownExceptions,
                AddSiblingCommand.ThrownExceptions,
                DeleteNodeCommand.ThrownExceptions,
                BeginEditCommand.ThrownExceptions,
                CutCommand.ThrownExceptions,
                CopyCommand.ThrownExceptions,
                PasteCommand.ThrownExceptions,
                SelectAllCommand.ThrownExceptions,
                ToggleCollapseCommand.ThrownExceptions,
                ToggleChildrenCommand.ThrownExceptions,
                ArrangeChildrenVerticalCommand.ThrownExceptions,
                ArrangeChildrenHorizontalCommand.ThrownExceptions,
                UndoCommand.ThrownExceptions,
                RedoCommand.ThrownExceptions)
            .SelectMany(ex => _showError.Handle(ex.Message))
            .Subscribe();

        _history.Changed += () =>
        {
            CanUndo = _history.CanUndo;
            CanRedo = _history.CanRedo;
        };

        _displayName = this
            .WhenAnyValue(x => x.CurrentFilePath, x => x.IsDirty)
            .Select(t =>
            {
                var name = t.Item1 is null ? _untitledName : Path.GetFileNameWithoutExtension(t.Item1);
                return $"{name}{(t.Item2 ? " *" : string.Empty)}";
            })
            .ToProperty(this, x => x.DisplayName);

        var root = new NodeViewModel(Guid.NewGuid(), "中心テーマ", string.Empty, 480, 300);
        AddNode(root);
        SelectedNode = root;
        IsDirty = false;
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();

    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    /// <summary>
    /// 選択中のノード全部。複数選択できるので、コマンドの対象は基本こちらを見る。
    /// 追加位置の基準や編集対象のように「1 つに決めたい」場面では <see cref="SelectedNode"/> を使う。
    /// </summary>
    public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new();

    /// <summary>タブに出す名前。未保存なら末尾に * が付く。</summary>
    public string DisplayName => _displayName.Value;

    /// <summary>
    /// 選択の代表となるノード（最後に選んだもの）。設定すると選択はそれ 1 つだけになる。
    /// 選択が空のときは null。
    /// </summary>
    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (value is null)
            {
                ClearSelection();
                return;
            }

            SetSelection(new[] { value }, value);
        }
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (_currentFilePath == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _currentFilePath, value);

            // 相対リンクの基準が変わったので、絵を作り直す。
            // 読み込みの直後（保存先が決まるのはノードを並べたあと）と、
            // 名前を付けて保存で別の場所へ移したときに効く。
            foreach (var node in Nodes)
            {
                RefreshThumbnail(node);
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set => this.RaiseAndSetIfChanged(ref _canUndo, value);
    }

    public bool CanRedo
    {
        get => _canRedo;
        private set => this.RaiseAndSetIfChanged(ref _canRedo, value);
    }

    /// <summary>キャンバスの拡大率。</summary>
    public double Zoom
    {
        get => _zoom;
        set => this.RaiseAndSetIfChanged(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    /// <summary>
    /// スクロール位置。タブは 1 つの表示領域を使い回すので、覚えておかないと
    /// タブを切り替えたときに前のタブの位置がそのまま残ってしまう。
    /// 表示のためだけの値なので通知も保存もしない。
    /// </summary>
    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }

    public ReactiveCommand<Unit, Unit> AddChildCommand { get; }

    public ReactiveCommand<Unit, Unit> AddSiblingCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteNodeCommand { get; }

    public ReactiveCommand<Unit, Unit> BeginEditCommand { get; }

    /// <summary>選択中のノードを（部分木ごと）クリップボードへ移す。</summary>
    public ReactiveCommand<Unit, Unit> CutCommand { get; }

    /// <summary>選択中のノードを（部分木ごと）クリップボードへ複製する。</summary>
    public ReactiveCommand<Unit, Unit> CopyCommand { get; }

    /// <summary>クリップボードのノードを、選択中のノードの子として貼り付ける。</summary>
    public ReactiveCommand<Unit, Unit> PasteCommand { get; }

    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }

    /// <summary>ノードの表示を大小切り替える（引数のノードを対象にする）。</summary>
    public ReactiveCommand<NodeViewModel, Unit> ToggleCollapseCommand { get; }

    public ReactiveCommand<NodeViewModel, Unit> ToggleChildrenCommand { get; }

    /// <summary>選んだノードの直接の子を、縦一列に並べる。</summary>
    public ReactiveCommand<Unit, Unit> ArrangeChildrenVerticalCommand { get; }

    /// <summary>選んだノードの直接の子を、横一列に並べる。</summary>
    public ReactiveCommand<Unit, Unit> ArrangeChildrenHorizontalCommand { get; }

    public ReactiveCommand<Unit, Unit> UndoCommand { get; }

    public ReactiveCommand<Unit, Unit> RedoCommand { get; }

    public ReactiveCommand<Unit, double> ZoomInCommand { get; }

    public ReactiveCommand<Unit, double> ZoomOutCommand { get; }

    public ReactiveCommand<Unit, double> ResetZoomCommand { get; }

    /// <summary>指定したファイルの内容で中身を差し替える。</summary>
    public void Load(string path)
    {
        LoadDocument(MindMapFileService.Load(path));
        CurrentFilePath = path;
        IsDirty = false;
        _history.Clear();
    }

    /// <summary>このタブを閉じてよいか。未保存なら保存するかどうかを尋ねる。</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        // DisplayName は未保存を示す * が付くので、確認の文面には素のファイル名を渡す。
        var label = CurrentFilePath is null ? _untitledName : Path.GetFileName(CurrentFilePath);

        switch (await _confirmSaveChanges.Handle(label))
        {
            case SaveChangesResult.Save:
                await SaveAsync();

                // 保存先を選ぶダイアログを取り消した場合はまだ未保存なので、閉じずに留まる。
                return !IsDirty;

            case SaveChangesResult.Discard:
                return true;

            default:
                return false;
        }
    }

    /// <summary>Ctrl+クリック用。選択に入っていなければ足し、入っていれば外す。</summary>
    public void ToggleSelection(NodeViewModel node)
    {
        var next = SelectedNodes.ToList();
        var removed = next.Remove(node);
        if (!removed)
        {
            next.Add(node);
        }

        SetSelection(next, removed ? next.LastOrDefault() : node);
    }

    /// <summary>Shift+クリック用。今の選択を保ったまま足す。</summary>
    public void AddToSelection(NodeViewModel node) => SetSelection(SelectedNodes.Append(node), node);

    /// <summary>範囲ドラッグ用。<paramref name="add"/> が true なら今の選択に足す。</summary>
    public void SelectNodes(IEnumerable<NodeViewModel> nodes, bool add)
    {
        var next = (add ? SelectedNodes.Concat(nodes) : nodes).ToList();
        SetSelection(next, next.LastOrDefault());
    }

    public void SelectAll() => SetSelection(Nodes, SelectedNode);

    public void ClearSelection() => SetSelection(Array.Empty<NodeViewModel>(), null);

    /// <summary>
    /// ドラッグ移動を 1 操作として履歴に積む。移動中の 1 ピクセルごとに積むと
    /// Undo が使い物にならないので、View がドラッグ終了時にだけ呼ぶ。
    /// 複数選択したまま動かしたときのために、動いたノードをまとめて受け取る。
    /// </summary>
    public void CompleteNodeDrag(IReadOnlyList<(NodeViewModel Node, double X, double Y)> origins)
    {
        var before = origins.Where(o => o.Node.X != o.X || o.Node.Y != o.Y).ToList();
        if (before.Count == 0)
        {
            return;
        }

        var after = before.Select(o => (o.Node, o.Node.X, o.Node.Y)).ToList();

        IsDirty = true;
        _history.Push(new DelegateUndoableAction(
            undo: () => ApplyPositions(before),
            redo: () => ApplyPositions(after)));
    }

    private void ApplyPositions(IReadOnlyList<(NodeViewModel Node, double X, double Y)> positions)
    {
        foreach (var (node, x, y) in positions)
        {
            node.X = x;
            node.Y = y;
        }

        SetSelection(positions.Select(p => p.Node).ToList(), positions[^1].Node);
        IsDirty = true;
    }

    /// <summary>
    /// 選択を入れ替える。<paramref name="primary"/> は子の追加位置や編集の基準になる代表ノード。
    /// </summary>
    private void SetSelection(IEnumerable<NodeViewModel> nodes, NodeViewModel? primary)
    {
        var next = new List<NodeViewModel>();
        foreach (var node in nodes)
        {
            // 畳まれて見えていないノードは選ばない。選べてしまうと、画面に出ていないものを
            // 消したり動かしたりできてしまう（すべて選択がいちばん通りやすい）。
            if (node.IsVisible && !next.Contains(node))
            {
                next.Add(node);
            }
        }

        var newPrimary = primary is not null && next.Contains(primary) ? primary : next.LastOrDefault();

        // 同じノードを選び直しただけなら触らない。編集中のノードを再クリックしたときに
        // 編集が終わってしまうのを防ぐため。
        if (next.Count == SelectedNodes.Count && next.All(SelectedNodes.Contains))
        {
            this.RaiseAndSetIfChanged(ref _selectedNode, newPrimary, nameof(SelectedNode));
            return;
        }

        // 選択から外れるノードは、編集中なら確定させてから外す。
        foreach (var node in SelectedNodes.Where(n => !next.Contains(n)).ToList())
        {
            node.IsEditing = false;
            node.IsSelected = false;
        }

        SelectedNodes.Clear();
        foreach (var node in next)
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }

        this.RaiseAndSetIfChanged(ref _selectedNode, newPrimary, nameof(SelectedNode));
    }

    /// <summary>編集中の内容を編集開始時点に戻して確定する（Escape 用）。</summary>
    public void CancelTextEdit()
    {
        if (_activeEdit is not { } edit)
        {
            return;
        }

        edit.Node.Title = edit.Title;
        edit.Node.Body = edit.Body;
        edit.Node.Link = edit.Link;
        edit.Node.IsEditing = false;
    }

    /// <summary>
    /// キャンバスに落とされたファイルを、選択中のノード（無ければルート）の子として追加する。
    /// タイトルはファイル名、本文はファイルの属性、リンクはファイル自身を指す。
    /// 複数まとめて落とされても 1 回の Undo で取り消せる。
    /// </summary>
    /// <param name="paths">落とされたファイル（フォルダー）のパス。</param>
    /// <param name="x">落とした位置。ここを起点に、縦に積んでいく。</param>
    /// <param name="y">落とした位置。</param>
    public void AddFileNodes(IReadOnlyList<string> paths, double x, double y)
    {
        if (DropParent() is not { } parent)
        {
            return;
        }

        var created = new List<NodeViewModel>();
        var top = y;

        foreach (var path in paths)
        {
            if (FileNodeContent.TryCreate(path) is not { } content)
            {
                continue;
            }

            // 制作日・更新日はファイル自身の日時に合わせる。AddNode より前に入れておくと、
            // 変更の監視が張られる前なので「今の時刻」で上書きされない。
            created.Add(new NodeViewModel(Guid.NewGuid(), content.Title, content.Body, x, top, content.Link)
            {
                CreatedAt = content.CreatedAt,
                UpdatedAt = content.UpdatedAt,
                Parent = parent,
            });

            top += NodeViewModel.DefaultHeight + VerticalGap;
        }

        AddDroppedNodes(created, parent);
    }

    /// <summary>
    /// ブラウザーから落とされた URL を、選択中のノード（無ければルート）の子として追加する。
    /// タイトルはページの題名、無ければ URL そのもの。リンクは URL を指す。
    ///
    /// ノードの上に落としたときと違い、こちらは<b>選択中のノードには触らない</b>。
    /// 調べものの途中で参照を足していくとき、いま書いているノードの題名や本文を
    /// 上書きされたくないため。複数まとめて落とされても 1 回の Undo で取り消せる。
    /// </summary>
    /// <param name="links">落とされた URL と、渡されていればページの題名。</param>
    /// <param name="x">落とした位置。ここを起点に、縦に積んでいく。</param>
    /// <param name="y">落とした位置。</param>
    public void AddLinkNodes(IReadOnlyList<DroppedUrl> links, double x, double y)
    {
        if (DropParent() is not { } parent)
        {
            return;
        }

        var created = new List<NodeViewModel>();
        var top = y;

        foreach (var link in links)
        {
            var title = string.IsNullOrWhiteSpace(link.Title) ? link.Url : link.Title.Trim();

            // 題名を 1 行目に出したときは、URL が見えなくなるので本文に残す。
            var body = title == link.Url ? string.Empty : link.Url;

            created.Add(new NodeViewModel(Guid.NewGuid(), title, body, x, top, link.Url)
            {
                Parent = parent,
            });

            top += NodeViewModel.DefaultHeight + VerticalGap;
        }

        AddDroppedNodes(created, parent);
    }

    /// <summary>
    /// 落とされたものをぶら下げる先。選択が無ければルートにする。
    /// 複数選択中は代表ノード（最後に選んだもの）を親にする。
    /// </summary>
    private NodeViewModel? DropParent() =>
        SelectedNode ?? Nodes.FirstOrDefault(n => n.Parent is null) ?? Nodes.FirstOrDefault();

    /// <summary>
    /// 落として作ったノードをマップに入れ、1 回の Undo でまとめて取り消せるようにする。
    /// ファイルでも URL でも同じ扱いにしたいので、ここに寄せてある。
    /// </summary>
    private void AddDroppedNodes(List<NodeViewModel> created, NodeViewModel parent)
    {
        if (created.Count == 0)
        {
            return;
        }

        foreach (var node in created)
        {
            AddNode(node);
        }

        SetSelection(created, created[^1]);
        IsDirty = true;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                SelectedNode = parent;
                foreach (var node in created)
                {
                    RemoveNode(node);
                }

                IsDirty = true;
            },
            redo: () =>
            {
                foreach (var node in created)
                {
                    AddNode(node);
                }

                SetSelection(created, created[^1]);
                IsDirty = true;
            }));
    }

    /// <summary>ファイル選択ダイアログを開き、選ばれたファイルをノードのリンクにする。</summary>
    public async Task SetFileLinkAsync(NodeViewModel node)
    {
        var path = await _showLinkFileDialog.Handle(Unit.Default);
        if (!string.IsNullOrEmpty(path))
        {
            SetLink(node, path);
        }
    }

    /// <summary>ノードのリンクを差し替える。空文字を渡すとリンクを外す。1 操作として元に戻せる。</summary>
    public void SetLink(NodeViewModel node, string newLink)
    {
        newLink = newLink?.Trim() ?? string.Empty;
        var oldLink = node.Link;
        if (oldLink == newLink)
        {
            return;
        }

        node.Link = newLink;
        SelectedNode = node;
        IsDirty = true;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                node.Link = oldLink;
                SelectedNode = node;
                IsDirty = true;
            },
            redo: () =>
            {
                node.Link = newLink;
                SelectedNode = node;
                IsDirty = true;
            }));
    }

    /// <summary>ノードの表示の大小を切り替える。1 操作として元に戻せる。</summary>
    private void ToggleCollapse(NodeViewModel node)
    {
        var newValue = !node.IsCollapsed;

        node.IsCollapsed = newValue;
        SelectedNode = node;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                node.IsCollapsed = !newValue;
                SelectedNode = node;
            },
            redo: () =>
            {
                node.IsCollapsed = newValue;
                SelectedNode = node;
            }));
    }

    /// <summary>
    /// 選んだノードの<b>直接の子だけ</b>を 1 列に並べる。孫より下は動かさない
    /// （動かす必要もない。子が動けば、その先は相対位置のまま付いてくる）。
    ///
    /// 並べるときに、子の本文を隠し、孫を畳む。並べた形を見るのに要らないものを
    /// たたんで、1 列の見通しを優先するため。並べ終わったら子をすべて選択するので、
    /// そのまま次の操作（まとめて動かす、削除する）に移れる。
    ///
    /// 位置・本文の畳み方・孫の畳み方・選択を、まとめて 1 回の Undo で戻せる。
    /// </summary>
    private async Task ArrangeChildrenAsync(LayoutOrientation orientation)
    {
        if (SelectedNode is not { } parent)
        {
            return;
        }

        // 画面に見えている並び順を崩さない。今の位置で並べ替えてから 1 列にする
        // （一覧の順で並べると、手で入れ替えた上下関係が実行のたびに元へ戻ってしまう）。
        var children = ChildrenOf(parent);
        children.Sort((a, b) => orientation == LayoutOrientation.Vertical
            ? a.Y.CompareTo(b.Y)
            : a.X.CompareTo(b.X));

        if (children.Count == 0)
        {
            return;
        }

        var before = children
            .Select(c => (Node: c, c.X, c.Y, c.IsCollapsed, Hidden: c.AreChildrenHidden))
            .ToList();

        foreach (var child in children)
        {
            child.IsCollapsed = true;
            child.AreChildrenHidden = true;
        }

        // 本文を隠したぶん、ノードは縮む。その新しい大きさで間を詰めたいので、
        // 位置を決める前に View に一度だけ測り直してもらう。
        await _measureNodes.Handle(Unit.Default);

        var placed = ChildLayout.Arrange(
            orientation,
            parent.X,
            parent.Y,
            parent.WorldWidth,
            parent.WorldHeight,
            children.Select(c => (c.WorldWidth, c.WorldHeight)).ToList(),
            HorizontalGap,
            VerticalGap);

        var after = children
            .Select((child, i) => (Node: child, placed[i].X, placed[i].Y, IsCollapsed: true, Hidden: true))
            .ToList();

        void Apply(IReadOnlyList<(NodeViewModel Node, double X, double Y, bool IsCollapsed, bool Hidden)> state)
        {
            foreach (var (node, x, y, collapsed, hidden) in state)
            {
                node.IsCollapsed = collapsed;
                node.AreChildrenHidden = hidden;
                node.X = x;
                node.Y = y;
            }

            IsDirty = true;
        }

        // 並べたあとは子がすべて選択された状態にする。代表は先頭（並べた順の 1 つ目）。
        // そのまま「まとめて動かす」「まとめて消す」に移れる。
        void SelectArranged()
        {
            Apply(after);
            SetSelection(children, children[0]);
        }

        SelectArranged();

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                Apply(before);

                // 元に戻したら、押したときに選んでいた親へ選択を返す。
                SelectedNode = parent;
            },
            redo: SelectArranged));
    }

    /// <summary>
    /// 子ノードを畳む・開く。畳むと子孫が見えなくなるので、その中に選択が残らないよう外す
    /// （見えないノードを選んだまま消す、が起きないため）。
    /// </summary>
    private void ToggleChildren(NodeViewModel node)
    {
        var newValue = !node.AreChildrenHidden;

        void Apply(bool hidden)
        {
            node.AreChildrenHidden = hidden;

            // 畳んだ中に選択が残っていることがあるので、選び直す
            // （見えなくなったぶんは SetSelection が落とす）。
            SetSelection(SelectedNodes.Append(node).ToList(), node);
            IsDirty = true;
        }

        Apply(newValue);

        _history.Push(new DelegateUndoableAction(
            undo: () => Apply(!newValue),
            redo: () => Apply(newValue)));
    }

    /// <summary>
    /// 保存する。保存先が決まっていなければダイアログで尋ねる。
    /// 取り消された場合は <see cref="IsDirty"/> が下りないので、呼び出し側から判別できる。
    /// </summary>
    public async Task SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        MindMapFileService.Save(CurrentFilePath, BuildDocument());
        IsDirty = false;
    }

    private async Task SaveAsAsync()
    {
        var suggested = CurrentFilePath is not null
            ? Path.GetFileName(CurrentFilePath)
            : SuggestFileName();

        var path = await _showSaveFileDialog.Handle(suggested);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        MindMapFileService.Save(path, BuildDocument());
        CurrentFilePath = path;
        IsDirty = false;
    }

    /// <summary>
    /// ノードのタイトルをファイル名の初期値にする。使えない文字は除去する。
    /// <paramref name="title"/> を省くとルートノードのタイトルを使う。
    /// </summary>
    private string SuggestFileName(string? title = null)
    {
        var baseName = (title ?? Nodes.FirstOrDefault(n => n.Parent is null)?.Title)?.Trim();
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = _untitledName;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        return baseName + MindMapFileService.FileExtension;
    }

    // ------------------------------------------------------------ 別ファイルへの切り出し

    /// <summary>
    /// ノードの子孫を別のファイルへ切り出す。ノード自身のコピーが新しいファイルのルートになり、
    /// 子ノードはその下にそのままぶら下がる。元のノードには新しいファイルへのリンクを、
    /// 新しいファイルのルートには元のファイルへのリンクを張って、行き来できるようにする。
    /// 切り出した子ノードは元のファイルから消す（リンクと削除はまとめて 1 回で元に戻せる）。
    /// </summary>
    public async Task ExtractChildrenToFileAsync(NodeViewModel node)
    {
        // CollectSubtree は自分自身から始まるので、2 つ目以降が子孫にあたる。
        var children = CollectSubtree(node).Skip(1).ToList();
        if (children.Count == 0)
        {
            return;
        }

        // 互いへのリンクは元のファイルの場所を基準に書くので、保存先が決まっていないと張れない。
        if (CurrentFilePath is not { } sourcePath)
        {
            await _showError.Handle("切り出したファイルとリンクし合うため、先にこのマップを保存してください。");
            return;
        }

        var path = await _showSaveFileDialog.Handle(SuggestFileName(node.Title));
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // 元のファイルを選ばれると、これから消す子ノードごと上書きしてしまう。
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            await _showError.Handle("元のファイルには切り出せません。別のファイルを指定してください。");
            return;
        }

        try
        {
            MindMapFileService.Save(path, BuildExtractedDocument(node, children, CrossLink(path, sourcePath)));
        }
        catch (Exception ex)
        {
            // 書き出せていない以上、元のファイルには手を付けずに終わる。
            await _showError.Handle($"切り出したファイルを保存できませんでした。\n\n{path}\n\n{ex.Message}");
            return;
        }

        var oldLink = node.Link;
        var newLink = CrossLink(sourcePath, path);

        Extract();

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                node.Link = oldLink;

                // CollectSubtree の順（親→子）のまま戻す。親が先にいないと接続線を張れない。
                foreach (var child in children)
                {
                    AddNode(child);
                }

                SelectedNode = node;
                IsDirty = true;
            },
            redo: Extract));

        void Extract()
        {
            node.Link = newLink;

            // 消す子ノードを選んだままにしないよう、先に元のノードだけの選択にしておく。
            SelectedNode = node;

            foreach (var child in children)
            {
                RemoveNode(child);
            }

            IsDirty = true;
        }
    }

    /// <summary>
    /// 切り出したノードを、新しいファイルの中身として組み立てる。
    /// <paramref name="node"/> のコピーをルートにし、その下に子ノードをぶら下げ直す。
    /// 子ノードは Id も制作日・更新日もそのまま引き継ぐ（元のファイルからは消えるため）。
    /// </summary>
    /// <param name="backLink">新しいファイルのルートに張る、元のファイルへのリンク。</param>
    private MindMapDocument BuildExtractedDocument(
        NodeViewModel node,
        IReadOnlyList<NodeViewModel> children,
        string backLink)
    {
        // ルートは元のノードとは別のノード（元のノードは元のファイルに残る）なので Id は振り直す。
        var rootId = Guid.NewGuid();

        var root = ToDto(node, null);
        root.Id = rootId;
        root.Link = backLink;

        var nodes = new List<MindMapNodeDto> { root };
        nodes.AddRange(children.Select(child =>
            ToDto(child, ReferenceEquals(child.Parent, node) ? rootId : child.Parent!.Id)));

        // 元の座標のままだと、キャンバスの隅にあった部分木は開いた直後の表示に入らない。
        // 位置関係は崩さず、かたまりごと左上へ寄せる。
        var offsetX = ExtractedMargin - nodes.Min(n => n.X);
        var offsetY = ExtractedMargin - nodes.Min(n => n.Y);

        foreach (var dto in nodes)
        {
            dto.X += offsetX;
            dto.Y += offsetY;
        }

        // 子の相対位置は動かさない（親との位置関係は変わらないため）。
        // 動くのはルートだけで、しかも新しいファイルではルート＝親がいないので、
        // 相対位置は寄せたあとの絶対位置そのものになる。
        root.Transform = new NodeTransform
        {
            Position = Vector3.Of(root.X, root.Y, root.Transform?.PositionZ ?? 0),
            Rotation = root.Transform?.Rotation,
            Scale = root.Transform?.Scale,
        };

        return new MindMapDocument
        {
            Version = MindMapDocument.CurrentVersion,
            Nodes = nodes,
        };
    }

    /// <summary>
    /// 切り出した 2 つのファイルが互いに張るリンク。
    ///
    /// 他のリンクと同じ規則（Root Path の下なら Root からの相対）で書く。ここだけ
    /// マップファイル基準にすると、1 つのファイルの中に基準の違う相対パスが混ざり、
    /// 同じ名前のファイルが両方の基準の先にあったときに取り違える。
    /// Root の外へ切り出したときは Root からは表せないので、相手のファイルからの相対に落とす。
    /// </summary>
    private static string CrossLink(string fromFile, string toFile)
    {
        var stored = LinkPathResolver.ToStoredLink(Path.GetFullPath(toFile));

        // 置き換わらなかった（＝Root の外、別ドライブ、設定が切）ときは絶対パスのまま返る。
        return Path.IsPathRooted(stored) ? LinkPath(fromFile, toFile) : stored;
    }

    /// <summary>
    /// <paramref name="fromFile"/> に書く、<paramref name="toFile"/> へのリンク。
    /// 相対パスにしておくと、2 つのファイルを一緒に別の場所へ移してもリンクが切れない
    /// （ドライブが違うなど相対で表せないときは絶対パスになる）。
    /// </summary>
    private static string LinkPath(string fromFile, string toFile)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fromFile));
            return string.IsNullOrEmpty(directory)
                ? toFile
                : Path.GetRelativePath(directory, Path.GetFullPath(toFile));
        }
        catch (ArgumentException)
        {
            // パスとして解釈できないときは、書かれたとおりのパスをそのまま使う。
            return toFile;
        }
    }

    private void AddChild()
    {
        if (SelectedNode is not { } parent)
        {
            return;
        }

        var (x, y) = NextChildPosition(parent);

        InsertNode(new NodeViewModel(Guid.NewGuid(), "新しいノード", string.Empty, x, y)
        {
            Parent = parent,
        });
    }

    private void AddSibling()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        // ルートには兄弟を作れないので、代わりに子を足す。
        if (node.Parent is not { } parent)
        {
            AddChild();
            return;
        }

        var siblings = ChildrenOf(parent);
        var y = siblings.Max(n => n.Y + n.WorldHeight) + VerticalGap;

        InsertNode(new NodeViewModel(Guid.NewGuid(), "新しいノード", string.Empty, node.X, y)
        {
            Parent = parent,
        });
    }

    /// <summary>
    /// 親に子を足すときの位置。既存の子の下に積み、まだ子がいなければ親の真横に置く。
    /// 手で足すときもツールが足すときも同じ並びになるよう、1 か所にまとめてある。
    /// </summary>
    private (double X, double Y) NextChildPosition(NodeViewModel parent)
    {
        var siblings = ChildrenOf(parent);

        return (
            parent.X + parent.WorldWidth + HorizontalGap,
            siblings.Count == 0 ? parent.Y : siblings.Max(n => n.Y + n.WorldHeight) + VerticalGap);
    }

    private List<NodeViewModel> ChildrenOf(NodeViewModel parent) =>
        Nodes.Where(n => ReferenceEquals(n.Parent, parent)).ToList();

    private void InsertNode(NodeViewModel node)
    {
        AddNode(node);
        SelectedNode = node;
        node.IsEditing = true;
        IsDirty = true;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                SelectedNode = node.Parent;
                RemoveNode(node);
                IsDirty = true;
            },
            redo: () =>
            {
                AddNode(node);
                SelectedNode = node;
                IsDirty = true;
            }));
    }

    /// <summary>
    /// 選択中のノードを部分木ごと消す。複数選んでいれば 1 回の Undo でまとめて消える。
    /// ルートを消すとマップが空になるので、選択に混ざっていても対象から外す。
    /// </summary>
    private void DeleteSelection()
    {
        var targets = SelectionRoots().Where(n => n.Parent is not null).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        // 消したあとの選択は、最初に消す枝の親に移す（消えたノードを選んだままにしない）。
        var parent = targets[0].Parent!;
        var subtree = targets.SelectMany(CollectSubtree).ToList();
        var selection = SelectedNodes.ToList();

        SelectedNode = parent;
        foreach (var node in subtree)
        {
            RemoveNode(node);
        }

        IsDirty = true;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                // 親を先に戻さないと接続線が張れないので、CollectSubtree の順序（親→子）を保つ。
                foreach (var node in subtree)
                {
                    AddNode(node);
                }

                SetSelection(selection, selection.LastOrDefault());
                IsDirty = true;
            },
            redo: () =>
            {
                SelectedNode = parent;
                foreach (var node in subtree)
                {
                    RemoveNode(node);
                }

                IsDirty = true;
            }));
    }

    // ------------------------------------------------------------ 切り取り・コピー・貼り付け

    private async Task CopySelectionAsync()
    {
        var roots = SelectionRoots();
        if (roots.Count > 0)
        {
            await _clipboard.WriteAsync(BuildFragment(roots));
        }
    }

    private async Task CutSelectionAsync()
    {
        // ルートは消せないので、コピーする範囲も実際に消えるものだけに揃える
        // （貼り付けたら元にもある、という食い違いを作らないため）。
        var roots = SelectionRoots().Where(n => n.Parent is not null).ToList();
        if (roots.Count == 0)
        {
            return;
        }

        await _clipboard.WriteAsync(BuildFragment(roots));
        DeleteSelection();
    }

    /// <summary>
    /// クリップボードのノードを、選択中のノード（無ければルート）の子として貼り付ける。
    /// 別のファイルからのものでも、Id を振り直すので同じ手順で貼り込める。
    /// </summary>
    private async Task PasteAsync()
    {
        if (await _clipboard.ReadAsync() is not { Nodes.Count: > 0 } fragment)
        {
            return;
        }

        var target = SelectedNode ?? Nodes.FirstOrDefault(n => n.Parent is null) ?? Nodes.FirstOrDefault();
        if (target is null)
        {
            return;
        }

        var created = MaterializeFragment(fragment, out var roots);
        if (created.Count == 0)
        {
            return;
        }

        // 子ノードを足すときと同じ場所に置く。かたまりの中の位置関係は崩したくないので、
        // 全体を同じ量だけずらして、左上のノードがその場所に来るようにする。
        var siblings = ChildrenOf(target);
        var offsetX = target.X + target.WorldWidth + HorizontalGap - created.Min(n => n.X);
        var offsetY = (siblings.Count == 0 ? target.Y : siblings.Max(n => n.Y + n.WorldHeight) + VerticalGap)
                      - created.Min(n => n.Y);

        // ずらすのは、かたまりの根だけでよい。子は親からの相対で置かれているので付いてくる。
        // 全部に掛けると、親のぶんと自分のぶんで二重にずれる。
        foreach (var root in roots)
        {
            root.X += offsetX;
            root.Y += offsetY;
        }

        foreach (var root in roots)
        {
            root.Parent = target;
        }

        foreach (var node in created)
        {
            AddNode(node);
        }

        SetSelection(roots, roots[^1]);
        IsDirty = true;

        _history.Push(new DelegateUndoableAction(
            undo: () =>
            {
                SelectedNode = target;
                foreach (var node in created)
                {
                    RemoveNode(node);
                }

                IsDirty = true;
            },
            redo: () =>
            {
                foreach (var node in created)
                {
                    AddNode(node);
                }

                SetSelection(roots, roots[^1]);
                IsDirty = true;
            }));
    }

    /// <summary>
    /// 選択のうち、他の選択ノードの子孫になっていないものだけを返す。
    /// 親と子を両方選んでいても、部分木を二重に扱わないようにするため。
    ///
    /// ドラッグも同じ理由でこれを使う。親を動かせば子は付いてくるので、
    /// 子まで動かすと移動量が二重に掛かってしまう。
    /// </summary>
    public List<NodeViewModel> SelectionRoots()
    {
        var selected = SelectedNodes.ToHashSet();
        return SelectedNodes.Where(node => !HasSelectedAncestor(node, selected)).ToList();
    }

    private static bool HasSelectedAncestor(NodeViewModel node, HashSet<NodeViewModel> selected)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (selected.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>コピー対象の部分木を、保存ファイルと同じ形の「かたまり」にする。</summary>
    private MindMapDocument BuildFragment(IReadOnlyList<NodeViewModel> roots)
    {
        var nodes = new List<NodeViewModel>();
        foreach (var node in roots.SelectMany(CollectSubtree))
        {
            if (!nodes.Contains(node))
            {
                nodes.Add(node);
            }
        }

        var ids = nodes.Select(n => n.Id).ToHashSet();

        return new MindMapDocument
        {
            Version = MindMapDocument.CurrentVersion,

            // かたまりの外にいる親は連れて行けないので、切り離してルートにする。
            Nodes = nodes
                .Select(n => ToDto(n, n.Parent is { } parent && ids.Contains(parent.Id) ? parent.Id : null))
                .ToList(),
        };
    }

    /// <summary>
    /// かたまりの中身をノードとして作り直す。親から順に並べて返すので、
    /// 呼び出し側はその順に <see cref="AddNode"/> すれば接続線が正しく張られる。
    /// </summary>
    private static List<NodeViewModel> MaterializeFragment(
        MindMapDocument fragment,
        out List<NodeViewModel> roots)
    {
        var byOldId = new Dictionary<Guid, NodeViewModel>();
        var pairs = new List<(MindMapNodeDto Dto, NodeViewModel Node)>();

        foreach (var dto in fragment.Nodes)
        {
            // Id が重複した壊れたデータは、最初の 1 つだけ拾う。
            if (byOldId.ContainsKey(dto.Id))
            {
                continue;
            }

            // 貼り付けたノードは元とは別物なので Id は振り直す（同じファイルに貼っても衝突しない）。
            var node = new NodeViewModel(Guid.NewGuid(), dto.ResolveTitle(), dto.Body, dto.X, dto.Y, dto.Link)
            {
                IsCollapsed = dto.Collapsed,
                AreChildrenHidden = dto.ChildrenCollapsed,
                Extra = dto.Extra,
            };

            // 制作日・更新日は引き継ぐ。別のファイルへ移したときに、いつ書いたものかを失わないため。
            if (dto.CreatedAt is { } createdAt)
            {
                node.CreatedAt = createdAt;
            }

            if (dto.UpdatedAt is { } updatedAt)
            {
                node.UpdatedAt = updatedAt;
            }

            byOldId[dto.Id] = node;
            pairs.Add((dto, node));
        }

        foreach (var (dto, node) in pairs)
        {
            if (dto.ParentId is { } parentId
                && byOldId.TryGetValue(parentId, out var parent)
                && !CreatesCycle(node, parent))
            {
                node.Parent = parent;
            }
        }

        // 読み込みと同じく、親を繋いだあとに置き方を入れる。
        ApplyTransforms(OrderTopDown(pairs));

        var all = pairs.Select(p => p.Node).ToList();
        roots = all.Where(n => n.Parent is null).ToList();

        var ordered = new List<NodeViewModel>();
        var queue = new Queue<NodeViewModel>(roots);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            ordered.Add(current);

            foreach (var child in all.Where(n => ReferenceEquals(n.Parent, current)))
            {
                queue.Enqueue(child);
            }
        }

        return ordered;
    }

    private void BeginEditSelectedNode()
    {
        if (SelectedNode is { } node)
        {
            node.IsEditing = true;
        }
    }

    /// <summary>
    /// 保存された置き方をノードへ入れる。<paramref name="topDown"/> は親が先に来る順であること
    /// （親の倍率が子の世界での位置に効くので、先に親を確定させないと食い違いの判定を誤る）。
    ///
    /// <see cref="MindMapNodeDto.X"/> / <see cref="MindMapNodeDto.Y"/> と食い違っていたら、
    /// そちらを正として相対位置を組み直す。置き方を知らない版や DeviceMap で動かして
    /// 保存された、と解釈するため。
    /// </summary>
    private static void ApplyTransforms(IEnumerable<(MindMapNodeDto Dto, NodeViewModel Node)> topDown)
    {
        foreach (var (dto, node) in topDown)
        {
            // Version 9 より前のファイル。X / Y から組んだ相対位置をそのまま使う。
            if (dto.Transform is not { } transform)
            {
                continue;
            }

            node.RotationX = transform.RotationX;
            node.RotationY = transform.RotationY;
            node.RotationZ = transform.RotationZ;

            node.SetLocalScale(transform.ScaleX, transform.ScaleY, transform.ScaleZ);
            node.SetLocalPosition(transform.PositionX, transform.PositionY, transform.PositionZ);

            if (Math.Abs(node.X - dto.X) > PositionTolerance
                || Math.Abs(node.Y - dto.Y) > PositionTolerance)
            {
                node.SetWorldPosition(dto.X, dto.Y);
            }
        }
    }

    /// <summary>親から順に並べ直す。読み込んだ木の深さで並べるだけ。</summary>
    private static List<(MindMapNodeDto Dto, NodeViewModel Node)> OrderTopDown(
        IEnumerable<(MindMapNodeDto Dto, NodeViewModel Node)> pairs)
    {
        static int Depth(NodeViewModel node)
        {
            var depth = 0;
            for (var current = node.Parent; current is not null; current = current.Parent)
            {
                depth++;
            }

            return depth;
        }

        return pairs.OrderBy(pair => Depth(pair.Node)).ToList();
    }

    /// <summary>
    /// ノードのリンク先から小さな絵を作り直す。作れなければ絵を外すだけ
    /// （リンクを画像から文書に差し替えたときに、前の絵が残らないようにする）。
    ///
    /// 待たない。絵は出来次第あとから入るもので、それまではノードが本文だけで描かれる。
    /// 未保存の印も立てない（絵はファイルに保存しないので、変わっても保存すべきものは増えない）。
    /// </summary>
    private void RefreshThumbnail(NodeViewModel node)
    {
        var path = LinkPathResolver.Resolve(node.Link, CurrentFilePath);
        var pending = _thumbnails.GetAsync(path);

        if (pending.IsCompletedSuccessfully)
        {
            node.Thumbnail = pending.Result;
            return;
        }

        _ = Assign(pending);

        async Task Assign(Task<object?> task)
        {
            var image = await task;

            // 待っている間にリンクが変わっていたら、古い絵は入れない。
            if (LinkPathResolver.Resolve(node.Link, CurrentFilePath) == path)
            {
                node.Thumbnail = image;
            }
        }
    }

    /// <summary>自分自身を含む部分木を親から順に返す。削除時に子孫を取り残さないため。</summary>
    private List<NodeViewModel> CollectSubtree(NodeViewModel root)
    {
        var result = new List<NodeViewModel>();
        var queue = new Queue<NodeViewModel>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var child in Nodes.Where(n => ReferenceEquals(n.Parent, current)))
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private void AddNode(NodeViewModel node)
    {
        Nodes.Add(node);

        if (node.Parent is { } parent)
        {
            Connections.Add(new ConnectionViewModel(parent, node));
            parent.HasChildren = true;
        }

        RefreshThumbnail(node);

        _nodeSubscriptions[node.Id] = new CompositeDisposable(
            node.Changed
                .Where(e => e.PropertyName is nameof(NodeViewModel.Title)
                    or nameof(NodeViewModel.Body)
                    or nameof(NodeViewModel.Link)
                    or nameof(NodeViewModel.IsCollapsed)
                    or nameof(NodeViewModel.AreChildrenHidden)
                    or nameof(NodeViewModel.X)
                    or nameof(NodeViewModel.Y)
                    or nameof(NodeViewModel.WorldScaleX)
                    or nameof(NodeViewModel.WorldScaleY))
                .Subscribe(e =>
                {
                    // 更新日は「内容」を変えたときだけ進める。位置移動や表示の大小は含めない。
                    if (e.PropertyName is nameof(NodeViewModel.Title)
                        or nameof(NodeViewModel.Body)
                        or nameof(NodeViewModel.Link))
                    {
                        node.UpdatedAt = DateTimeOffset.Now;
                    }

                    // リンクを差し替えたら、絵も差し替える。
                    if (e.PropertyName is nameof(NodeViewModel.Link))
                    {
                        RefreshThumbnail(node);
                    }

                    IsDirty = true;
                }),
            node.WhenAnyValue(n => n.IsEditing)
                .Skip(1)
                .Subscribe(isEditing => OnEditingChanged(node, isEditing)));
    }

    /// <summary>
    /// 編集の開始から終了までを 1 回の Undo 単位にまとめる。1 文字ごとに履歴を積むと
    /// Undo が実用にならないため、確定時に差分があったときだけ積む。
    /// </summary>
    private void OnEditingChanged(NodeViewModel node, bool isEditing)
    {
        if (isEditing)
        {
            _activeEdit = (node, node.Title, node.Body, node.Link);
            return;
        }

        if (_activeEdit is not { } edit || !ReferenceEquals(edit.Node, node))
        {
            return;
        }

        _activeEdit = null;
        PushContentChange(node, (edit.Title, edit.Body, edit.Link));
    }

    /// <summary>
    /// キャンバスの外（ビューアの本文欄）で内容を編集し始めたときに呼ぶ。
    /// ノードの見た目は変えず、Undo のまとまりだけを作る。
    /// キャンバス上の編集とは別の控えに持ち、取り違えが起きないようにする。
    /// </summary>
    public void BeginExternalEdit(NodeViewModel node)
    {
        // 別のノードに移ったときは、前のぶんをそこで確定させる。
        if (_externalEdit is { } previous && !ReferenceEquals(previous.Node, node))
        {
            EndExternalEdit(previous.Node);
        }

        _externalEdit ??= (node, node.Title, node.Body, node.Link);
    }

    /// <summary>編集を終えたときに呼ぶ。始めた時点から変わっていれば 1 回の Undo として積む。</summary>
    public void EndExternalEdit(NodeViewModel node)
    {
        if (_externalEdit is not { } edit || !ReferenceEquals(edit.Node, node))
        {
            return;
        }

        _externalEdit = null;
        PushContentChange(node, (edit.Title, edit.Body, edit.Link));
    }

    /// <summary>編集を始めた時点と今を比べ、変わっていれば 1 回ぶんの Undo として積む。</summary>
    private void PushContentChange(NodeViewModel node, (string Title, string Body, string Link) before)
    {
        if (before.Title == node.Title && before.Body == node.Body && before.Link == node.Link)
        {
            return;
        }

        // タイトル・本文・リンクはひとつの編集操作なので、まとめて 1 回の Undo にする。
        var after = (node.Title, node.Body, node.Link);

        _history.Push(new DelegateUndoableAction(
            undo: () => Restore(node, before),
            redo: () => Restore(node, after)));
    }

    private void Restore(NodeViewModel node, (string Title, string Body, string Link) content)
    {
        node.Title = content.Title;
        node.Body = content.Body;
        node.Link = content.Link;
        SelectedNode = node;
        IsDirty = true;
    }

    private void RemoveNode(NodeViewModel node)
    {
        foreach (var connection in Connections
                     .Where(c => ReferenceEquals(c.Child, node) || ReferenceEquals(c.Parent, node))
                     .ToList())
        {
            Connections.Remove(connection);
            connection.Dispose();
        }

        if (_nodeSubscriptions.Remove(node.Id, out var subscription))
        {
            subscription.Dispose();
        }

        // 消えたノードを選んだままにしない。
        if (SelectedNodes.Remove(node))
        {
            node.IsSelected = false;

            if (ReferenceEquals(_selectedNode, node))
            {
                this.RaiseAndSetIfChanged(
                    ref _selectedNode,
                    SelectedNodes.LastOrDefault(),
                    nameof(SelectedNode));
            }
        }

        Nodes.Remove(node);

        // 最後の子が消えたら、親は「子なし」に戻る。
        if (node.Parent is { } parent)
        {
            parent.HasChildren = Nodes.Any(n => ReferenceEquals(n.Parent, parent));
        }
    }

    private void Clear()
    {
        ClearSelection();
        _activeEdit = null;
        _externalEdit = null;
        _documentExtra = null;

        foreach (var connection in Connections)
        {
            connection.Dispose();
        }

        Connections.Clear();

        foreach (var subscription in _nodeSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _nodeSubscriptions.Clear();
        Nodes.Clear();
    }

    private MindMapDocument BuildDocument() => new()
    {
        Version = MindMapDocument.CurrentVersion,
        Nodes = Nodes.Select(n => ToDto(n, n.Parent?.Id)).ToList(),
        Extra = _documentExtra,
    };

    private static MindMapNodeDto ToDto(NodeViewModel node, Guid? parentId) => new()
    {
        Id = node.Id,
        ParentId = parentId,
        Title = node.Title,
        Body = node.Body,

        // リンクは、書くときだけ Root からの相対に直す（設定と Root の下にあることが条件）。
        // 画面の側は絶対パスのまま持っておく。ここで node.Link を書き換えてしまうと、
        // 保存しただけで未保存の印が立ち、Undo の記録にも残ってしまう。
        Link = LinkPathResolver.ToStoredLink(node.Link),
        Collapsed = node.IsCollapsed,
        ChildrenCollapsed = node.AreChildrenHidden,
        CreatedAt = node.CreatedAt,
        UpdatedAt = node.UpdatedAt,
        // 絶対位置は、置き方を知らない版と DeviceMap のために書き続ける。
        X = node.X,
        Y = node.Y,
        Transform = new NodeTransform
        {
            Position = Vector3.Of(node.LocalX, node.LocalY, node.LocalZ),

            // 既定のままの欄は書き出さない。ほとんどのノードは回っておらず等倍なので、
            // 全部書くとノード 1 つが 3 倍の行数になり、ファイルを目で追えなくなる。
            Rotation = node.RotationX == 0 && node.RotationY == 0 && node.RotationZ == 0
                ? null
                : Vector3.Of(node.RotationX, node.RotationY, node.RotationZ),
            Scale = node.ScaleX == 1 && node.ScaleY == 1 && node.ScaleZ == 1
                ? null
                : Vector3.Of(node.ScaleX, node.ScaleY, node.ScaleZ),
        },

        // 知らない欄はそのまま書き戻す。読んだときに捨てていないので、
        // このアプリが知らないパッケージの情報もファイルに残る。
        Extra = node.Extra,
    };

    private void LoadDocument(MindMapDocument document)
    {
        Clear();
        _documentExtra = document.Extra;

        // 親子を結ぶ前に全ノードを実体化しておく（ファイル内の並び順に依存しないため）。
        var byId = new Dictionary<Guid, NodeViewModel>();
        var pairs = new List<(MindMapNodeDto Dto, NodeViewModel Node)>();
        foreach (var dto in document.Nodes)
        {
            // 旧形式（Version 1）のファイルはタイトルが Text 欄に入っている。
            // IsCollapsed は AddNode で監視を張る前に入れておく（読み込みで未保存扱いにしないため）。
            var node = new NodeViewModel(dto.Id, dto.ResolveTitle(), dto.Body, dto.X, dto.Y, dto.Link)
            {
                IsCollapsed = dto.Collapsed,
                AreChildrenHidden = dto.ChildrenCollapsed,
                Extra = dto.Extra,
            };

            // 保存済みの日時があれば復元する。無い（Version 4 以前の）ファイルは
            // コンストラクタが入れた現在時刻を制作日・更新日として使う。
            if (dto.CreatedAt is { } createdAt)
            {
                node.CreatedAt = createdAt;
            }

            if (dto.UpdatedAt is { } updatedAt)
            {
                node.UpdatedAt = updatedAt;
            }

            byId[dto.Id] = node;
            pairs.Add((dto, node));
        }

        foreach (var dto in document.Nodes)
        {
            if (dto.ParentId is { } parentId
                && byId.TryGetValue(parentId, out var parent)
                && !CreatesCycle(byId[dto.Id], parent))
            {
                byId[dto.Id].Parent = parent;
            }
        }

        // 置き方は親を繋いだあとに入れる（相対位置は親が決まらないと世界での位置にならない）。
        // AddNode より前に済ませるのは、監視を張る前に入れて未保存扱いにしないため。
        ApplyTransforms(OrderTopDown(pairs));

        foreach (var node in byId.Values)
        {
            AddNode(node);
        }

        SelectedNode = Nodes.FirstOrDefault(n => n.Parent is null) ?? Nodes.FirstOrDefault();
    }

    /// <summary>
    /// 壊れたファイルで親子関係が輪になっていると、部分木の走査が終わらなくなる。
    /// 輪を作る親子付けは読み込み時点で捨てる。
    /// </summary>
    private static bool CreatesCycle(NodeViewModel node, NodeViewModel candidateParent)
    {
        for (var current = candidateParent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, node))
            {
                return true;
            }
        }

        return false;
    }
}
