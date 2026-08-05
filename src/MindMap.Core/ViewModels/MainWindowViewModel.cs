using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using MindMap.Services;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// ウィンドウ全体。開いているドキュメント（タブ）の集まりを持つだけで、
/// マップそのものの操作は <see cref="DocumentViewModel"/> 側にある。
/// </summary>
public sealed class MainWindowViewModel : ReactiveObject
{
    private readonly ObservableAsPropertyHelper<string> _title;

    /// <summary>クリップボードの読み書き。OS ごとに違うので UI 側から受け取り、各タブへ配る。</summary>
    private readonly INodeClipboard _clipboard;

    /// <summary>「無題 1」「無題 2」… と通し番号を振るための連番。</summary>
    private int _untitledCounter;

    private DocumentViewModel? _activeDocument;

    /// <param name="clipboard">
    /// 省略すると何もしないクリップボードになる（画面につながっていない状態でも組み立てられるように）。
    /// </param>
    public MainWindowViewModel(INodeClipboard? clipboard = null)
    {
        _clipboard = clipboard ?? NullNodeClipboard.Instance;

        var hasActiveDocument = this.WhenAnyValue(x => x.ActiveDocument).Select(d => d is not null);

        // 未保存のタブが 1 つでもあるときだけ「すべて保存」を押せるようにする。
        // タブは増減するので、開いているタブが変わるたびに監視対象を組み替える。
        var documentsChanged = Observable
            .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Documents.CollectionChanged += h,
                h => Documents.CollectionChanged -= h)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        var hasDirtyDocument = documentsChanged
            .Select(_ => Documents.Count == 0
                ? Observable.Return(false)
                : Documents
                    .Select(d => d.WhenAnyValue(x => x.IsDirty))
                    .CombineLatest(flags => flags.Any(dirty => dirty)))
            .Switch();

        NewDocumentCommand = ReactiveCommand.Create(() => AddDocument(CreateDocument()));
        OpenDocumentCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        OpenLinkCommand = ReactiveCommand.CreateFromTask<NodeViewModel>(OpenLinkAsync);
        SaveAllCommand = ReactiveCommand.CreateFromTask(SaveAllAsync, hasDirtyDocument);
        CloseDocumentCommand = ReactiveCommand.CreateFromTask<DocumentViewModel>(CloseDocumentAsync);
        CloseActiveDocumentCommand = ReactiveCommand.CreateFromTask(
            () => CloseDocumentAsync(ActiveDocument!),
            hasActiveDocument);

        Observable
            .Merge(
                NewDocumentCommand.ThrownExceptions,
                OpenDocumentCommand.ThrownExceptions,
                OpenLinkCommand.ThrownExceptions,
                SaveAllCommand.ThrownExceptions,
                CloseDocumentCommand.ThrownExceptions,
                CloseActiveDocumentCommand.ThrownExceptions)
            .SelectMany(ex => ShowError.Handle(ex.Message))
            .Subscribe();

        _title = this
            .WhenAnyValue(x => x.ActiveDocument, x => x.ActiveDocument!.DisplayName)
            .Select(t => t.Item1 is null ? "MindMap" : $"{t.Item2} - MindMap")
            .ToProperty(this, x => x.Title);

