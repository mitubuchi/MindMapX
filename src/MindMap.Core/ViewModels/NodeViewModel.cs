using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>キャンバス上の 1 ノード。1 行のタイトルと、任意の複数行の内容を持つ。</summary>
public sealed class NodeViewModel : ReactiveObject
{
    /// <summary>実測値が入るまでの暫定サイズ。新しいノードの配置計算に使われる。</summary>
    public const double DefaultWidth = 168;

    public const double DefaultHeight = 56;

    private string _title;
    private string _body;
    private string _link;
    private double _x;
    private double _y;
    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private bool _isSelected;
    private bool _isEditing;
    private bool _isCollapsed;
    private bool _hasChildren;

    public NodeViewModel(Guid id, string title, string body, double x, double y, string link = "")
    {
        Id = id;
        _title = title;
        _body = body;
        _link = link;
        _x = x;
        _y = y;

        // 新しいノードは今この瞬間を制作日とする。読み込み時は保存済みの値で上書きする。
        CreatedAt = DateTimeOffset.Now;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; }

    /// <summary>
    /// 制作日。UI には出さないが、後のデータ処理のためにファイルへ保存する。
    /// 一度決めたら変えない（読み込み時は保存済みの値を復元する）。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 更新日。UI には出さないが、内容（タイトル・本文・リンク）を変えるたびに現在時刻へ更新し、ファイルへ保存する。
    /// 位置の移動や表示の大小切り替えでは更新しない。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>ルートノードなら null。</summary>
    public NodeViewModel? Parent { get; set; }

    /// <summary>
    /// 子ノードがぶら下がっているかどうか。子の一覧はドキュメントが持っているので、
    /// ノードの出入りに合わせてドキュメント側が入れ直す。
    /// </summary>
    public bool HasChildren
    {
        get => _hasChildren;
        set => this.RaiseAndSetIfChanged(ref _hasChildren, value);
    }

    /// <summary>1 行の見出し。中央寄せで表示される。</summary>
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    /// <summary>改行を含められる本文。左寄せで表示され、空のときは表示自体を省く。</summary>
    public string Body
    {
        get => _body;
        set
        {
            if (_body == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _body, value);
            this.RaisePropertyChanged(nameof(HasBody));
            this.RaisePropertyChanged(nameof(IsBodyVisible));
        }
    }

    /// <summary>本文が空なら、表示のときは本文欄と区切り線を出さない。</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(_body);

    /// <summary>
    /// 小さく表示するかどうか。true の間はタイトル（とリンク）だけを見せ、本文と区切り線を隠す。
    /// ノードごとに切り替えられ、ファイルにも保存する。
    /// </summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isCollapsed, value);
            this.RaisePropertyChanged(nameof(IsBodyVisible));
        }
    }

    /// <summary>本文があり、かつ小さく表示していないときだけ本文欄と区切り線を出す。</summary>
    public bool IsBodyVisible => HasBody && !_isCollapsed;

    /// <summary>URL またはファイルパス。設定するとタイトルの脇にリンクのアイコンが出る。</summary>
    public string Link
    {
        get => _link;
        set
        {
            if (_link == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _link, value);
            this.RaisePropertyChanged(nameof(HasLink));
        }
    }

    public bool HasLink => !string.IsNullOrWhiteSpace(_link);

    public double X
    {
        get => _x;
        set => this.RaiseAndSetIfChanged(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => this.RaiseAndSetIfChanged(ref _y, value);
    }

    /// <summary>
    /// View が実際に描画した大きさ。タイトルと本文の量で変わるため、保存はせず毎回測り直す。
    /// 接続線の端点とドラッグの移動範囲がこれを見ている。
    /// </summary>
    public double Width
    {
        get => _width;
        set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => this.RaiseAndSetIfChanged(ref _height, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>true の間はテキストボックスに切り替わり、その場で編集できる。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }
}
