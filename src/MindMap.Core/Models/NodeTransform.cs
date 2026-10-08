using System.Text.Json.Serialization;

namespace MindMap.Models;

/// <summary>
/// ノードの置き方。Unity の Transform と同じ 3 つ組で持つ。
///
/// 値は<b>親からの相対</b>で、Unity の localPosition / localEulerAngles / localScale に対応する。
/// 相対にしておくと、親を動かせば部分木ごと動き、レイアウトは「ある親の子たちの位置」だけを
/// 決めればよくなる（絶対座標だと部分木を動かすたびに全体を計算し直すことになる）。
///
/// Y は<b>画面の下向きが正</b>。<see cref="MindMapNodeDto.X"/> / <see cref="MindMapNodeDto.Y"/> と
/// 符号を揃えておかないと、両者が食い違ったときの解決規則が意味を成さなくなるため。
/// Unity へ持っていくときは、取り込む側で符号を反転する。
///
/// <see cref="Rotation"/> は 2D の描画では使わない。保存して持ち越すだけで、
/// 実際に使うのは 3D 側や Unity 側。
/// </summary>
public sealed class NodeTransform
{
    /// <summary>親からの相対位置。欠けていれば原点。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Vector3? Position { get; set; }

    /// <summary>
    /// オイラー角（度）。2D では無視する。Unity 側で Quaternion.Euler に通す。
    /// 回っていないノードでは書き出さない（ほとんどのノードで 0 のままなので、
    /// 全部に書くとファイルが読みにくくなる）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Vector3? Rotation { get; set; }

    /// <summary>親を基準とした倍率。欠けていれば等倍。等倍のときは書き出さない。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Vector3? Scale { get; set; }

    /// <summary>取り出し用。書き出すのは <see cref="Position"/> のほうなので、この形は保存しない。</summary>
    [JsonIgnore]
    public double PositionX => Position?.X ?? 0;

    [JsonIgnore]
    public double PositionY => Position?.Y ?? 0;

    [JsonIgnore]
    public double PositionZ => Position?.Z ?? 0;

    [JsonIgnore]
    public double RotationX => Rotation?.X ?? 0;

    [JsonIgnore]
    public double RotationY => Rotation?.Y ?? 0;

    [JsonIgnore]
    public double RotationZ => Rotation?.Z ?? 0;

    /// <summary>
    /// 倍率。欠けている欄は 1 とみなす。
    ///
    /// 0 を既定にすると、欄をひとつ書き忘れただけでノードが消える。
    /// 「書いていない」と「0 と書いた」は別物として扱う（だから <see cref="Vector3"/> の
    /// 各成分も null を取れるようにしてある）。
    /// </summary>
    [JsonIgnore]
    public double ScaleX => Scale?.X ?? 1;

    [JsonIgnore]
    public double ScaleY => Scale?.Y ?? 1;

    [JsonIgnore]
    public double ScaleZ => Scale?.Z ?? 1;
}

/// <summary>
/// ファイルに書く 3 成分。書いていない成分と 0 を書いた成分を区別するため、
/// 各成分が null を取れる（<see cref="NodeTransform.ScaleX"/> の説明を参照）。
/// </summary>
public sealed class Vector3
{
    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Z { get; set; }

    public static Vector3 Of(double x, double y, double z) => new() { X = x, Y = y, Z = z };
}
