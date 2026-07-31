using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// 素材库缩略图缓存。
///
/// 为什么按固定宽度解码：素材卡片尺寸可由滑杆调节（AssetThumbnailSize），
/// 如果按当前尺寸解码，每拖一次滑杆就要重解一遍全部图片。
/// 固定 <see cref="DecodeWidth"/> 解码后交给 Image 控件做 Uniform 缩放，
/// 一张图只解一次，内存也可控（4K 贴图不会整张进内存）。
///
/// 缓存键带文件最后写入时间，素材被外部替换后自动失效。
/// </summary>
public static class AssetThumbnailCache
{
    /// <summary>缩略图解码宽度（像素）。够 2 倍 DPI 下的最大卡片尺寸用。</summary>
    public const int DecodeWidth = 192;

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 异步取缩略图。同一路径并发请求共享同一个 Task，不会重复解码。
    /// 解码失败（文件损坏 / 格式不支持）返回 null，调用方回退到 emoji 图标。
    /// </summary>
    public static Task<Bitmap?> GetAsync(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        var key = BuildKey(imagePath);
        return Cache.GetOrAdd(key, _ => Task.Run(() => Decode(imagePath)));
    }

    /// <summary>清空缓存（素材根目录切换时调用）。</summary>
    public static void Clear() => Cache.Clear();

    private static string BuildKey(string imagePath)
    {
        try
        {
            var info = new FileInfo(imagePath);
            return $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return imagePath;
        }
    }

    private static Bitmap? Decode(string imagePath)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            return Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            // 损坏文件 / 不支持的格式：静默回退到 emoji 图标
            return null;
        }
    }
}
