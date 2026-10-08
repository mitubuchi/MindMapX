namespace MindMap.Services;

/// <summary>
/// バイト数の表記。ノードの本文に書くときと、一覧の列に並べるときで
/// 詳しさが違うので 2 つ用意してある（単位の刻み方は同じにしたいので 1 か所にまとめた）。
/// </summary>
public static class ByteSize
{
    private static readonly string[] Units = ["バイト", "KB", "MB", "GB", "TB"];

    /// <summary>一覧の列に並べる用。短く出す（"1.2 KB"）。</summary>
    public static string Format(long bytes)
    {
        var (size, unit) = Scale(bytes);

        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {Units[unit]}";
    }

    /// <summary>本文に書く用。元のバイト数も添える（"1.2 KB (1,234 バイト)"）。</summary>
    public static string Describe(long bytes)
    {
        var (size, unit) = Scale(bytes);

        return unit == 0
            ? $"{bytes:N0} バイト"
            : $"{size:N1} {Units[unit]} ({bytes:N0} バイト)";
    }

    private static (double Size, int Unit) Scale(long bytes)
    {
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return (size, unit);
    }
}
