using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using MindMap.Services;
using MindMap.Services.Viewers;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// 画面右のビューア。選択中のノードの本文を編集するか、リンク先の中身を見せる。
///
/// 「何を出すか」がここ、「どう描くか」は <see cref="ViewerSession"/> が選んだビューアの側。
/// リンク先の描画はパッケージで差し替わるので、ここは出来上がった画面を枠に載せるだけで、
/// 中身が Markdown なのか画像なのかを知らない。
/// </summary>
public sealed class ViewerViewModel : ReactiveObject
{
    private const double DefaultWidth = 380;

    /// <summary>これより狭くはできない。狭すぎると本文が 1 行ずつになって読めないため。</summary>
    private const double MinWidth = 200;

    /// <summary>ビューアを広げても、キャンバス側にはこれだけ残す。</summary>
    private const double MinCanvasWidth = 320;

    /// <summary>リンクタブの見出しに出す名前の長さ。これを超えたら末尾を省く。</summary>
    private const int MaxTabLabelLength = 24;

    /// <summary>
    /// リンク先を読みに行くまでの待ち。矢印キーでノードを送ると選択が続けて変わるので、
    /// 落ち着くまで始めない。本文の編集はこの待ちを通さない（購読を分けてある）。
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private const string NoTargetMessage = "ノードを選ぶと、ここに内容が出ます。";
    private const string NoLinkLabel = "リンク";

    private readonly ILinkViewerHost _session;

    /// <summary>Undo のまとまりを作るための、編集中のタブとノード。編集していなければ null。</summary>
    private (DocumentViewModel Document, NodeViewModel Node)? _editing;

    /// <summary>いま見ているタブ。相対リンクの基準と、本文の編集を履歴に積むのに要る。</summary>
    private DocumentViewModel? _document;

    private NodeViewModel? _node;
    private ViewerSource _sourceKind = ViewerSource.Body;
    private bool _isVisible;
    private bool _canOpenLink;
    private double _width = DefaultWidth;
    private string _caption = string.Empty;
    private string _tabLabel = NoLinkLabel;
    private string _body = string.Empty;
    private string _link = string.Empty;
    private object? _linkView;
    private string? _message = NoTargetMessage;

