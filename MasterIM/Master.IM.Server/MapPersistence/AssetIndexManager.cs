using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MasterIM.Server.MapPersistence;

/// <summary>
/// 素材索引管理器：管理 assets/index.json
/// </summary>
public class AssetIndexManager
{
    private readonly string _roomId;
    private readonly string _indexPath;
    private Dictionary<string, AssetMetadata> _assets = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AssetIndexManager(string roomId)
    {
        _roomId = roomId;
        _indexPath = RoomAssetPaths.GetAssetIndexPath(roomId);
        Load();
    }

    /// <summary>
    /// 加载索引文件
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_indexPath))
            {
                var json = File.ReadAllText(_indexPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, AssetMetadata>>(json, JsonOptions);
                _assets = data ?? new();
            }
            else
            {
                _assets = new();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetIndex] 加载索引失败 RoomId={_roomId}: {ex.Message}");
            _assets = new();
        }
    }

    /// <summary>
    /// 保存索引文件
    /// </summary>
    public void Save()
    {
        try
        {
            RoomAssetPaths.EnsureDirectories(_roomId);
            var json = JsonSerializer.Serialize(_assets, JsonOptions);
            File.WriteAllText(_indexPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetIndex] 保存索引失败 RoomId={_roomId}: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查素材是否存在
    /// </summary>
    public bool HasAsset(string hash)
    {
        return _assets.ContainsKey(hash);
    }

    /// <summary>
    /// 获取素材元数据
    /// </summary>
    public AssetMetadata? GetAsset(string hash)
    {
        return _assets.TryGetValue(hash, out var asset) ? asset : null;
    }

    /// <summary>
    /// 添加素材
    /// </summary>
    public void AddAsset(AssetMetadata asset)
    {
        _assets[asset.Hash] = asset;
        Save();
    }

    /// <summary>
    /// 删除素材（refCount == 0 时立即删除）
    /// </summary>
    public void RemoveAsset(string hash)
    {
        if (_assets.Remove(hash))
        {
            Save();

            // 删除物理文件（语义命名架构：从元数据获取 relativePath）
            try
            {
                var asset = GetAsset(hash);
                if (asset != null && !string.IsNullOrEmpty(asset.RelativePath))
                {
                    var assetRoot = RoomAssetPaths.GetAssetsDirectory(_roomId);
                    var assetPath = Path.Combine(assetRoot, asset.RelativePath);
                    if (File.Exists(assetPath))
                    {
                        File.Delete(assetPath);
                        Console.WriteLine($"[AssetIndex] 素材文件已删除 RelativePath={asset.RelativePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssetIndex] 删除素材文件失败 Hash={hash}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 添加引用
    /// </summary>
    public void AddReference(string hash, AssetReference reference)
    {
        if (_assets.TryGetValue(hash, out var asset))
        {
            if (!asset.References.Contains(reference))
            {
                asset.References.Add(reference);
                asset.RefCount = asset.References.Count;
                Save();
            }
        }
    }

    /// <summary>
    /// 移除引用（refCount 归零时立即删除素材）
    /// </summary>
    public void RemoveReference(string hash, AssetReference reference)
    {
        if (_assets.TryGetValue(hash, out var asset))
        {
            asset.References.Remove(reference);
            asset.RefCount = asset.References.Count;

            // refCount == 0 立即删除素材（不保留期）
            if (asset.RefCount == 0)
            {
                Console.WriteLine($"[AssetIndex] 素材引用计数归零，立即删除 Hash={hash}");
                RemoveAsset(hash);
            }
            else
            {
                Save();
            }
        }
    }

    /// <summary>
    /// 获取所有素材
    /// </summary>
    public IEnumerable<AssetMetadata> GetAllAssets()
    {
        return _assets.Values;
    }

    /// <summary>
    /// 按类型获取素材
    /// </summary>
    public IEnumerable<AssetMetadata> GetAssetsByType(string type)
    {
        return _assets.Values.Where(a => a.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按标签搜索素材
    /// </summary>
    public IEnumerable<AssetMetadata> SearchByTag(string tag)
    {
        return _assets.Values.Where(a =>
            a.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)));
    }
}
