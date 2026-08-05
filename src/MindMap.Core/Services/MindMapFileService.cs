using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MindMap.Models;

namespace MindMap.Services;

/// <summary>マインドマップの JSON ファイル (*.mindmap) 入出力。</summary>
public static class MindMapFileService
{
    public const string FileExtension = ".mindmap";
    public const string FileDialogFilter = "マインドマップ (*.mindmap)|*.mindmap|すべてのファイル (*.*)|*.*";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 日本語をそのまま出力し、ファイルをテキストエディタでも読めるようにする。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static void Save(string path, MindMapDocument document)
    {
        var json = JsonSerializer.Serialize(document, Options);
        File.WriteAllText(path, json);
    }

    public static MindMapDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MindMapDocument>(json, Options)
               ?? throw new InvalidDataException("ファイルの内容が空か、マインドマップとして解釈できません。");
    }
}
