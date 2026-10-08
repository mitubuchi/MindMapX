using System.Reflection;
using System.Runtime.Loader;

namespace MindMap.Services.Packages;

/// <summary>
/// パッケージ 1 つぶんの読み込み先。パッケージだけが同梱した DLL は
/// <c>.deps.json</c> から解決するので、それぞれが違う版のライブラリを持てる。
///
/// <b>ホストが持っているアセンブリは、必ずホストのものを使う。</b>
/// WPF 版では WPF がフレームワーク側にあるので、契約（MindMap.Abstractions）だけを
/// 共有すれば済んだ。Avalonia は NuGet で配るライブラリなので、パッケージのフォルダーにも
/// Avalonia.*.dll が置かれる。そのまま解決させると同じ名前の別の型になり、
/// パッケージが作った画面（Control）をホストの画面に載せられない
/// （SkiaSharp の絵や Bitmap も同じ）。ホストの一覧に載っている名前は null を返して、
/// 既定の読み込み先（ホスト側）に寄せる。
/// </summary>
internal sealed class PackageLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// ホストが起動時から持っているアセンブリの名前。実行ファイルの .deps.json から
    /// ランタイムが組み立てた一覧（TRUSTED_PLATFORM_ASSEMBLIES）を使う。
    /// .NET 本体・Avalonia・契約がすべて入っている。
    /// </summary>
    private static readonly Lazy<HashSet<string>> HostAssemblies = new(() =>
    {
        var list = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return list
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    private readonly AssemblyDependencyResolver _resolver;

    public PackageLoadContext(string mainAssemblyPath)
        // 取り外しはしない。画面が参照を握るため、collectible にしても実際には外れない。
        : base(isCollectible: false) =>
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && HostAssemblies.Value.Contains(name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        // null を返すと既定の読み込み先に回る。
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // ここに来るのは、パッケージだけが持つアセンブリからの呼び出しだけ。
        // SkiaSharp のようにホストへ寄せたものは、ホスト側でネイティブ DLL を解決する。
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
