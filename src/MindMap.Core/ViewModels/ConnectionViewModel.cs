using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace MindMap.ViewModels;

/// <summary>
/// 親ノードと子ノードを結ぶ線。両端の中心を結ぶので、位置だけでなく
/// テキストの行数によるサイズ変化にも追従する必要がある。
/// </summary>
public sealed class ConnectionViewModel : ReactiveObject, IDisposable
{
    private readonly IDisposable _subscription;

    public ConnectionViewModel(NodeViewModel parent, NodeViewModel child)
    {
        Parent = parent;
        Child = child;

        _subscription = Observable
            .Merge(Geometry(parent), Geometry(child))
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(X1));
                this.RaisePropertyChanged(nameof(Y1));
                this.RaisePropertyChanged(nameof(X2));
                this.RaisePropertyChanged(nameof(Y2));
            });
    }

    public NodeViewModel Parent { get; }

    public NodeViewModel Child { get; }

    public double X1 => Parent.X + Parent.Width / 2;

    public double Y1 => Parent.Y + Parent.Height / 2;

    public double X2 => Child.X + Child.Width / 2;

    public double Y2 => Child.Y + Child.Height / 2;

    public void Dispose() => _subscription.Dispose();

    private static IObservable<Unit> Geometry(NodeViewModel node) => node
        .WhenAnyValue(n => n.X, n => n.Y, n => n.Width, n => n.Height)
        .Select(_ => Unit.Default);
}
