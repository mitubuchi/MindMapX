using System.IO;
using System.Text;
using System.Text.Json;
using MindMapX.Abstractions.Viewers;
using MindMap.Models;

namespace MindMap.Services.Viewers;

/// <summary>
/// リンク先のマインドマップを、親子関係が分かるアウトラインとして出す。
/// 図として描くにはビューアの幅が足りないので、タイトルを字下げで並べ、
/// どんな構造のマップなのかだけが分かるようにする。
/// </summary>
public sealed class MindMapOutlineViewerFactory : IContentViewerFactory
{
    public string Id => "builtin.mindmap-outline";

    public int Priority => 100;

    public bool CanView(ViewerContent content) =>
        string.Equals(content.Extension, MindMapFileService.FileExtension, StringComparison.OrdinalIgnoreCase);

    public IContentViewer Create() => new MindMapOutlineViewer();
}

internal sealed class MindMapOutlineViewer : TextContentViewer
{
    /// <summary>字下げ 1 段ぶんの空白。</summary>
    private const string Indent = "  ";

    // ファイルの読み込みと組み立てで画面を止めないよう、別のスレッドに逃がす。
    protected override Task<TextDocument> BuildAsync(
        ViewerContent content,
        CancellationToken cancellationToken) =>
        Task.Run(() => Build(content.FilePath), cancellationToken);

    private static TextDocument Build(string path)
    {
        MindMapDocument document;

        try
        {
            document = MindMapFileService.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or InvalidDataException)
        {
            return TextDocument.Unavailable($"マインドマップとして読めませんでした。\n\n{ex.Message}");
        }

        if (document.Nodes.Count == 0)
        {
            return TextDocument.Unavailable("このマインドマップにはノードがありません。");
        }

        // 兄弟はキャンバスでの並び（上から、同じ高さなら左から）に揃える。
        // ファイル上の順序は編集の履歴でばらばらなので、そのままでは読み取りにくい。
        var children = document.Nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Y).ThenBy(n => n.X).ToList());

        var ids = document.Nodes.Select(n => n.Id).ToHashSet();

        // 親が見当たらないノードも根として扱う。壊れたファイルでも構造は見せられるように。
        var roots = document.Nodes
            .Where(n => n.ParentId is not { } parent || !ids.Contains(parent))
            .OrderBy(n => n.Y)
            .ThenBy(n => n.X)
            .ToList();

        var builder = new StringBuilder();
        var visited = new HashSet<Guid>();

        foreach (var root in roots)
        {
            Append(root, 0);
        }

        return TextDocument.Of(builder.ToString().TrimEnd());

        void Append(MindMapNodeDto node, int depth)
        {
            // 親子が輪になっているファイルでも止まるよう、一度出したノードは辿らない。
            if (!visited.Add(node.Id))
            {
                return;
            }

            var title = node.ResolveTitle();

            builder
                .Append(string.Concat(Enumerable.Repeat(Indent, depth)))
                .Append(depth == 0 ? "■ " : "・")
                .AppendLine(string.IsNullOrWhiteSpace(title) ? "(無題)" : title);

            if (!children.TryGetValue(node.Id, out var list))
            {
                return;
            }

            foreach (var child in list)
            {
                Append(child, depth + 1);
            }
        }
    }
}
