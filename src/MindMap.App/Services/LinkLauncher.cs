using System.ComponentModel;
using System.Diagnostics;

namespace MindMap.Services;

/// <summary>
/// URL やファイルを OS に渡して開いてもらう。開き方は OS ごとに違うので、ここで吸収する。
/// </summary>
public static class LinkLauncher
{
    /// <summary>関連付けが無いファイルを開こうとしたときに Windows が返すコード。</summary>
    private const int NoAssociationErrorCode = 1155;

    /// <summary>開けなかったときは、その理由を文字列で返す（開けたときは null）。</summary>
    public static string? Open(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // UseShellExecute にすると、実行ではなく「開く」の既定の動作に委ねられる。
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", [target]);
            }
            else
            {
                // Linux などの freedesktop 系。既定のアプリに渡す標準の入口。
                Process.Start("xdg-open", [target]);
            }

            return null;
        }
        catch (Win32Exception ex) when (OperatingSystem.IsWindows() && ex.NativeErrorCode == NoAssociationErrorCode)
        {
            // 開くアプリが決まっていないので、Windows の「プログラムから開く」を出す。
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {target}")
                {
                    UseShellExecute = true,
                });

                return null;
            }
            catch (Exception inner)
            {
                return inner.Message;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
