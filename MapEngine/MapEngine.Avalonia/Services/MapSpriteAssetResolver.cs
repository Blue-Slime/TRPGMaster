using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using MapEngine.Core.Assets;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// 把 AssetRef（sha256 哈希）解析成素材库里的实际图片路径。
/// 新架构：图片使用语义命名存储，通过 asset-index.json 元数据索引查找哈希对应的文件路径。
/// </summary>
public static class MapSpriteAssetResolver
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    private static readonly ConcurrentDictionary<string, string?> HashPathCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static AssetMetadataStore? _localMetadataStore;
    private static AssetMetadataStore? _roomMetadataStore;

    /// <summary>
    /// 房间素材库根目录。联机时由宿主注入，null 表示单机。
    /// </summary>
    public static string? RoomAssetRoot { get; set; }

    /// <summary>初始化元数据索引（在素材库路径变更时调用）。</summary>
    public static void Initialize()
    {
        var localRoot = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
        _localMetadataStore = new AssetMetadataStore(localRoot);

        if (!string.IsNullOrWhiteSpace(RoomAssetRoot))
        {
            _roomMetadataStore = new AssetMetadataStore(RoomAssetRoot);
        }
        else
        {
            _roomMetadataStore = null;
        }

        ClearCache();
    }

    /// <summary>按内容哈希解析图片路径，找不到返回 null。</summary>
    public static string? ResolveSpritePath(string? assetRef)
    {
        if (string.IsNullOrWhiteSpace(assetRef))
            return null;

        // 确保元数据索引已初始化
        if (_localMetadataStore == null)
            Initialize();

        return HashPathCache.GetOrAdd(assetRef, FindByHash);
    }

    /// <summary>读取 .asset 文件里的 assetRef，再解析成图片路径（素材库缩略图用）。</summary>
    public static string? ResolveAssetFileSprite(string? assetFilePath)
    {
        if (string.IsNullOrWhiteSpace(assetFilePath) || !File.Exists(assetFilePath))
            return null;

        if (!assetFilePath.EndsWith(AssetLibraryFileSystemService.StaticObjectExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            return ImageExtensions.Contains(Path.GetExtension(assetFilePath), StringComparer.OrdinalIgnoreCase)
                ? assetFilePath
                : null;
        }

        try
        {
            var json = File.ReadAllText(assetFilePath);
            var document = JsonSerializer.Deserialize<StaticObjectAssetDocument>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var assetRef = document?.Components
                .FirstOrDefault(c => c.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))
                ?.Properties.GetValueOrDefault("assetRef");

            return ResolveSpritePath(assetRef);
        }
        catch
        {
            return null;
        }
    }

    public static void Invalidate(string? assetRef)
    {
        if (!string.IsNullOrWhiteSpace(assetRef))
            HashPathCache.TryRemove(assetRef, out _);
    }

    public static void ClearCache() => HashPathCache.Clear();

    private static string? FindByHash(string hash)
    {
        // 规范化哈希格式（移除 "sha256:" 前缀）
        var normalizedHash = hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? hash.Substring(7)
            : hash;

        // 1. 优先查房间库（房间优先策略）
        if (_roomMetadataStore != null)
        {
            var roomPath = _roomMetadataStore.GetPathByHash($"sha256:{normalizedHash}");
            if (roomPath != null)
            {
                var fullPath = Path.Combine(RoomAssetRoot!, roomPath);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }
        }

        // 2. 降级到本地库
        if (_localMetadataStore != null)
        {
            var localPath = _localMetadataStore.GetPathByHash($"sha256:{normalizedHash}");
            if (localPath != null)
            {
                var localRoot = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
                var fullPath = Path.Combine(localRoot, localPath);
                if (File.Exists(fullPath))
                    return Path.GetFullPath(fullPath);
            }
        }

        // 3. 后备方案：递归搜索（兼容旧数据，未来可移除）
        return FallbackSearchByHash(normalizedHash);
    }

    /// <summary>后备搜索方法（兼容旧版哈希命名文件）。</summary>
    private static string? FallbackSearchByHash(string hash)
    {
        // 先搜本地库
        var localRoot = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
        var result = SearchInRoot(localRoot, hash);
        if (result is not null)
            return result;

        // 再搜房间库
        if (!string.IsNullOrWhiteSpace(RoomAssetRoot))
        {
            result = SearchInRoot(RoomAssetRoot, hash);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static string? SearchInRoot(string root, string hash)
    {
        if (!Directory.Exists(root))
            return null;

        foreach (var ext in ImageExtensions)
        {
            var match = Directory
                .EnumerateFiles(root, $"{hash}{ext}", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (match is not null)
                return Path.GetFullPath(match);
        }

        return null;
    }
}
