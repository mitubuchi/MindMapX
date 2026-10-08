using Avalonia;
using System.Globalization;
using Avalonia.Data;
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

    /// <summary>子ノードを隠している間は「開く」、出している間は「畳む」印。</summary>
    public static readonly FuncValueConverter<bool, Geometry> ChildrenGlyph =
        new(hidden => hidden ? Icons.ExpandChildren : Icons.CollapseChildren);

    /// <summary>
    /// 列挙の値が ConverterParameter に並べた名前（"Folder|File" のように | 区切り）のどれかか。
    /// 種類ごとに入力欄を出し分けるのに使う（WPF 版の DataTrigger にあたる）。
    /// </summary>
    public static readonly IValueConverter EnumIn = new FuncValueConverter<object?, string?, bool>(
        (value, names) => value is not null && names is not null && names.Split('|').Contains(value.ToString()));

    /// <summary><see cref="EnumIn"/> の反対。</summary>
    public static readonly IValueConverter EnumIsNot = new FuncValueConverter<object?, string?, bool>(
        (value, names) => value is null || names is null || !names.Split('|').Contains(value.ToString()));

    /// <summary>展開中は「たたむ」、たたみ中は「広げる」印。</summary>
    public static readonly FuncValueConverter<bool, Geometry> CollapseGlyph =
        new(collapsed => collapsed ? Icons.ChevronDown : Icons.ChevronUp);

    /// <summary>
    /// 列挙の値が ConverterParameter（名前）と同じか。RadioButton の IsChecked を
    /// 列挙のプロパティにつなぐのに使う（選ばれたら、その名前の値を書き戻す）。
    /// </summary>
    public static readonly IValueConverter EnumEquals = new EnumEqualsConverter();

    /// <summary>選択中のノードは重なり順を上げ、他のノードに隠れないようにする。</summary>
    public static readonly FuncValueConverter<bool, int> SelectedZIndex = new(selected => selected ? 1000 : 0);

    /// <summary>
    /// ノードの倍率（祖先の倍率を掛け合わせたもの）。X と Y を 1 つの拡大にまとめる。
    /// 倍率 1 のノードには変形を付けない（描画のたびに無駄な変形を挟まないため）。
    /// </summary>
    public static readonly FuncMultiValueConverter<double, ITransform?> WorldScale = new(values =>
    {
        var pair = values.ToArray();
        if (pair.Length != 2 || (Math.Abs(pair[0] - 1) < 1e-9 && Math.Abs(pair[1] - 1) < 1e-9))
        {
            return null;
        }

        return new ScaleTransform(pair[0], pair[1]);
    });

    /// <summary>接続線の端点。X と Y を 1 つの点にまとめる。</summary>
    public static readonly FuncMultiValueConverter<double, Point> ToPoint = new(values =>
    {
        var pair = values.ToArray();
        return pair.Length == 2 ? new Point(pair[0], pair[1]) : default;
    });

    private sealed class EnumEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not null && parameter is string name && value.ToString() == name;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            // 選ばれたほうだけが書き戻す。外れた側（false）まで書くと、選んだ値を上書きしてしまう。
            value is true && parameter is string name
                ? Enum.Parse(targetType, name)
                : BindingOperations.DoNothing;
    }
}
