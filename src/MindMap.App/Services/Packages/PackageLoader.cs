using System.IO;
using System.Reflection;
using System.Text.Json;
using MindMapX.Abstractions.Thumbnails;
using MindMapX.Abstractions.Viewers;
using MindMap.Services.Thumbnails;
using MindMap.Services.Viewers;

namespace MindMap.Services.Packages;

/// <summary>
/// <c>plugins</c> フォルダーを見て、パッケージが名乗った提供物を種類ごとのレジストリへ配る。
///
/// 種類が増えても走査と検証の流れは 1 本のままで、<see cref="Distribute"/> の中が
/// 種類ごとに分かれるだけ。1 つのパッケージが読めなくても、残りは読み込む。
/// </summary>
public static class PackageLoader
{
    public const string FolderName = "plugins";
    public const string ManifestName = "plugin.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 実行ファイルの隣にある <c>plugins</c> を走査する。
    /// フォルダーが無ければ何もしない（パッケージを 1 つも入れていない状態が普通）。
    /// </summary>
    public static PackageLoadResult LoadAll(
        ViewerRegistry viewers,
        ThumbnailRegistry thumbnails) =>
        LoadAll(Path.Combine(AppContext.BaseDirectory, FolderName), viewers, thumbnails);

    public static PackageLoadResult LoadAll(
        string root,
        ViewerRegistry viewers,
        ThumbnailRegistry thumbnails)
    {
        var loaded = new List<PackageManifest>();
        var errors = new List<string>();

        if (!Directory.Exists(root))
        {
            return new PackageLoadResult(loaded, errors);
        }

        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(f => f))
        {
            var manifestPath = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = Read(manifestPath);
                Distribute(manifest, folder, viewers, thumbnails);
                loaded.Add(manifest);
            }
            catch (Exception ex)
            {
                // 1 つ読めなくても残りは読み込む。壊れたパッケージでアプリを止めない。
                errors.Add($"{Path.GetFileName(folder)}: {ex.Message}");
            }
        }

        return new PackageLoadResult(loaded, errors);
    }

    private static PackageManifest Read(string path)
    {
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path), Options)
                       ?? throw new InvalidDataException($"{ManifestName} を読めませんでした。");

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidDataException("id が書かれていません。");
        }

        RequireCompatible(manifest);
        return manifest;
    }

    /// <summary>
    /// 契約の大版が食い違うパッケージは読み込まない。
    /// 小版の違いは、足された分を使わなければ動くので通す。
    /// </summary>
    private static void RequireCompatible(PackageManifest manifest)
    {
        var host = typeof(IContentViewerFactory).Assembly.GetName().Version ?? new Version(1, 0);

        if (!Version.TryParse(manifest.ApiVersion, out var required))
        {
            throw new InvalidDataException(
                $"apiVersion が読めません（{manifest.ApiVersion}）。ホストは {host.Major}.{host.Minor} です。");
        }

        if (required.Major != host.Major)
        {
            throw new InvalidDataException(
                $"対応していない apiVersion です（要求 {required.Major}.{required.Minor} / ホスト {host.Major}.{host.Minor}）。");
        }
    }

    /// <summary>宣言された提供物を、種類ごとのレジストリへ配る。種類が増えたらここに足す。</summary>
    private static void Distribute(
        PackageManifest manifest,
        string folder,
        ViewerRegistry viewers,
        ThumbnailRegistry thumbnails)
    {
        foreach (var contribution in manifest.Contributes.Viewers)
        {
            if (string.IsNullOrWhiteSpace(contribution.Type))
            {
                throw new InvalidDataException("viewers に type が書かれていません。");
            }

            var id = $"{manifest.Id}/{contribution.Type}";

            viewers.Add(new DeferredViewerFactory(
                id,
                contribution.Priority,
                contribution.Extensions,
                () => Create<IContentViewerFactory>(manifest, folder, contribution.Type)));
        }

        foreach (var contribution in manifest.Contributes.Thumbnails)
        {
            if (string.IsNullOrWhiteSpace(contribution.Type))
            {
                throw new InvalidDataException("thumbnails に type が書かれていません。");
            }

            thumbnails.Add(new DeferredThumbnailProvider(
                $"{manifest.Id}/{contribution.Type}",
                contribution.Priority,
                contribution.Extensions,
                () => Create<INodeThumbnailProvider>(manifest, folder, contribution.Type)));
        }

        // WPF 版向けのツールは入れられても使えない。黙って捨てると「入れたのに出ない」になるので、
        // 種類ごとの欄に無いものとして扱われていることだけは分かるようにしておく。
        if (manifest.Contributes.Extra?.ContainsKey("tools") == true
            && manifest.Contributes.Viewers.Count == 0
            && manifest.Contributes.Thumbnails.Count == 0)
        {
            throw new InvalidDataException("ツールだけのパッケージは MindMapX では使えません（WPF 版向け）。");
        }
    }

    /// <summary>
    /// 実際に必要になった時点で DLL を読み、提供物を作る。
    /// 読み込み先はパッケージごとに分けてあるので、同梱したライブラリの版がぶつからない。
    /// </summary>
    private static T Create<T>(PackageManifest manifest, string folder, string typeName)
    {
        if (manifest.Entry?.Assembly is not { Length: > 0 } assemblyName)
        {
            throw new InvalidDataException($"{manifest.Id}: entry.assembly が書かれていません。");
        }

        var assemblyPath = Path.Combine(folder, assemblyName);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"{manifest.Id}: {assemblyName} が見つかりません。", assemblyPath);
        }

        var context = new PackageLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        var type = assembly.GetType(typeName, throwOnError: false)
                   ?? throw new TypeLoadException($"{manifest.Id}: {typeName} が見つかりません。");

        if (Activator.CreateInstance(type) is not T instance)
        {
            throw new InvalidCastException(
                $"{manifest.Id}: {typeName} は {typeof(T).Name} を実装していません。");
        }

        return instance;
    }
}

/// <summary>読み込めたパッケージと、読めなかったものの理由。</summary>
public sealed record PackageLoadResult(IReadOnlyList<PackageManifest> Loaded, IReadOnlyList<string> Errors);
