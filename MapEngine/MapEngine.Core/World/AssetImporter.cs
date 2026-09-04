using System.Text.Json;
using MapEngine.Core.Utilities;

namespace MapEngine.Core.Assets;

public static class AssetImporter
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
    };

    public enum ImportStatus
    {
        Success,
        DuplicateContent,
        UnsupportedFormat,
        FileNotFound
    }

    public sealed class ImportResult
    {
        public required ImportStatus Status { get; init; }
        public required string ImageHash { get; init; }
        public string? AssetFilePath { get; init; }
        public string? ImageFilePath { get; init; }
        public string? DisplayName { get; init; }
        public Guid? AssetGuid { get; init; }
        public bool IsNewImage { get; init; }
    }

    /// <summary>
    /// 导入图片到素材库（支持语义命名、层级结构、元数据索引）。
    /// </summary>
    /// <param name="sourceImagePath">源图片路径</param>
    /// <param name="targetFolder">目标文件夹（素材库根目录）</param>
    /// <param name="targetSubfolder">子文件夹（如 "tokens" / "maps" / "tiles"），可选</param>
    /// <param name="metadataStore">元数据存储（用于去重和路径查询），可选</param>
    /// <param name="assetDb">资产数据库（用于 GUID 分配），可选</param>
    /// <returns>导入结果，包含状态、哈希、路径等信息</returns>
    public static ImportResult? ImportImage(
        string sourceImagePath,
        string targetFolder,
        string? targetSubfolder = null,
        AssetMetadataStore? metadataStore = null,
        AssetDatabase? assetDb = null)
    {
        if (!File.Exists(sourceImagePath))
        {
            return new ImportResult
            {
                Status = ImportStatus.FileNotFound,
                ImageHash = string.Empty
            };
        }

        var ext = Path.GetExtension(sourceImagePath);
        if (!SupportedImageExtensions.Contains(ext))
        {
            return new ImportResult
            {
                Status = ImportStatus.UnsupportedFormat,
                ImageHash = string.Empty
            };
        }

        // 1. 计算源图片的内容哈希
        var hash = ContentHasher.ComputeHash(sourceImagePath);
        var hashWithPrefix = $"sha256:{hash}";

        // 2. 去重检测（基于内容哈希）
        if (metadataStore?.ContainsHash(hash) == true)
        {
            return new ImportResult
            {
                Status = ImportStatus.DuplicateContent,
                ImageHash = hashWithPrefix
            };
        }

        // 3. 确定目标路径（支持层级结构）
        var actualTargetFolder = string.IsNullOrWhiteSpace(targetSubfolder)
            ? targetFolder
            : Path.Combine(targetFolder, targetSubfolder);

        if (!Directory.Exists(actualTargetFolder))
            Directory.CreateDirectory(actualTargetFolder);

        // 4. 语义命名（保留原始文件名）
        var originalName = Path.GetFileName(sourceImagePath);
        var imagePath = GetUniquePath(actualTargetFolder, Path.GetFileNameWithoutExtension(originalName), ext);
        File.Copy(sourceImagePath, imagePath, overwrite: false);

        // 5. 计算相对路径（用于 metadata）
        var relativePath = string.IsNullOrWhiteSpace(targetSubfolder)
            ? Path.GetFileName(imagePath)
            : Path.Combine(targetSubfolder, Path.GetFileName(imagePath));

        // 6. 注册到元数据索引
        if (metadataStore != null)
        {
            var fileInfo = new FileInfo(imagePath);
            metadataStore.Register(hash, new AssetMetadata
            {
                Hash = hash,
                Algorithm = "sha256",
                RelativePath = relativePath.Replace('\\', '/'),
                FileName = Path.GetFileName(imagePath),
                MimeType = GetMimeType(ext),
                Size = fileInfo.Length,
                ImportedAt = DateTime.UtcNow,
                Tags = new List<string>()
            });
        }

        // 7. 创建 .asset 文件（用户友好的名称）
        var baseName = Path.GetFileNameWithoutExtension(imagePath);
        var assetPath = GetUniquePath(actualTargetFolder, baseName, ".asset");

        var assetContent = new
        {
            name = Path.GetFileNameWithoutExtension(assetPath),
            type = "StaticObjectClass",
            assetRef = hashWithPrefix,  // 存储 sha256: 前缀的哈希引用
            components = new object[]
            {
                new { type = "Transform", properties = new { x = 0, y = 0, scaleX = 1, scaleY = 1 } },
                new { type = "SpriteRenderer", properties = new { assetRef = hashWithPrefix, sourceAssetKind = "Image", opacity = 1.0 } }
            }
        };

        var json = JsonSerializer.Serialize(assetContent, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(assetPath, json);

        var guid = assetDb?.GetOrAssignGuid(assetPath) ?? Guid.NewGuid();

        return new ImportResult
        {
            Status = ImportStatus.Success,
            AssetFilePath = assetPath,
            ImageFilePath = imagePath,
            ImageHash = hashWithPrefix,
            DisplayName = Path.GetFileNameWithoutExtension(assetPath),
            AssetGuid = guid,
            IsNewImage = true
        };
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private static string GetUniquePath(string folder, string baseName, string extension)
    {
        var candidate = Path.Combine(folder, baseName + extension);
        if (!File.Exists(candidate))
            return candidate;

        for (int i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{baseName} {i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(folder, $"{baseName}_{Guid.NewGuid():N}{extension}");
    }
}
