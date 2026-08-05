using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MindMap.Services;
using MindMap.ViewModels;
using MindMap.Views;

namespace MindMap;

/// <summary>
/// アプリの入り口。どの OS でも共通の組み立てをここで行い、
/// OS ごとに違う部分（クリップボード・アイコンの取り出し方）だけを差し込む。
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Windows では、関連付けられたアプリのアイコンをリンクに出せる。
            // 他の OS では線画のアイコンで代用する（LinkIcons.Current は null のまま）。
            if (OperatingSystem.IsWindows())
            {
                LinkIcons.Current = new WindowsLinkIconProvider();
            }

            var window = new MainWindow();

            // クリップボードはウィンドウにぶら下がるので、都度そこから取れるように渡す。
            var viewModel = new MainWindowViewModel(new AvaloniaNodeClipboard(() => window.Clipboard));

            window.DataContext = viewModel;
            window.RegisterInteractionHandlers(viewModel);

            // 拡張子の関連付けやコマンドラインから渡されたファイルを開く。
            if (desktop.Args is { Length: > 0 } args)
            {
                viewModel.OpenFiles(args);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