    public ViewerViewModel(ILinkViewerHost viewers)
    {
        _session = viewers;

        ToggleCommand = ReactiveCommand.Create(() => { IsVisible = !IsVisible; });

        // 対象のノードだけでなく、その内容の変化も追う。キャンバス側で書き換えても
        // ビューアに映り、Undo で戻したときも同じ経路で戻る。
        var content = this
            .WhenAnyValue(x => x.Node)
            .Select(node => node is null
                ? Observable.Return((Title: string.Empty, Body: string.Empty, Link: string.Empty))
                : node.WhenAnyValue(
                    x => x.Title,
                    x => x.Body,
                    x => x.Link,
                    (title, body, link) => (Title: title, Body: body, Link: link)))
            .Switch();

        var request = Observable.CombineLatest(
            content,
            this.WhenAnyValue(x => x.SourceKind),
            (c, kind) => (c.Title, c.Body, c.Link, Kind: kind));

        // 見出しと本文は待たずに反映する。ここに待ちを入れると、ノードを移った直後の
        // わずかな間だけ古い本文が残り、そこへ打ち込むと移る前のノードの内容が
        // 新しいノードに書き込まれてしまう。
        request.Subscribe(r => ApplyImmediate(r.Title, r.Body, r.Link, r.Kind));

        // リンク先はファイルを読むので、選択が落ち着いてから。
        // 前の読み込みは Switch が打ち切るので、古い中身が後から出てくることはない。
        request
            .Where(r => r.Kind == ViewerSource.Link)
            .Throttle(SettleDelay, RxApp.MainThreadScheduler)
            .Select(r => ResolveTarget(r.Link) is { Path: { } path } target
                ? Observable.FromAsync(ct => _session.ShowAsync(path, target.IsDirectory, ct))
                : Observable.Empty<object>())
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplyLoaded);
    }

    /// <summary>見せる対象のノード。ウィンドウ側が選択に合わせて差し替える。</summary>
    public NodeViewModel? Node
    {
        get => _node;
        private set => this.RaiseAndSetIfChanged(ref _node, value);
    }

    /// <summary>本文とリンクのどちらを見るか。利用者が選ぶ。</summary>
    public ViewerSource SourceKind
    {
        get => _sourceKind;
        set
        {
            // 本文欄から離れるので、ここまでの編集を 1 回ぶんとして確定させる。
            EndEdit();
            this.RaiseAndSetIfChanged(ref _sourceKind, value);
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!value)
            {
                EndEdit();
            }

            this.RaiseAndSetIfChanged(ref _isVisible, value);
        }
    }

    /// <summary>ビューアの幅。たたんでいる間も覚えておき、開き直したら元の広さに戻す。</summary>
    public double Width
    {
        get => _width;
        private set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    /// <summary>
    /// タブの下に出す手がかり。本文ならノードの見出し、リンクならその場所。
    /// リンクのタブ名はファイル名だけに縮めてあるので、全体はここで見せる。
    /// </summary>
    public string Caption
    {
        get => _caption;
        private set
        {
            this.RaiseAndSetIfChanged(ref _caption, value);
            this.RaisePropertyChanged(nameof(HasCaption));
        }
    }

    public bool HasCaption => !string.IsNullOrEmpty(_caption);

    /// <summary>リンクタブの見出し。何を指しているか分かるようファイル名を出す。</summary>
    public string TabLabel
    {
        get => _tabLabel;
        private set => this.RaiseAndSetIfChanged(ref _tabLabel, value);
    }

    /// <summary>
    /// 本文。ここへの書き込みはそのまま選択中のノードの本文になる。
    /// ノード側の変化はまた <see cref="ApplyImmediate"/> で戻ってくるが、同じ値になるので
    /// 通知は起きず、入力中に文字の位置がずれることはない。
    /// </summary>
    public string Body
    {
        get => _body;
        set
        {
            this.RaiseAndSetIfChanged(ref _body, value);

            if (Node is { } node)
            {
                node.Body = value;
            }
        }
    }

    /// <summary>
    /// 選択中のノードのリンクそのもの。
    /// 名前を <see cref="NodeViewModel.Link"/> に揃えてあるのは、リンクの絵柄を出すスタイルを
    /// ノードの脇とビューアで使い回すため（どちらも DataContext の Link を見る）。
    /// </summary>
    public string Link
    {
        get => _link;
        private set => this.RaiseAndSetIfChanged(ref _link, value);
    }

    /// <summary>
    /// リンク先を描いている画面。種類ごとのビューアが作ったものをそのまま載せる。
    /// ViewModel が画面の型を持つのは行儀が良くないが、差し替えの単位が
    /// 「描画そのもの」である以上、ここを文字列に落とすと差し替えられなくなる。
    /// </summary>
    public object? LinkView
    {
        get => _linkView;
        private set => this.RaiseAndSetIfChanged(ref _linkView, value);
    }

    /// <summary>中身の代わりに出す案内。出すものがあるときは null。</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            this.RaiseAndSetIfChanged(ref _message, value);
            this.RaisePropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message is not null;

    /// <summary>本文の入力欄を出すか。</summary>
    public bool ShowsEditor => _message is null && _sourceKind == ViewerSource.Body;

    /// <summary>リンク先の画面を出すか。</summary>
    public bool ShowsLink => _message is null && _sourceKind == ViewerSource.Link;

    /// <summary>
    /// 「リンク先を開く」を出すか。開ける先があるときは常に出す。
    ///
    /// ビューアで出せなかったときだけに絞っていたが、失敗の理由はビューア自身が
    /// 自分の枠の中に出すようになったので、ホスト側からは成否が見えない。
    /// 出せているときに押せても害は無いので、開ける先があるかどうかだけで決める。
    /// </summary>
    public bool ShowsOpenLink => _canOpenLink && _sourceKind == ViewerSource.Link;

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    /// <summary>選択が変わったときにウィンドウ側から呼ぶ。</summary>
    public void SetTarget(DocumentViewModel? document, NodeViewModel? node)
    {
        // 別のノードに移る前に、ここまでの編集を確定させる。
        EndEdit();

        _document = document;
        Node = node;
    }

    /// <summary>本文欄に入ったときに呼ぶ。ここから抜けるまでを 1 回の Undo にまとめる。</summary>
    public void BeginEdit()
    {
        if (_editing is not null || _document is not { } document || Node is not { } node)
        {
            return;
        }

        document.BeginExternalEdit(node);
        _editing = (document, node);
    }

    /// <summary>本文欄から抜けたときに呼ぶ。何も編集していなければ何も起きない。</summary>
    public void EndEdit()
    {
        if (_editing is not { } editing)
        {
            return;
        }

        _editing = null;
        editing.Document.EndExternalEdit(editing.Node);
    }

    /// <summary>スプリッターの操作で幅を変える。キャンバスが潰れないところで止める。</summary>
    public void Resize(double width, double availableWidth)
    {
        var max = Math.Max(MinWidth, availableWidth - MinCanvasWidth);
        Width = Math.Clamp(width, MinWidth, max);
    }

    private void ApplyImmediate(string title, string body, string link, ViewerSource kind)
    {
        TabLabel = BuildTabLabel(link);
        Link = link.Trim();

        if (Node is null)
        {
            Caption = string.Empty;
            Body = string.Empty;
            Link = string.Empty;
            LinkView = null;
            Message = NoTargetMessage;
            _canOpenLink = false;
        }
        else if (kind == ViewerSource.Body)
        {
            Caption = title;
            Body = body;

            // 本文は編集できるので、空でも入力欄を出す
            // （案内文に差し替えると、書き始めることができなくなる）。
            Message = null;
            _canOpenLink = false;
        }
        else
        {
            var resolution = ResolveTarget(link);

            Caption = resolution.Path ?? Link;
            Body = body;

            // 前のファイルの画面が残らないよう、読み直す前に外す。
            // 読める場合の画面は、少し遅れて ApplyLoaded から入る。
            LinkView = null;
            Message = resolution.Message;
            _canOpenLink = resolution.CanOpen;
        }

        RaiseVisibilityFlags();
    }

    private void ApplyLoaded(object view)
    {
        // 読んでいる間に本文へ切り替えられていたら、載せる先が無いので捨てる。
        if (SourceKind != ViewerSource.Link)
        {
            return;
        }

        LinkView = view;
        Message = null;
        RaiseVisibilityFlags();
    }

    private void RaiseVisibilityFlags()
    {
        this.RaisePropertyChanged(nameof(ShowsEditor));
        this.RaisePropertyChanged(nameof(ShowsLink));
        this.RaisePropertyChanged(nameof(ShowsOpenLink));
    }

    /// <summary>
    /// リンクが読める場所を指しているかを調べる。読めるならその絶対パス、
    /// 読めないなら理由を返す（ビューアを呼ぶ前に分かるものはここで片付ける）。
    /// </summary>
    private LinkResolution ResolveTarget(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return new LinkResolution(null, "このノードにはリンクがありません。", false);
        }

        var trimmed = link.Trim();

        // ファイル以外は、外のアプリに渡せば開ける。ビューアでは出せないことだけ伝える。
        switch (LinkClassifier.Classify(trimmed))
        {
            case LinkKind.Web:
                return new LinkResolution(null, "Web のリンクを出せるビューアがまだありません。", true);
            case LinkKind.Mail:
                return new LinkResolution(null, "メールのリンクは表示できません。", true);
        }

        if (ResolvePath(trimmed) is not { } path)
        {
            return new LinkResolution(
                null, "リンクの場所を特定できません。相対パスのリンクは、マップを保存してから使えます。", false);
        }

        // フォルダーもビューアに渡す。中身の一覧は組み込みが出すが、
        // 別の見せ方をするパッケージがあればそちらが勝つ。
        if (Directory.Exists(path))
        {
            return new LinkResolution(path, null, true, IsDirectory: true);
        }

        return File.Exists(path)
            ? new LinkResolution(path, null, true)
            : new LinkResolution(null, "ファイルが見つかりません。", false);
    }

    /// <summary>
    /// リンクを実際のパスに直す。解き方は <see cref="LinkPathResolver"/> にまとめてある
    /// （ノードのサムネイルも同じ解き方をする必要があるため）。
    /// </summary>
    private string? ResolvePath(string link) =>
        LinkPathResolver.Resolve(link, _document?.CurrentFilePath);

    /// <summary>
    /// リンクタブの見出し。ファイルならファイル名、Web ならホスト名。
    /// 長いものは末尾を省く（全体は <see cref="Caption"/> に出る）。
    /// </summary>
    private static string BuildTabLabel(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return NoLinkLabel;
        }

        var trimmed = link.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return uri.Scheme is "http" or "https" ? Shorten(uri.Host) : NoLinkLabel;
        }

        try
        {
            // フォルダーのように末尾が区切りで終わるものは、その手前を名前として拾う。
            var name = Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? NoLinkLabel : Shorten(name);
        }
        catch (ArgumentException)
        {
            return NoLinkLabel;
        }
    }

    private static string Shorten(string text) =>
        text.Length <= MaxTabLabelLength ? text : text[..(MaxTabLabelLength - 1)] + "…";

    /// <summary>
    /// リンクの行き先を調べた結果。<see cref="Path"/> が入っていればビューアで出せる。
    /// 出せないときは <see cref="Message"/> に理由が入り、外のアプリでなら開けるものは
    /// <see cref="CanOpen"/> が true になる。
    /// </summary>
    private sealed record LinkResolution(
        string? Path,
        string? Message,
        bool CanOpen,
        bool IsDirectory = false);
}
