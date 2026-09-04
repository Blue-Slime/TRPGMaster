using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapEngine.Core.Assets;

/// <summary>资产元数据存储（管理 asset-index.json）。</summary>
public sealed class AssetMetadataStore
{
    private readonly string _libraryRoot;
    private readonly string _indexPath;
    private AssetIndex _index;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AssetMetadataStore(string libraryRoot)
    {
        _libraryRoot = libraryRoot;
        _indexPath = Path.Combine(libraryRoot, ".metadata", "asset-index.json");
        _index = Load();
    }

    /// <summary>根据哈希查找文件相对路径。</summary>
    public string? GetPathByHash(string hash)
    {
        var key = NormalizeHashKey(hash);
        return _index.Assets.TryGetValue(key, out var metadata) ? metadata.RelativePath : null;
    }

    /// <summary>根据相对路径查找哈希。</summary>
    public string? GetHashByPath(string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        foreach (var (hash, metadata) in _index.Assets)
        {
            if (NormalizePath(metadata.RelativePath) == normalizedPath)
                return hash;
        }
        return null;
    }

    /// <summary>检查哈希是否已存在（用于去重）。</summary>
    public bool ContainsHash(string hash)
    {
        var key = NormalizeHashKey(hash);
        return _index.Assets.ContainsKey(key);
    }

    /// <summary>获取元数据对象。</summary>
    public AssetMetadata? GetMetadata(string hash)
    {
        var key = NormalizeHashKey(hash);
        return _index.Assets.TryGetValue(key, out var metadata) ? metadata : null;
    }

    /// <summary>注册新资产。</summary>
    public void Register(string hash, AssetMetadata metadata)
    {
        var key = NormalizeHashKey(hash);
        _index.Assets[key] = metadata;
        Save();
    }

    /// <summary>更新路径（重命名/移动时调用）。</summary>
    public void UpdatePath(string hash, string newRelativePath)
    {
        var key = NormalizeHashKey(hash);
        if (_index.Assets.TryGetValue(key, out var metadata))
        {
            metadata.RelativePath = newRelativePath;
            metadata.FileName = Path.GetFileName(newRelativePath);
            Save();
        }
    }

    /// <summary>删除条目。</summary>
    public void Remove(string hash)
    {
        var key = NormalizeHashKey(hash);
        if (_index.Assets.Remove(key))
        {
            Save();
        }
    }

    /// <summary>获取所有哈希列表。</summary>
    public IEnumerable<string> GetAllHashes()
    {
        return _index.Assets.Keys;
    }

    /// <summary>加载索引文件。</summary>
    private AssetIndex Load()
    {
        if (!File.Exists(_indexPath))
        {
            return new AssetIndex();
        }

        try
        {
            var json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<AssetIndex>(json, JsonOptions) ?? new AssetIndex();
        }
        catch
        {
            // 索引文件损坏，返回空索引
            return new AssetIndex();
        }
    }

    /// <summary>保存索引文件。</summary>
    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_indexPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_index, JsonOptions);
            File.WriteAllText(_indexPath, json);
        }
        catch
        {
            // 静默失败，避免阻塞主流程
        }
    }

    /// <summary>标准化哈希键（移除 "sha256:" 前缀，统一大小写）。</summary>
    private static string NormalizeHashKey(string hash)
    {
        return hash.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
    }

    /// <summary>更新资源文件的相对路径（用于文件重命名或移动）。</summary>
    public void UpdateRelativePath(string oldRelativePath, string newRelativePath)
    {
        var hash = GetHashByPath(oldRelativePath);
        if (hash != null)
        {
            UpdatePath(hash, newRelativePath);
        }
    }

    /// <summary>批量更新路径前缀（用于文件夹重命名或移动）。</summary>
    public void UpdateRelativePathPrefix(string oldPrefix, string newPrefix)
    {
        var normalizedOldPrefix = NormalizePath(oldPrefix);
        var updated = false;

        foreach (var (hash, metadata) in _index.Assets.ToList())
        {
            var normalizedPath = NormalizePath(metadata.RelativePath);
            if (normalizedPath.StartsWith(normalizedOldPrefix + "/") || normalizedPath == normalizedOldPrefix)
            {
                var suffix = normalizedPath.Length > normalizedOldPrefix.Length
                    ? normalizedPath.Substring(normalizedOldPrefix.Length)
                    : "";
                metadata.RelativePath = newPrefix + suffix;
                metadata.FileName = Path.GetFileName(newPrefix + suffix);
                updated = true;
            }
        }

        if (updated)
        {
            Save();
        }
    }

    /// <summary>克隆路径前缀（用于文件夹复制）。</summary>
    public void ClonePathPrefix(string sourcePrefix, string targetPrefix)
    {
        var normalizedSourcePrefix = NormalizePath(sourcePrefix);
        var cloned = false;

        foreach (var (hash, metadata) in _index.Assets.ToList())
        {
            var normalizedPath = NormalizePath(metadata.RelativePath);
            if (normalizedPath.StartsWith(normalizedSourcePrefix + "/") || normalizedPath == normalizedSourcePrefix)
            {
                var suffix = normalizedPath.Length > normalizedSourcePrefix.Length
                    ? normalizedPath.Substring(normalizedSourcePrefix.Length)
                    : "";
                var newPath = targetPrefix + suffix;

                var newMetadata = new AssetMetadata
                {
                    Hash = metadata.Hash,
                    Algorithm = metadata.Algorithm,
                    RelativePath = newPath,
                    FileName = Path.GetFileName(newPath),
                    MimeType = metadata.MimeType,
                    Size = metadata.Size,
                    ImportedAt = DateTime.UtcNow,
                    Tags = metadata.Tags != null ? new List<string>(metadata.Tags) : new List<string>()
                };

                Register(hash, newMetadata);
                cloned = true;
            }
        }

        if (cloned)
        {
            Save();
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }
}
