using System;
using System.Collections.Generic;

namespace MapEngine.Core.Assets;

/// <summary>资产元数据（asset-index.json 中的条目）。</summary>
public sealed class AssetMetadata
{
    /// <summary>内容哈希（SHA256）。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>哈希算法（默认 sha256）。</summary>
    public string Algorithm { get; set; } = "sha256";

    /// <summary>相对于素材库根目录的路径（如 "tokens/战士.png"）。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>文件名（如 "战士.png"）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME 类型（如 "image/png"）。</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long Size { get; set; }

    /// <summary>图片宽度（像素），可选。</summary>
    public int? Width { get; set; }

    /// <summary>图片高度（像素），可选。</summary>
    public int? Height { get; set; }

    /// <summary>导入时间。</summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>标签列表（如 ["token", "player"]），可选。</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>资产索引（asset-index.json 的根结构）。</summary>
public sealed class AssetIndex
{
    /// <summary>索引格式版本。</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>哈希 → 元数据映射。</summary>
    public Dictionary<string, AssetMetadata> Assets { get; set; } = new();
}
