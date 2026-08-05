using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MindMap.Models;

namespace MindMap.Services;

/// <summary>
/// ノードのかたまりを、クリップボードに載せる形（JSON と箇条書き）に変換する。
/// クリップボードそのものへの読み書きは OS ごとに違うので <see cref="INodeClipboard"/> に任せ、
/// ここには形式の組み立て・読み取りだけを置く（どのプラットフォームでも同じ中身にするため）。
/// </summary>
public static class NodeClipboardFormat
{
    /// <summary>
    /// MindMap 専用のクリップボード形式。ファイル保存と同じ JSON を載せるので、
    /// 形式のバージョン管理は <see cref="MindMapDocument.Version"/> に任せられる。
    /// </summary>
    public const string ClipboardFormat = "MindMap.Nodes";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Serialize(MindMapDocument fragment) => JsonSerializer.Serialize(fragment, Options);

    /// <summary>MindMap が載せた JSON を読み取る。壊れていれば null。</summary>
    public static MindMapDocument? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var fragment = JsonSerializer.Deserialize<MindMapDocument>(json, Options);
            return fragment is { Nodes.Count: > 0 } ? fragment : null;
        }
        catch (JsonException)
        {
            // 形式名は同じでも中身が壊れている場合。呼び出し側が文字列として拾い直す。
            return null;
        }
    }

    /// <summary>
    /// MindMap 以外からコピーされた文字列を 1 つのノードにする。
    /// 1 行目をタイトル、残りを内容に入れる（URL をそのまま貼れるようにするため）。
    /// </summary>
    public static MindMapDocument? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var title = lines[0].Trim();
        var body = string.Join("\n", lines.Skip(1)).TrimEnd();

        return new MindMapDocument
        {
            Nodes =
            {
                new MindMapNodeDto
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    Body = body,
                },
            },
        };
    }

    /// <summary>他のアプリに貼るための文字列。親子の深さを字下げで表す。</summary>
    public static string ToOutline(MindMapDocument fragment)
    {
        var byParent = fragment.Nodes
            .Where(n => n.ParentId is not null)
            .ToLookup(n => n.ParentId!.Value);

        var ids = fragment.Nodes.Select(n => n.Id).ToHashSet();
        var builder = new StringBuilder();

        foreach (var root in fragment.Nodes.Where(n => n.ParentId is null || !ids.Contains(n.ParentId.Value)))
        {
            AppendOutline(builder, byParent, root, 0);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendOutline(
        StringBuilder builder,
        ILookup<Guid, MindMapNodeDto> byParent,
        MindMapNodeDto node,
        int depth)
    {
        var indent = new string(' ', depth * 2);
        builder.Append(indent).AppendLine(node.ResolveTitle());

        if (!string.IsNullOrWhiteSpace(node.Body))
        {
            foreach (var line in node.Body.Replace("\r\n", "\n").Split('\n'))
            {
                builder.Append(indent).Append("  ").AppendLine(line);
            }
        }

        foreach (var child in byParent[node.Id])
        {
            AppendOutline(builder, byParent, child, depth + 1);
        }
    }
}
