using System.Text.Json;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>キャンバス上の 1 ノード。1 行のタイトルと、任意の複数行の内容を持つ。</summary>
public sealed class NodeViewModel : ReactiveObject
{
    /// <summary>実測値が入るまでの暫定サイズ。新しいノードの配置計算に使われる。</summary>
    public const double DefaultWidth = 168;

    public const double DefaultHeight = 56;

    /// <summary>
    /// 直接の子。<see cref="Parent"/> の出し入れに合わせて自動で保たれる。
    /// 置き方の変更を子孫へ伝えるために要る（親を動かすと部分木ごと動くため）。
    /// </summary>
    private readonly List<NodeViewModel> _children = new();

    private NodeViewModel? _parent;

    private string _title;
    private string _body;
    private string _link;

    /// <summary>親からの相対位置。ルートならキャンバスの絶対位置と同じ。</summary>
    private double _localX;
    private double _localY;
    private double _localZ;

    /// <summary>親を基準とした倍率。1 が等倍。</summary>
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _scaleZ = 1;

    private double _width = DefaultWidth;
    private double _height = DefaultHeight;
    private bool _isSelected;
    private bool _isEditing;
    // 新しいノードは本文を畳んだ状態で始める（既定値の意味は IsCollapsed を参照）。
    private bool _isCollapsed = true;
    private bool _areChildrenHidden;
    private bool _hasChildren;
    private object? _thumbnail;

