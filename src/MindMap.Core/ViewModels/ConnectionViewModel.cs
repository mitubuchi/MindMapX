using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// 親ノードと子ノードを結ぶ線。両端の中心を結ぶので、位置だけでなく
/// テキストの行数によるサイズ変化にも追従する必要がある。
///
/// 大きさは倍率を掛けたあとの値（WorldWidth / WorldHeight）を見る。
/// 実測値のままだと、縮めたノードの外に線の端が飛び出す。
/// </summary>
public sealed class ConnectionViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable _subscription;

    public ConnectionViewModel(NodeViewModel parent, NodeViewModel child)
    {
        Parent = parent;
        Child = child;

        _subscription = new CompositeDisposable(
            Observable
                .Merge(Geometry(parent), Geometry(child))
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(X1));
                    this.RaisePropertyChanged(nameof(Y1));
                    this.RaisePropertyChanged(nameof(X2));
                    this.RaisePropertyChanged(nameof(Y2));
                }),

            // 子が見えているなら親も見えている（畳んだ祖先がいれば両方消える）ので、
            // 子のほうだけ見ていれば足りる。
            child
                .WhenAnyValue(n => n.IsVisible)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(IsVisible))));
    }

    public NodeViewModel Parent { get; }

    public NodeViewModel Child { get; }

    public double X1 => Parent.X + Parent.WorldWidth / 2;

    public double Y1 => Parent.Y + Parent.WorldHeight / 2;

    public double X2 => Child.X + Child.WorldWidth / 2;

    public double Y2 => Child.Y + Child.WorldHeight / 2;

    /// <summary>畳まれて子が消えている間は線も消す。</summary>
    public bool IsVisible => Child.IsVisible;

    public void Dispose() => _subscription.Dispose();

    private static IObservable<Unit> Geometry(NodeViewModel node) => node
        .WhenAnyValue(n => n.X, n => n.Y, n => n.WorldWidth, n => n.WorldHeight)
        .Select(_ => Unit.Default);
}
