using Avalonia;
using Avalonia.ReactiveUI;

namespace MindMap.Desktop;

/// <summary>
/// Windows / macOS / Linux 共通の実行形。画面そのものは MindMap.App にあり、
/// ここは Avalonia を立ち上げるだけ。OS ごとの実行形はこの薄い層だけを差し替える。
/// </summary>
internal static class Program
{
    // Avalonia の初期化より前に、Avalonia や SynchronizationContext に依存する処理を書かないこと。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // デザイナーからも使われるので、この形のまま残す。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()

            // ReactiveUI の通知を UI スレッドに載せる（ViewModel が ReactiveUI で書かれているため）。
            .UseReactiveUI();
}
