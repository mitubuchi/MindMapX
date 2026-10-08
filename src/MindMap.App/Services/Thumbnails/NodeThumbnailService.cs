using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MindMapX.Abstractions.Thumbnails;

namespace MindMap.Services.Thumbnails;

/// <summary>
/// ノードのリンク先から小さな絵を作って配る。作り方はパッケージ（<see cref="ThumbnailRegistry"/>）が持ち、
/// ここは「いつ作るか・どこで作るか・作ったものをどう使い回すか」だけを見る。
///
/// 作る場所を UI スレッドから外してあるのが要点。画像のデコードも、動画のポスターフレームの
/// 取り出しも 1 枚ずつは短いが、マップを開いた瞬間に何十枚も作るので、UI スレッドで回すと
/// そのぶん画面が止まる。かといって普通のスレッドプールにも投げられない
/// （Windows の OS のサムネイルは COM を使うので STA から呼ぶ必要がある）。
/// そこで作業スレッドを 1 本だけ立てて、そこに順番に流す（WPF 版と同じ考え方）。
/// </summary>
public sealed class NodeThumbnailService : INodeThumbnailSource, IDisposable
{
    /// <summary>一辺の目安。ノード側の枠もこの大きさで用意する。</summary>
    public const int Size = 256;

    private readonly ThumbnailRegistry _registry;

    /// <summary>
    /// 同じファイルを何度も作り直さないための控え。
    /// 鍵にファイルの更新日時と大きさを混ぜてあるので、差し替えられた画像は作り直される。
    /// </summary>
    private readonly Dictionary<string, Task<object?>> _cache = new();

    /// <summary>作業スレッドへの順番待ちの列。1 枚も要らないマップではスレッドも立てない。</summary>
    private readonly Lazy<BlockingCollection<Action>> _queue;

    private bool _disposed;

    public NodeThumbnailService(ThumbnailRegistry registry)
    {
        _registry = registry;
        _queue = new Lazy<BlockingCollection<Action>>(StartWorker);
    }

    /// <summary>
    /// 絵を作る（すでに作ってあれば、そのまま返す）。
    /// 作れないとき（パッケージが無い・扱えない種類・ファイルが無い）は null。
    ///
    /// UI スレッドから呼ぶこと。実際の生成だけが作業スレッドへ回る。
    /// </summary>
    public Task<object?> GetAsync(string? absolutePath)
    {
        if (_disposed || _registry.IsEmpty || string.IsNullOrEmpty(absolutePath))
        {
            return Task.FromResult<object?>(null);
        }

        FileInfo info;
        try
        {
            info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                return Task.FromResult<object?>(null);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or IOException or UnauthorizedAccessException)
        {
            // 読めない場所を指していた、というだけ。絵が付かないノードとして扱う。
            return Task.FromResult<object?>(null);
        }

        var request = new ThumbnailRequest(info.FullName, Size);
        if (_registry.Resolve(request) is not { } provider)
        {
            return Task.FromResult<object?>(null);
        }

        // 同じ中身なら作り直さない。差し替えられたら（更新日時か大きさが変われば）別の鍵になる。
        var key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var task = RunAsync(provider, request);
        _cache[key] = task;
        return task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_queue.IsValueCreated)
        {
            _queue.Value.CompleteAdding();
        }
    }

    private async Task<object?> RunAsync(INodeThumbnailProvider provider, ThumbnailRequest request)
    {
        var done = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _queue.Value.Add(() =>
            {
                try
                {
                    // 提供物が非同期で書かれていても、作業スレッドの上で終わるまで待つ
                    // （STA のまま COM を呼ばせるため、続きを別スレッドへ逃がさない）。
                    done.SetResult(provider.GetAsync(request, CancellationToken.None).GetAwaiter().GetResult());
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            });

            var image = await done.Task;
            return image is null ? null : AsPixelSized(image);
        }
        catch (Exception)
        {
            // 壊れたファイル、対応していない形式、パッケージ側の不具合。
            // どれも「絵が付かない」で済ませる。ノードは本文つきの見た目のまま出る。
            return null;
        }
    }

    /// <summary>
    /// 絵の解像度（dpi）を 96 に直す。中身のピクセルはそのままで、大きさの言い方だけを変える。
    ///
    /// 頼むときの一辺（<see cref="Size"/>）はピクセルで数えているのに、画面は絵の大きさを
    /// 「ピクセル数 × 96 ÷ dpi」で測る。元の写真の dpi を引き継いだ絵をそのまま渡すと、
    /// 350 dpi の写真では 256px の絵が 70 相当と見なされ、枠の中で小さく描かれる
    /// （WPF 版で実際に起きた）。置き方を決めているのはホストなので、物差しもホストが合わせる。
    /// </summary>
    private static Bitmap AsPixelSized(Bitmap image)
    {
        if (Math.Abs(image.Dpi.X - 96) < 0.5 && Math.Abs(image.Dpi.Y - 96) < 0.5)
        {
            return image;
        }

        try
        {
            var size = image.PixelSize;
            var stride = size.Width * 4;
            var buffer = Marshal.AllocHGlobal(stride * size.Height);
            try
            {
                image.CopyPixels(new PixelRect(size), buffer, stride * size.Height, stride);
                return new Bitmap(
                    image.Format ?? PixelFormat.Bgra8888,
                    image.AlphaFormat ?? AlphaFormat.Premul,
                    buffer,
                    size,
                    new Vector(96, 96),
                    stride);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            // 写し取れない形式だった。小さく出るとしても、絵が無いよりはましなので元のまま返す。
            return image;
        }
    }

    /// <summary>
    /// 絵を作るための作業スレッドを 1 本立てる。Windows では STA にする
    /// （OS のサムネイル＝動画のポスターフレームが COM を使うため）。
    /// </summary>
    private static BlockingCollection<Action> StartWorker()
    {
        var queue = new BlockingCollection<Action>();

        var thread = new Thread(() =>
        {
            foreach (var work in queue.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            // 画面を閉じるときに、作りかけの絵を待たない。
            IsBackground = true,
            Name = "MindMap thumbnails",
        };

        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        return queue;
    }
}
