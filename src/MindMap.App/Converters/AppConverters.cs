using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MindMap.Resources;
using MindMap.Services;

namespace MindMap.Converters;

/// <summary>
/// 画面で使う小さな変換のまとめ。XAML からは <c>{x:Static conv:AppConverters.LinkGlyph}</c> の形で参照する。
/// Avalonia には WPF の DataTrigger が無いので、見た目の出し分けはここで値に落とす。
/// </summary>
public static class AppConverters
{
    /// <summary>リンク先の種類に合った線画アイコン。</summary>
    public static readonly FuncValueConverter<string?, Geometry> LinkGlyph = new(link =>
        LinkClassifier.Classify(link) switch
        {
            LinkKind.MindMap => Icons.MindMap,
            LinkKind.Folder => Icons.Folder,
            LinkKind.Mail => Icons.Mail,
            LinkKind.File => Icons.File,

            // Web は行き先が特定のアプリに紐づかないので、そのままリンクの記号で表す。
            LinkKind.Web => Icons.Link,
            _ => Icons.Unknown,
        });

    /// <summary>
    /// ファイルへのリンクに出す、関連付けられたアプリのアイコン。
    /// 引けない種類・引けない OS では null になり、View 側は線画に切り替える。
    /// </summary>
    public static readonly FuncValueConverter<string?, IImage?> LinkIcon = new(LinkIcons.ForLink);

    /// <summary>展開中は「たたむ」、たたみ中は「広げる」印。</summary>
    public static readonly FuncValueConverter<bool, Geometry> CollapseGlyph =
        new(collapsed => collapsed ? Icons.ChevronDown : Icons.ChevronUp);

    /// <summary>選択中のノードは重なり順を上げ、他のノードに隠れないようにする。</summary>
    public static readonly FuncValueConverter<bool, int> SelectedZIndex = new(selected => selected ? 1000 : 0);

    /// <summary>接続線の端点。X と Y を 1 つの点にまとめる。</summary>
    public static readonly FuncMultiValueConverter<double, Point> ToPoint = new(values =>
    {
        var pair = values.ToArray();
        return pair.Length == 2 ? new Point(pair[0], pair[1]) : default;
    });
}
