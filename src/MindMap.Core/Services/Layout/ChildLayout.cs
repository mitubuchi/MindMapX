namespace MindMap.Services.Layout;

/// <summary>子ノードの並べ方。</summary>
public enum LayoutOrientation
{
    /// <summary>親の右に、縦一列に積む。</summary>
    Vertical,

    /// <summary>親の下に、横一列に並べる。</summary>
    Horizontal,
}

/// <summary>
/// 直接の子だけを 1 列に並べる位置を決める。
///
/// ここには画面も ViewModel も出てこない。入れた数値から位置が決まるだけなので、
/// 画面を出さずに実測できる。並べた結果をノードへ入れる・1 回の Undo にまとめるのは
/// 呼ぶ側（<c>DocumentViewModel</c>）の仕事。
///
/// 大きさは<b>倍率を掛けたあと</b>の値（WorldWidth / WorldHeight）を渡すこと。
/// 縮めた子は画面上でも小さいので、実測値のまま渡すと間が空きすぎる。
/// </summary>
public static class ChildLayout
{
    /// <param name="children">並べる順に並べた、子の画面上の大きさ。</param>
    /// <returns>子ごとの、キャンバス上の絶対位置（左上）。</returns>
    public static List<(double X, double Y)> Arrange(
        LayoutOrientation orientation,
        double parentX,
        double parentY,
        double parentWidth,
        double parentHeight,
        IReadOnlyList<(double Width, double Height)> children,
        double horizontalGap,
        double verticalGap)
    {
        var result = new List<(double X, double Y)>(children.Count);
        if (children.Count == 0)
        {
            return result;
        }

        if (orientation == LayoutOrientation.Vertical)
        {
            var x = parentX + parentWidth + horizontalGap;

            // 列の高さの合計が親の中心に来るようにする（上端を揃えるより、
            // 親から線が左右対称に出るぶん、どこに繋がっているか追いやすい）。
            var total = children.Sum(c => c.Height) + (verticalGap * (children.Count - 1));
            var y = Math.Max(0, parentY + (parentHeight / 2) - (total / 2));

            foreach (var (_, height) in children)
            {
                result.Add((x, y));
                y += height + verticalGap;
            }

            return result;
        }

        var top = parentY + parentHeight + verticalGap;
        var width = children.Sum(c => c.Width) + (horizontalGap * (children.Count - 1));
        var left = Math.Max(0, parentX + (parentWidth / 2) - (width / 2));

        foreach (var (childWidth, _) in children)
        {
            result.Add((left, top));
            left += childWidth + horizontalGap;
        }

        return result;
    }
}
