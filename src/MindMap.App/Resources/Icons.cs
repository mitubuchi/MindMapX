using Avalonia.Media;

namespace MindMap.Resources;

/// <summary>
/// アプリで使うアイコンの線画。すべて 24x24 を基準にした線で、塗りは使わない。
/// 小さく表示しても形が潰れにくく、色を 1 か所で変えられるため。
/// XAML からは <c>{x:Static res:Icons.New}</c> のように参照する。
/// </summary>
public static class Icons
{
    public static readonly Geometry New = Parse("M7,3 H14 L19,8 V21 H7 Z M14,3 V8 H19");

    public static readonly Geometry Open = Parse("M3,7 H10 L12,9 H21 V20 H3 Z");

    public static readonly Geometry Save = Parse("M4,4 H16 L20,8 V20 H4 Z M8,4 V9 H15 V4 M7,20 V14 H17 V20");

    /// <summary>名前を付けて保存：受け皿に矢印を落とす形</summary>
    public static readonly Geometry SaveAs = Parse("M12,3 V15 M7.5,10.5 L12,15 L16.5,10.5 M4,18 V21 H20 V18");

    /// <summary>すべて保存：保存の形を 2 枚重ねて「複数まとめて」を表す</summary>
    public static readonly Geometry SaveAll =
        Parse("M8,3 H20 V16 H17 M3,7 H14 L17,10 V21 H3 Z M7,7 V11 H13 V7 M6,21 V16 H14 V21");

    public static readonly Geometry Undo = Parse("M8,8 H14 A5,5 0 0 1 14,18 H9 M8,8 L11.5,4.5 M8,8 L11.5,11.5");

    public static readonly Geometry Redo = Parse("M16,8 H10 A5,5 0 0 0 10,18 H15 M16,8 L12.5,4.5 M16,8 L12.5,11.5");

    /// <summary>子ノード：横に枝を伸ばした先に + の付いた箱</summary>
    public static readonly Geometry AddChild =
        Parse("M3,9 H9 V15 H3 Z M9,12 H15 M15,7 H21 V17 H15 Z M18,10 V14 M16,12 H20");

    /// <summary>兄弟ノード：下に枝を伸ばした先に + の付いた箱</summary>
    public static readonly Geometry AddSibling =
        Parse("M8,3 H16 V8 H8 Z M12,8 V13 M6,13 H18 V21 H6 Z M12,15 V19 M10,17 H14");

    /// <summary>切り取り：交差する 2 枚の刃と、下の 2 つの持ち手</summary>
    public static readonly Geometry Cut =
        Parse("M6,3 L15,16 M18,3 L9,16 M4,19.5 A2.5,2.5 0 1 0 9,19.5 A2.5,2.5 0 1 0 4,19.5 " +
              "M15,19.5 A2.5,2.5 0 1 0 20,19.5 A2.5,2.5 0 1 0 15,19.5");

    /// <summary>コピー：同じ紙が 2 枚重なった形</summary>
    public static readonly Geometry Copy = Parse("M9,3 H20 V15 H16 M4,8 H16 V21 H4 Z");

    /// <summary>貼り付け：クリップボードに紙をはさんだ形</summary>
    public static readonly Geometry Paste =
        Parse("M9,4 H5 V21 H19 V4 H15 M9,2.5 H15 V6 H9 Z M8,11 H16 M8,15 H16");

    public static readonly Geometry Edit = Parse("M4,20 V16 L16,4 L20,8 L8,20 Z M14,6 L18,10");

    public static readonly Geometry Delete =
        Parse("M4,7 H20 M9,7 V4 H15 V7 M6,7 V20 H18 V7 M10,11 V17 M14,11 V17");

    public static readonly Geometry ZoomOut = Parse("M11,4 A7,7 0 1 0 11,18 A7,7 0 1 0 11,4 M16,16 L21,21 M8,11 H14");

    public static readonly Geometry ZoomIn =
        Parse("M11,4 A7,7 0 1 0 11,18 A7,7 0 1 0 11,4 M16,16 L21,21 M8,11 H14 M11,8 V14");

    /// <summary>等倍に戻す：枠の中に枠</summary>
    public static readonly Geometry ZoomReset = Parse("M4,5 H20 V19 H4 Z M9,9 H15 V15 H9 Z");

    public static readonly Geometry Close = Parse("M5,5 L15,15 M15,5 L5,15");

    /// <summary>リンク：枠の外に飛び出す矢印。Web のリンクにも使う。</summary>
    public static readonly Geometry Link = Parse("M13,4 H20 V11 M20,4 L11,13 M17,14 V20 H4 V7 H10");

