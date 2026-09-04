using System;
using System.Collections.Generic;

namespace MasterIM.Server.MapPersistence;

/// <summary>
/// 素材元数据：存储在 assets/index.json 中
/// </summary>
public class AssetMetadata
{
    /// <summary>内容哈希（唯一标识）</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>素材类型（token/map/audio/file）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>原始文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>相对路径（语义命名架构：如 "tokens/战士.png"）</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>MIME 类型</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>图片宽度（仅图片类型）</summary>
    public int? Width { get; set; }

    /// <summary>图片高度（仅图片类型）</summary>
    public int? Height { get; set; }

    /// <summary>上传者用户ID</summary>
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>上传时间</summary>
    public DateTime UploadedAt { get; set; }

    /// <summary>引用计数（被多少对象引用）</summary>
    public int RefCount { get; set; }

    /// <summary>引用列表（哪些对象在使用）</summary>
    public List<AssetReference> References { get; set; } = new();

    /// <summary>标签（用于搜索和分类）</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// 素材引用：记录哪个对象在使用该素材
/// </summary>
public class AssetReference
{
    /// <summary>引用类型（scene/prefab/message）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>引用路径或ID</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>对象ID（scene 类型时使用）</summary>
    public string? ObjectId { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is AssetReference other &&
               Type == other.Type &&
               Path == other.Path &&
               ObjectId == other.ObjectId;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Path, ObjectId);
    }
}
