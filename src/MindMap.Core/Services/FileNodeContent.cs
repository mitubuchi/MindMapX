using System.IO;

namespace MindMap.Services;

/// <summary>
/// ドロップされたファイル（フォルダー）から、ノードに入れる内容を組み立てる。
/// View から切り離してあるのは、文面の組み立てを単体で確かめられるようにするため。
/// </summary>
public sealed record FileNodeContent(
    string Title,
    string Body,
    string Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    private const string DateFormat = "yyyy/MM/dd HH:mm:ss";

    /// <summary>
    /// 画像・映像・音声の大きさや撮影時の設定を、本文の行にして返す係。
    /// 読み方が OS ごとに違う（Windows はプロパティシステム）ので、起動時に UI 側が差し込む。
    /// 差し込まれていない OS では、その行が増えないだけ。
    /// </summary>
    public static Func<string, IEnumerable<string>>? DescribeMedia { get; set; }

    /// <summary>
    /// パスから内容を作る。存在しない、または読めない場合は null。
    /// 属性が読めなくてもノード自体は作れるよう、日時とサイズだけを諦める作りにしている。
    /// </summary>
    public static FileNodeContent? TryCreate(string path)
    {
        try
        {
            var isDirectory = Directory.Exists(path);
            if (!isDirectory && !File.Exists(path))
            {
                return null;
            }

            FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);

            // フォルダーには「サイズ」が無いので、行そのものを出さない
            // （中身を数え上げると大きなフォルダーで固まるため、あえて集計しない）。
            var lines = new List<string>
            {
                $"場所: {Path.GetDirectoryName(path) ?? path}",
            };

            if (info is FileInfo file)
            {
                lines.Add($"サイズ: {ByteSize.Describe(file.Length)}");

                // 画像・映像・音声なら、大きさや撮影時の設定を続けて並べる。
                // 持っていないファイルでは 1 行も増えない。
                if (DescribeMedia is { } describe)
                {
                    lines.AddRange(describe(path));
                }
            }
            else
            {
                lines.Add("種類: フォルダー");
            }

            lines.Add($"作成日時: {info.CreationTime.ToString(DateFormat)}");
            lines.Add($"更新日時: {info.LastWriteTime.ToString(DateFormat)}");
            lines.Add($"アクセス日時: {info.LastAccessTime.ToString(DateFormat)}");

            // ノードの制作日・更新日も、ファイル自身の日時に合わせる。
            return new FileNodeContent(
                Title: isDirectory ? new DirectoryInfo(path).Name : Path.GetFileName(path),
                Body: string.Join(Environment.NewLine, lines),
                Link: path,
                CreatedAt: new DateTimeOffset(info.CreationTime),
                UpdatedAt: new DateTimeOffset(info.LastWriteTime));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 権限が無い・パスが壊れているなど。ノードは作らず黙って捨てる。
            return null;
        }
    }

}