    /// <summary>種類が分からないリンク：疑問符に、リンクの矢印を添えた形</summary>
    public static readonly Geometry Unknown =
        Parse("M6.8,8.4 A3.3,3.3 0 1 1 10.1,11.9 V13.8 M10.1,16.9 V17 M15.5,3.5 H20.5 V8.5 M20.5,3.5 L15.8,8.2");

    /// <summary>メール：封筒</summary>
    public static readonly Geometry Mail = Parse("M3,6 H21 V18 H3 Z M3.5,6.5 L12,13 L20.5,6.5");

    /// <summary>フォルダー：見出しの付いた四角</summary>
    public static readonly Geometry Folder = Parse("M3,6 H9.5 L11.5,8.5 H21 V19 H3 Z");

    /// <summary>マインドマップ：中心から 2 本の枝が伸びて子ノードにつながる形</summary>
    public static readonly Geometry MindMap =
        Parse("M2,9 H8 V15 H2 Z M8,12 H12 M12,6 V18 M12,6 H16 M12,18 H16 " +
              "M16,3.5 H22 V8.5 H16 Z M16,15.5 H22 V20.5 H16 Z");

    /// <summary>
    /// ファイル：角を折った紙。関連付けられたアプリのアイコンを引けない環境
    /// （macOS / Linux）で、ファイルへのリンクに使う。
    /// </summary>
    public static readonly Geometry File = Parse("M6,3 H14 L18,7 V21 H6 Z M14,3 V7 H18 M9,12 H15 M9,16 H15");

    /// <summary>子ノードを縦に整列：左の親から、右へ縦に並ぶ線</summary>
    public static readonly Geometry ArrangeVertical = Parse("M3,10 H7 V14 H3 Z M11,5 H21 M11,12 H21 M11,19 H21");

    /// <summary>子ノードを横に整列：上の親から、下へ横に並ぶ線</summary>
    public static readonly Geometry ArrangeHorizontal = Parse("M10,3 H14 V7 H10 Z M5,11 V21 M12,11 V21 M19,11 V21");

    /// <summary>設定：歯車</summary>
    public static readonly Geometry Settings = Parse(
        "M18.94,9.74 L22.24,10.19 A10.4,10.4 0 0 1 22.24,13.81 L18.94,14.26 A7.3,7.3 0 0 1 18.5,15.31 " +
        "L20.52,17.97 A10.4,10.4 0 0 1 17.97,20.52 L15.31,18.5 A7.3,7.3 0 0 1 14.26,18.94 " +
        "L13.81,22.24 A10.4,10.4 0 0 1 10.19,22.24 L9.74,18.94 A7.3,7.3 0 0 1 8.69,18.5 " +
        "L6.03,20.52 A10.4,10.4 0 0 1 3.48,17.97 L5.5,15.31 A7.3,7.3 0 0 1 5.06,14.26 " +
        "L1.76,13.81 A10.4,10.4 0 0 1 1.76,10.19 L5.06,9.74 A7.3,7.3 0 0 1 5.5,8.69 " +
        "L3.48,6.03 A10.4,10.4 0 0 1 6.03,3.48 L8.69,5.5 A7.3,7.3 0 0 1 9.74,5.06 " +
        "L10.19,1.76 A10.4,10.4 0 0 1 13.81,1.76 L14.26,5.06 A7.3,7.3 0 0 1 15.31,5.5 " +
        "L17.97,3.48 A10.4,10.4 0 0 1 20.52,6.03 L18.5,8.69 A7.3,7.3 0 0 1 18.94,9.74 Z " +
        "M8.6,12.0 A3.4,3.4 0 0 1 15.4,12.0 A3.4,3.4 0 0 1 8.6,12.0 Z");

    /// <summary>子ノードを畳む：丸に横棒</summary>
    public static readonly Geometry CollapseChildren = Parse("M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 M8.5,12 H15.5");

    /// <summary>畳んだ子ノードを開く：丸に十字</summary>
    public static readonly Geometry ExpandChildren =
        Parse("M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 M8.5,12 H15.5 M12,8.5 V15.5");

    /// <summary>ビューア：右側に欄を持つ窓</summary>
    public static readonly Geometry Viewer = Parse("M3,5 H21 V19 H3 Z M14,5 V19 M16,9 H19 M16,12 H19 M16,15 H18");

    /// <summary>展開中に出す「小さくたたむ」印（上向き山）。</summary>
    public static readonly Geometry ChevronUp = Parse("M6,15 L12,9 L18,15");

    /// <summary>たたんだときに出す「広げる」印（下向き山）。</summary>
    public static readonly Geometry ChevronDown = Parse("M6,9 L12,15 L18,9");

    private static Geometry Parse(string figures) => StreamGeometry.Parse(figures);
}