    public NodeViewModel(Guid id, string title, string body, double x, double y, string link = "")
    {
        Id = id;
        _title = title;
        _body = body;
        _link = link;

        // 親がまだ無いので、渡された値はそのまま相対位置になる。
        // 親を付けたときに、世界での位置を保ったまま相対位置へ組み直される。
        _localX = x;
        _localY = y;

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

    /// <summary>
    /// ルートノードなら null。
    ///
    /// 付け替えても<b>世界での位置と大きさは変わらない</b>。相対位置と倍率のほうを計算し直す
    /// （別の親にドロップしただけでノードが飛んでいくと、何が起きたのか分からなくなるため）。
    /// </summary>
    public NodeViewModel? Parent
    {
        get => _parent;
        set
        {
            if (ReferenceEquals(_parent, value))
            {
                return;
            }

            var worldX = X;
            var worldY = Y;
            var worldScaleX = WorldScaleX;
            var worldScaleY = WorldScaleY;
            var worldScaleZ = WorldScaleZ;

            _parent?._children.Remove(this);
            _parent = value;
            _parent?._children.Add(this);

            SetWorldScale(worldScaleX, worldScaleY, worldScaleZ);
            SetWorldPosition(worldX, worldY);
            NotifyVisibilityChanged();
        }
    }

    /// <summary>直接の子。<see cref="Parent"/> が保つので、外から出し入れはしない。</summary>
    public IReadOnlyList<NodeViewModel> Children => _children;

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
            this.RaisePropertyChanged(nameof(HasBodyContent));
            this.RaisePropertyChanged(nameof(IsBodyVisible));
        }
    }

    /// <summary>本文が空なら、表示のときは本文欄と区切り線を出さない。</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(_body);

    /// <summary>
    /// リンク先から作った小さな絵。作れないリンク（画像でも動画でもない、
    /// 扱えるパッケージが入っていない）では null のまま。
    ///
    /// ファイルには保存しない。リンク先から作り直せるものなので、マップに焼き込まない。
    /// </summary>
    public object? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _thumbnail, value);
            this.RaisePropertyChanged(nameof(HasThumbnail));
            this.RaisePropertyChanged(nameof(HasBodyContent));
            this.RaisePropertyChanged(nameof(IsBodyVisible));
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    /// <summary>
    /// 本文欄に出すものがあるか。文章か、リンク先の絵。
    ///
    /// 絵は<b>本文の代わり</b>に本文欄へ出るので、表示・非表示の切り替えも
    /// たたんだときの見た目も本文とまったく同じに扱う（ボタンが 2 つに増えない）。
    /// </summary>
    public bool HasBodyContent => HasBody || HasThumbnail;

    /// <summary>
    /// 小さく表示するかどうか。true の間はタイトル（とリンク）だけを見せ、本文と区切り線を隠す。
    /// ノードごとに切り替えられ、ファイルにも保存する。
    ///
    /// <b>新しく作るノードは true で始まる。</b> 本文は開いたときだけ見せたいため。
    /// 読み込み時は保存された値で必ず上書きされる（この欄は昔から必ず書き出されているので、
    /// 既存のファイルを開いても見た目は変わらない）。
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

    /// <summary>本文欄に出すものがあり、かつ小さく表示していないときだけ、本文欄と区切り線を出す。</summary>
    public bool IsBodyVisible => HasBodyContent && !_isCollapsed;

    /// <summary>
    /// 子ノードを畳んで隠すかどうか。<see cref="IsCollapsed"/>（本文を隠す）とは別物。
    /// 隠れるのは子だけでなく、その先の子孫すべて。
    /// </summary>
    public bool AreChildrenHidden
    {
        get => _areChildrenHidden;
        set
        {
            if (_areChildrenHidden == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _areChildrenHidden, value);

            foreach (var child in _children)
            {
                child.NotifyVisibilityChanged();
            }
        }
    }

    /// <summary>
    /// キャンバスに出すかどうか。祖先のどれかが子を畳んでいれば消える。
    ///
    /// ノードの一覧から外すのではなく見せないだけにしてあるのは、畳んでいる間も
    /// 位置や親子関係を保ち、開いたときに元どおりに戻すため。
    /// </summary>
    public bool IsVisible => _parent is null || (_parent.IsVisible && !_parent.AreChildrenHidden);

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

    /// <summary>親からの相対位置。ファイルに保存されるのはこちら。</summary>
    public double LocalX
    {
        get => _localX;
        set
        {
            if (_localX.Equals(value))
            {
                return;
            }

            _localX = value;
            NotifyTransformChanged();
        }
    }

    public double LocalY
    {
        get => _localY;
        set
        {
            if (_localY.Equals(value))
            {
                return;
            }

            _localY = value;
            NotifyTransformChanged();
        }
    }

    /// <summary>奥行き。2D の描画では使わないが、保存して持ち越す。</summary>
    public double LocalZ
    {
        get => _localZ;
        set => _localZ = value;
    }

    /// <summary>
    /// オイラー角（度）。2D の描画では<b>使わない</b>。保存して持ち越すだけで、
    /// 実際に使うのは 3D 側や Unity 側。
    /// </summary>
    public double RotationX { get; set; }

    public double RotationY { get; set; }

    public double RotationZ { get; set; }

    /// <summary>
    /// 親を基準とした倍率。親の倍率が子に乗るのは Unity と同じ
    /// （古い枝を親ごと縮める、といった見せ方がそのまま書ける）。
    /// </summary>
    public double ScaleX
    {
        get => _scaleX;
        set
        {
            if (_scaleX.Equals(value))
            {
                return;
            }

            _scaleX = value;
            NotifyTransformChanged();
        }
    }

    public double ScaleY
    {
        get => _scaleY;
        set
        {
            if (_scaleY.Equals(value))
            {
                return;
            }

            _scaleY = value;
            NotifyTransformChanged();
        }
    }

    /// <summary>奥行き方向の倍率。2D の描画では使わないが、保存して持ち越す。</summary>
    public double ScaleZ
    {
        get => _scaleZ;
        set => _scaleZ = value;
    }

    /// <summary>祖先ぶんを掛け合わせた倍率。描画と当たり判定はこれを見る。</summary>
    public double WorldScaleX => (_parent?.WorldScaleX ?? 1) * _scaleX;

    public double WorldScaleY => (_parent?.WorldScaleY ?? 1) * _scaleY;

    public double WorldScaleZ => (_parent?.WorldScaleZ ?? 1) * _scaleZ;

    /// <summary>
    /// キャンバス上の絶対位置。相対位置には親の倍率が掛かる（Unity と同じ）。
    ///
    /// 代入すると、世界での位置がその値になるよう相対位置のほうを計算し直す。
    /// ドラッグや配置の計算は絶対座標のまま書けるので、呼び出し側は相対か絶対かを意識しなくてよい。
    /// </summary>
    public double X
    {
        get => (_parent?.X ?? 0) + (_localX * (_parent?.WorldScaleX ?? 1));
        set => LocalX = Divide(value - (_parent?.X ?? 0), _parent?.WorldScaleX ?? 1);
    }

    public double Y
    {
        get => (_parent?.Y ?? 0) + (_localY * (_parent?.WorldScaleY ?? 1));
        set => LocalY = Divide(value - (_parent?.Y ?? 0), _parent?.WorldScaleY ?? 1);
    }

    /// <summary>
    /// View が実際に描画した大きさ。タイトルと本文の量で変わるため、保存はせず毎回測り直す。
    /// <b>倍率が掛かる前</b>の値なので、画面上の大きさが要る場面では
    /// <see cref="WorldWidth"/> / <see cref="WorldHeight"/> を見ること。
    /// </summary>
    public double Width
    {
        get => _width;
        set
        {
            if (_width.Equals(value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _width, value);
            this.RaisePropertyChanged(nameof(WorldWidth));
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (_height.Equals(value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _height, value);
            this.RaisePropertyChanged(nameof(WorldHeight));
        }
    }

    /// <summary>
    /// 倍率を掛けた、キャンバス上での大きさ。接続線の端点・範囲選択・ドラッグの制限はこちらを使う。
    ///
    /// 回転を使わない限りノードの外接矩形は軸に平行なままなので、
    /// (X, Y, WorldWidth, WorldHeight) がそのまま当たり判定になる。
    /// </summary>
    public double WorldWidth => _width * WorldScaleX;

    public double WorldHeight => _height * WorldScaleY;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// 読み込んだファイルにあった、このアプリが知らない欄。中身は解釈せず、
    /// 保存するときにそのまま書き戻すためだけに持ち歩く
    /// （<see cref="Models.MindMapNodeDto.Extra"/>）。画面には出さないので通知もしない。
    /// </summary>
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>true の間はテキストボックスに切り替わり、その場で編集できる。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    /// <summary>
    /// 見えているかどうかが変わったことを知らせる。祖先から降ってくる値なので、
    /// 置き方と同じく子孫までたどる。
    /// </summary>
    public void NotifyVisibilityChanged()
    {
        this.RaisePropertyChanged(nameof(IsVisible));

        foreach (var child in _children)
        {
            child.NotifyVisibilityChanged();
        }
    }

    /// <summary>自分が <paramref name="other"/> の子孫かどうか。</summary>
    public bool IsDescendantOf(NodeViewModel other)
    {
        for (var current = _parent; current is not null; current = current._parent)
        {
            if (ReferenceEquals(current, other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 相対位置をまとめて入れる。読み込みとレイアウトの適用で、
    /// 通知を 1 回にまとめたいときに使う。
    /// </summary>
    public void SetLocalPosition(double x, double y, double z)
    {
        _localX = x;
        _localY = y;
        _localZ = z;
        NotifyTransformChanged();
    }

    public void SetLocalScale(double x, double y, double z)
    {
        _scaleX = x;
        _scaleY = y;
        _scaleZ = z;
        NotifyTransformChanged();
    }

    /// <summary>世界での位置を指定して、相対位置のほうを計算し直す。</summary>
    public void SetWorldPosition(double x, double y)
    {
        _localX = Divide(x - (_parent?.X ?? 0), _parent?.WorldScaleX ?? 1);
        _localY = Divide(y - (_parent?.Y ?? 0), _parent?.WorldScaleY ?? 1);
        NotifyTransformChanged();
    }

    private void SetWorldScale(double x, double y, double z)
    {
        _scaleX = Divide(x, _parent?.WorldScaleX ?? 1);
        _scaleY = Divide(y, _parent?.WorldScaleY ?? 1);
        _scaleZ = Divide(z, _parent?.WorldScaleZ ?? 1);
    }

    /// <summary>
    /// 置き方が変わったことを知らせる。親の変化は子の世界での位置と大きさを変えるので、
    /// 子孫までたどって知らせる。
    /// </summary>
    private void NotifyTransformChanged()
    {
        this.RaisePropertyChanged(nameof(X));
        this.RaisePropertyChanged(nameof(Y));
        this.RaisePropertyChanged(nameof(WorldScaleX));
        this.RaisePropertyChanged(nameof(WorldScaleY));
        this.RaisePropertyChanged(nameof(WorldWidth));
        this.RaisePropertyChanged(nameof(WorldHeight));

        foreach (var child in _children)
        {
            child.NotifyTransformChanged();
        }
    }

    /// <summary>
    /// 倍率 0 の親がいても、位置が無限大や NaN にならないようにする。
    /// 0 は「見えなくする」指定として書けてしまうので、割り算のほうで受け止める。
    /// </summary>
    private static double Divide(double value, double divisor) =>
        divisor == 0 ? value : value / divisor;
}
