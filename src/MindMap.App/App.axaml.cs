using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MindMap.Services;
using MindMap.Services.Packages;
using MindMap.Services.Settings;
using MindMap.Services.Thumbnails;
using MindMap.Services.Viewers;
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
            // 設定は起動時に必ず読む。ファイルが無ければ既定値で作る（何が設定できるのかを、
            // config.txt を開くだけで分かるようにするため）。リンクの解き方が Root Path を
            // 見るので、マップを 1 つでも開く前に済ませておく必要がある。
            SettingsService.Load();

            // BOM の無いテキストを Shift_JIS などで読み直せるようにする。
            // .NET では既定で UTF-8 系しか引けないので、ビューアが使う前にここで足しておく。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Windows では、関連付けられたアプリのアイコンをリンクに出せる。
            // 他の OS では線画のアイコンで代用する（LinkIcons.Current は null のまま）。
            if (OperatingSystem.IsWindows())
            {
                LinkIcons.Current = new WindowsLinkIconProvider();
            }

            // 提供物の置き場。種類ごとに 1 つずつ用意して、plugins のパッケージから配ってもらう。
            // 表示（ビューア）は組み込みぶんを持って始まり、サムネイルは空から始まる。
            var viewers = new ViewerRegistry();
            var thumbnails = new ThumbnailRegistry();
            var packages = PackageLoader.LoadAll(viewers, thumbnails);

            var window = new MainWindow();

            // クリップボードはウィンドウにぶら下がるので、都度そこから取れるように渡す。
            var viewModel = new MainWindowViewModel(
                new ViewerSession(viewers),
                new NodeThumbnailService(thumbnails),
                new AvaloniaNodeClipboard(() => window.Clipboard));

            window.DataContext = viewModel;
            window.RegisterInteractionHandlers(viewModel);

            // 壊れたパッケージは黙って無視せず 1 度だけ知らせる（入れたのに効かない状態が
            // いちばん分かりにくいため）。読めたぶんはそのまま使える。
            // ダイアログの表示先が要るので、ウィンドウが出てから出す。
            if (packages.Errors.Count > 0)
            {
                window.Opened += async (_, _) => await MessageDialog.ShowAsync(
                    window,
                    "パッケージを読み込めませんでした。\n\n" + string.Join(Environment.NewLine, packages.Errors),
                    "OK");
            }

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