        AddDocument(CreateDocument());
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    public DocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        set => this.RaiseAndSetIfChanged(ref _activeDocument, value);
    }

    public string Title => _title.Value;

    public ReactiveCommand<Unit, Unit> NewDocumentCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenDocumentCommand { get; }

    /// <summary>タブの × ボタン用。閉じる対象を引数で受け取る。</summary>
    public ReactiveCommand<DocumentViewModel, Unit> CloseDocumentCommand { get; }

    /// <summary>ノードのリンクを開く。マインドマップなら新しいタブ、それ以外は外部アプリ。</summary>
    public ReactiveCommand<NodeViewModel, Unit> OpenLinkCommand { get; }

    /// <summary>開いているタブのうち、未保存のものをまとめて保存する。</summary>
    public ReactiveCommand<Unit, Unit> SaveAllCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseActiveDocumentCommand { get; }

    /// <summary>「開く」ダイアログを View 側に依頼する。キャンセル時は null を返す。</summary>
    public Interaction<Unit, string?> ShowOpenFileDialog { get; } = new();

    /// <summary>「名前を付けて保存」ダイアログ。引数は初期ファイル名。</summary>
    public Interaction<string?, string?> ShowSaveFileDialog { get; } = new();

    /// <summary>ノードのリンク先にするファイルを選ぶダイアログ。キャンセル時は null。</summary>
    public Interaction<Unit, string?> ShowLinkFileDialog { get; } = new();

    /// <summary>未保存のまま閉じようとしたときの確認。引数はドキュメント名。</summary>
    public Interaction<string, SaveChangesResult> ConfirmSaveChanges { get; } = new();

    public Interaction<string, Unit> ShowError { get; } = new();

    /// <summary>URL やファイルを OS に渡して開いてもらう。</summary>
    public Interaction<string, Unit> OpenExternal { get; } = new();

    /// <summary>ウィンドウを閉じてよいか。未保存のタブがあれば 1 つずつ確認する。</summary>
    public async Task<bool> CanCloseAsync()
    {
        foreach (var document in Documents.ToList())
        {
            // どのタブについて聞かれているのか分かるよう、確認の前に見せておく。
            ActiveDocument = document;

            if (!await document.CanCloseAsync())
            {
                return false;
            }
        }

        return true;
    }

    private DocumentViewModel CreateDocument() =>
        new(
            $"無題 {++_untitledCounter}",
            ShowSaveFileDialog,
            ShowLinkFileDialog,
            ConfirmSaveChanges,
            ShowError,
            _clipboard);

    private void AddDocument(DocumentViewModel document)
    {
        Documents.Add(document);
        ActiveDocument = document;
    }

    /// <summary>
    /// コマンドラインで渡されたファイルを開く。拡張子の関連付けから起動されたとき、
    /// エクスプローラーが対象のパスを引数で渡してくるため。
    /// 起動直後の空タブは、ファイルが開けたなら残さず置き換える。
    /// </summary>
    public void OpenFiles(IEnumerable<string> paths)
    {
        // 置き換えてよいのは「まだ何もしていない、保存先の無いタブ」だけ。
        var initial = Documents is [{ CurrentFilePath: null, IsDirty: false } only] ? only : null;

        var opened = false;

        foreach (var path in paths)
        {
            try
            {
                OpenInTab(path);
                opened = true;
            }
            catch (Exception ex)
            {
                // 1 つ開けなくても、残りのファイルは開けるように続ける。
                ShowError.Handle($"ファイルを開けませんでした。\n\n{path}\n\n{ex.Message}").Subscribe();
            }
        }

        if (opened && initial is not null && Documents.Count > 1)
        {
            Documents.Remove(initial);
        }
    }

    private async Task OpenAsync()
    {
        var path = await ShowOpenFileDialog.Handle(Unit.Default);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        OpenInTab(path);
    }

    /// <summary>ファイルをタブで開く。すでに開いていれば、二重に開かずそのタブに切り替える。</summary>
    private void OpenInTab(string path)
    {
        var already = Documents.FirstOrDefault(d =>
            d.CurrentFilePath is not null &&
            string.Equals(
                Path.GetFullPath(d.CurrentFilePath),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase));

        if (already is not null)
        {
            ActiveDocument = already;
            return;
        }

        var document = CreateDocument();
        document.Load(path);
        AddDocument(document);
    }

    /// <summary>
    /// 未保存のタブを順に保存する。保存先の無いタブはダイアログで尋ねることになるので、
    /// どのタブについて聞かれているのか分かるよう、先にそのタブを見せる。
    /// 取り消されたタブは未保存のまま残し、残りのタブは続けて保存する。
    /// </summary>
    private async Task SaveAllAsync()
    {
        var active = ActiveDocument;

        foreach (var document in Documents.ToList())
        {
            if (!document.IsDirty)
            {
                continue;
            }

            if (document.CurrentFilePath is null)
            {
                ActiveDocument = document;
            }

            try
            {
                await document.SaveAsync();
            }
            catch (Exception ex)
            {
                // 1 つ保存できなくても、残りのタブは保存できるように続ける。
                await ShowError.Handle($"保存できませんでした。\n\n{document.DisplayName}\n\n{ex.Message}");
            }
        }

        // 保存のために切り替えた場合は、元に見ていたタブに戻す。
        if (active is not null && Documents.Contains(active))
        {
            ActiveDocument = active;
        }
    }

    private async Task OpenLinkAsync(NodeViewModel node)
    {
        if (string.IsNullOrWhiteSpace(node.Link))
        {
            return;
        }

        var link = node.Link.Trim();

        // マインドマップのファイルは外部アプリに渡さず、自分の新しいタブで開く。
        if (ResolveMindMapPath(link) is { } mapPath)
        {
            OpenInTab(mapPath);
            return;
        }

        await OpenExternal.Handle(link);
    }

    /// <summary>
    /// リンクが手元のマインドマップファイルを指しているならその絶対パスを返す。
    /// 相対パスは、リンク元のドキュメントが置かれた場所を基準に解く。
    /// </summary>
    private string? ResolveMindMapPath(string link)
    {
        // http/https/mailto など、ファイル以外を指す URL はここでは扱わない。
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }

        try
        {
            var path = link;
            if (!Path.IsPathRooted(path))
            {
                if (ActiveDocument?.CurrentFilePath is not { } baseFile)
                {
                    return null;
                }

                path = Path.Combine(Path.GetDirectoryName(baseFile) ?? string.Empty, path);
            }

            path = Path.GetFullPath(path);

            var isMindMap = string.Equals(
                Path.GetExtension(path),
                MindMapFileService.FileExtension,
                StringComparison.OrdinalIgnoreCase);

            return isMindMap && File.Exists(path) ? path : null;
        }
        catch (ArgumentException)
        {
            // パスに使えない文字が入っていた場合。リンクとしては外部に投げる。
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task CloseDocumentAsync(DocumentViewModel document)
    {
        if (!Documents.Contains(document) || !await document.CanCloseAsync())
        {
            return;
        }

        // 閉じたあとは隣のタブに移る。最後の 1 枚だったら新しい空のタブを開く。
        var index = Documents.IndexOf(document);
        Documents.Remove(document);

        if (Documents.Count == 0)
        {
            AddDocument(CreateDocument());
            return;
        }

        ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
    }
}
