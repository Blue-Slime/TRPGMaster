using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapEngine.Avalonia.Services;

public static class AssetLibraryFileSystemService
{
    public const string DefaultRootFolderName = "AssetLibrary";
    public const string StaticObjectExtension = ".asset";

    public static AssetLibrarySnapshot Load(string? configuredRootFolder = null)
    {
        var rootPath = ResolveRootPath(configuredRootFolder);
        EnsureRootScaffold(rootPath);

        var rootFolder = BuildFolder(rootPath);
        var items = new List<AssetItemDto>();
        CollectItems(rootFolder, items);

        return new AssetLibrarySnapshot
        {
            RootPath = rootPath,
            RootFolders = [rootFolder],
            AssetItems = items
        };
    }

    public static string CreateFolder(string parentPath, string folderName)
    {
        var targetPath = GetUniqueDirectoryPath(parentPath, folderName);
        Directory.CreateDirectory(targetPath);
        return targetPath;
    }

    /// <param name="libraryRoot">
    /// 当前生效的素材库根（本地库或房间库）。必须由调用方传入：
    /// 用 ResolveRootPath(null) 永远拿到本地库根，会把房间库的改动写进本地索引（跨库污染）。
    /// </param>
    public static string RenameFolder(string folderPath, string newName, string libraryRoot)
    {
        var parentPath = Path.GetDirectoryName(folderPath)
            ?? throw new InvalidOperationException("素材文件夹缺少父目录。");
        var targetPath = BuildUniqueRenamePath(parentPath, newName, Path.GetFileName(folderPath));

        // 更新元数据索引中所有子文件的 relativePath
        var metadataStore = new MapEngine.Core.Assets.AssetMetadataStore(libraryRoot);
        var oldPrefix = Path.GetRelativePath(libraryRoot, folderPath);
        var newPrefix = Path.GetRelativePath(libraryRoot, targetPath);
        metadataStore.UpdateRelativePathPrefix(oldPrefix, newPrefix);

        Directory.Move(folderPath, targetPath);
        return targetPath;
    }

    public static void DeleteFolder(string folderPath)
        => Directory.Delete(folderPath, recursive: true);

    /// <param name="libraryRoot">当前生效的素材库根，见 <see cref="RenameFolder"/>。</param>
    public static string CopyFolderToParent(string sourceFolderPath, string targetParentPath, string libraryRoot)
    {
        var targetPath = GetUniqueDirectoryPath(targetParentPath, Path.GetFileName(sourceFolderPath));

        // 复制文件夹并更新元数据索引
        var metadataStore = new MapEngine.Core.Assets.AssetMetadataStore(libraryRoot);
        var sourcePrefix = Path.GetRelativePath(libraryRoot, sourceFolderPath);
        var targetPrefix = Path.GetRelativePath(libraryRoot, targetPath);

        CopyDirectory(sourceFolderPath, targetPath);

        // 为所有复制的文件创建新的元数据条目
        metadataStore.ClonePathPrefix(sourcePrefix, targetPrefix);

        return targetPath;
    }

    public static string CreateStaticObjectFile(string folderPath, string fileName)
    {
        var targetPath = GetUniqueFilePath(folderPath, EnsureStaticObjectFileName(fileName));
        var payload = new StaticObjectAssetDocument
        {
            Name = Path.GetFileNameWithoutExtension(Path.GetFileName(targetPath)),
            Components =
            [
                new StaticObjectComponentDocument
                {
                    Type = "Transform"
                }
            ]
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(targetPath, json);
        return targetPath;
    }

    /// <param name="libraryRoot">当前生效的素材库根，见 <see cref="RenameFolder"/>。</param>
    public static string RenameFile(string filePath, string newName, string libraryRoot)
    {
        var parentPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("素材文件缺少父目录。");
        var targetFileName = PreserveKnownExtension(Path.GetFileName(filePath), newName);
        var targetPath = BuildUniqueRenamePath(parentPath, targetFileName, Path.GetFileName(filePath));

        // 更新元数据索引中的 relativePath
        var metadataStore = new MapEngine.Core.Assets.AssetMetadataStore(libraryRoot);
        var oldRelativePath = Path.GetRelativePath(libraryRoot, filePath);
        var newRelativePath = Path.GetRelativePath(libraryRoot, targetPath);
        metadataStore.UpdateRelativePath(oldRelativePath, newRelativePath);

        File.Move(filePath, targetPath);
        SyncStaticObjectName(targetPath);
        return targetPath;
    }

    public static void DeleteFile(string filePath)
        => File.Delete(filePath);

    /// <param name="libraryRoot">当前生效的素材库根，见 <see cref="RenameFolder"/>。</param>
    public static string CopyFileToFolder(string sourceFilePath, string targetFolderPath, string libraryRoot)
    {
        var targetPath = GetUniqueFilePath(targetFolderPath, Path.GetFileName(sourceFilePath));

        // 复制文件并更新元数据索引
        var metadataStore = new MapEngine.Core.Assets.AssetMetadataStore(libraryRoot);
        var sourceRelativePath = Path.GetRelativePath(libraryRoot, sourceFilePath);
        var targetRelativePath = Path.GetRelativePath(libraryRoot, targetPath);

        File.Copy(sourceFilePath, targetPath);

        // 如果源文件在元数据中，为目标文件创建新条目（保持相同哈希）
        var sourceHash = metadataStore.GetHashByPath(sourceRelativePath);
        if (sourceHash != null)
        {
            var sourceMeta = metadataStore.GetMetadata(sourceHash);
            if (sourceMeta != null)
            {
                var newMeta = new MapEngine.Core.Assets.AssetMetadata
                {
                    Hash = sourceMeta.Hash,
                    Algorithm = sourceMeta.Algorithm,
                    RelativePath = targetRelativePath,
                    FileName = Path.GetFileName(targetPath),
                    MimeType = sourceMeta.MimeType,
                    Size = sourceMeta.Size,
                    ImportedAt = DateTime.UtcNow,
                    Tags = sourceMeta.Tags != null ? new List<string>(sourceMeta.Tags) : new List<string>()
                };
                metadataStore.Register(sourceHash, newMeta);
            }
        }

        SyncStaticObjectName(targetPath);
        return targetPath;
    }

    private static string ResolveRootPath(string? configuredRootFolder)
    {
        var settings = GlobalSettingsStore.Load();

        // 三级优先级路径解析
        switch (settings.MapModuleAssetPathMode)
        {
            case AssetPathMode.Global:
                // P1: 使用全局配置路径（由启动器设置）
                var globalPath = GlobalConfigStore.TryGetAssetLibraryPath();
                if (!string.IsNullOrWhiteSpace(globalPath))
                {
                    return Path.IsPathRooted(globalPath)
                        ? globalPath
                        : Path.Combine(AppContext.BaseDirectory, globalPath);
                }
                // 全局配置不存在或为空时，降级到模块默认路径
                break;

            case AssetPathMode.ModuleDefault:
                // P2: 强制使用模块默认路径（用户手动选择）
                break;

            case AssetPathMode.Custom:
                // P3: 使用模块自定义路径
                if (!string.IsNullOrWhiteSpace(settings.MapModuleCustomAssetPath))
                {
                    return Path.IsPathRooted(settings.MapModuleCustomAssetPath)
                        ? settings.MapModuleCustomAssetPath
                        : Path.Combine(AppContext.BaseDirectory, settings.MapModuleCustomAssetPath);
                }
                // 自定义路径为空时，降级到模块默认路径
                break;
        }

        // 降级：模块默认路径（可执行文件目录/AssetLibrary）
        return GetModuleDefaultPath();
    }

    /// <summary>获取模块默认路径（开发环境兼容）。</summary>
    private static string GetModuleDefaultPath()
    {
        var defaultPath = Path.Combine(AppContext.BaseDirectory, DefaultRootFolderName);

        // 开发环境兼容：如果当前目录是项目根目录（有 .csproj 文件），使用当前目录
        var currentDirectory = Directory.GetCurrentDirectory();
        var projectPath = Path.Combine(currentDirectory, DefaultRootFolderName);
        if (File.Exists(Path.Combine(currentDirectory, "MapEngine.Shell.csproj"))
            || File.Exists(Path.Combine(currentDirectory, "MapEngine.Avalonia.csproj")))
        {
            return projectPath;
        }

        return defaultPath;
    }

    /// <summary>解析当前生效的素材库根路径（公开方法供设置对话框使用）。</summary>
    public static string ResolveEffectiveRootPath()
    {
        return ResolveRootPath(null);
    }

    private static void EnsureRootScaffold(string rootPath)
    {
        try
        {
            Directory.CreateDirectory(rootPath);

            var staticObjectsPath = Path.Combine(rootPath, "StaticObjects");
            Directory.CreateDirectory(staticObjectsPath);

            var samplePath = Path.Combine(staticObjectsPath, $"示例静态对象{StaticObjectExtension}");
            if (!File.Exists(samplePath))
            {
                var json = JsonSerializer.Serialize(
                    new StaticObjectAssetDocument
                    {
                        Name = "示例静态对象",
                        Components =
                        [
                            new StaticObjectComponentDocument
                            {
                                Type = "Transform"
                            },
                            new StaticObjectComponentDocument
                            {
                                Type = "SpriteRenderer",
                                Properties = []
                            }
                        ]
                    },
                    JsonOptions);
                File.WriteAllText(samplePath, json);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 无权创建目录，素材库将为空
        }
        catch (IOException)
        {
            // 磁盘或路径问题，素材库将为空
        }
    }

    private static AssetFolderDto BuildFolder(string path)
    {
        var folder = new AssetFolderDto
        {
            Id = NormalizePath(path),
            Name = Path.GetFileName(path),
            FullPath = path
        };

        try
        {
            if (Directory.Exists(path))
            {
                foreach (var directory in Directory.GetDirectories(path).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    folder.Children.Add(BuildFolder(directory));
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 目录在扫描期间被删除，返回空文件夹
        }
        catch (UnauthorizedAccessException)
        {
            // 无权访问子目录，返回空文件夹
        }

        return folder;
    }

    private static void CollectItems(AssetFolderDto folder, List<AssetItemDto> items)
    {
        try
        {
            if (Directory.Exists(folder.FullPath))
            {
                foreach (var filePath in Directory.GetFiles(folder.FullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(BuildItem(filePath, folder.FullPath));
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 目录在扫描期间被删除，跳过
        }
        catch (UnauthorizedAccessException)
        {
            // 无权访问目录，跳过
        }

        foreach (var child in folder.Children)
        {
            CollectItems(child, items);
        }
    }

    private static AssetItemDto BuildItem(string filePath, string folderPath)
    {
        var fileName = Path.GetFileName(filePath);
        var metadata = BuildMetadata(filePath);

        return new AssetItemDto
        {
            Id = NormalizePath(filePath),
            FolderId = NormalizePath(folderPath),
            Name = metadata.DisplayName,
            FileName = fileName,
            Kind = metadata.Kind,
            Icon = metadata.Icon,
            Description = metadata.Description,
            FullPath = filePath
        };
    }

    private static AssetFileMetadata BuildMetadata(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return BuildStaticObjectMetadata(filePath);
        }

        var info = new FileInfo(filePath);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var (kind, icon) = extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" => ("Image", "🖼️"),
            ".gif" => ("AnimatedImage", "🎞️"),
            ".prefab" => ("Prefab", "🧩"),
            ".map" or ".tmx" => ("Map", "🗺️"),
            ".wav" or ".mp3" or ".ogg" => ("Audio", "🔊"),
            _ => ("File", "📄")
        };

        return new AssetFileMetadata
        {
            DisplayName = fileName,
            Kind = kind,
            Icon = icon,
            Description = $"{info.Extension}  {Math.Max(1, info.Length / 1024)} KB"
        };
    }

    private static AssetFileMetadata BuildStaticObjectMetadata(string filePath)
    {
        var info = new FileInfo(filePath);
        try
        {
            var json = File.ReadAllText(filePath);
            var document = JsonSerializer.Deserialize<StaticObjectAssetDocument>(json, JsonOptions);
            var componentSummary = document?.Components.Count > 0
                ? string.Join(", ", document.Components.Select(component => component.Type))
                : "无组件";

            return new AssetFileMetadata
            {
                DisplayName = document?.Name?.Trim() is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(filePath),
                Kind = document?.Type?.Trim() is { Length: > 0 } kind ? kind : "StaticObjectClass",
                Icon = ResolveAssetIcon(document),
                Description = $"{componentSummary}  {Math.Max(1, info.Length / 1024)} KB"
            };
        }
        catch
        {
            return new AssetFileMetadata
            {
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                Kind = "StaticObjectClass",
                Icon = "📦",
                Description = $".asset  {Math.Max(1, info.Length / 1024)} KB"
            };
        }
    }

    private static string EnsureStaticObjectFileName(string fileName)
    {
        if (fileName.EndsWith(StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var sanitized = fileName.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "新建静态对象类";
        }

        if (sanitized.EndsWith(StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return sanitized;
        }

        return $"{sanitized}{StaticObjectExtension}";
    }

    private static string PreserveKnownExtension(string oldFileName, string newName)
    {
        if (newName.Contains('.'))
        {
            return newName;
        }

        if (oldFileName.EndsWith(StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return $"{newName}{StaticObjectExtension}";
        }

        return $"{newName}{Path.GetExtension(oldFileName)}";
    }

    private static string GetUniqueDirectoryPath(string parentPath, string folderName)
    {
        var candidate = Path.Combine(parentPath, folderName);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        var index = 2;
        while (Directory.Exists(Path.Combine(parentPath, $"{folderName} {index}")))
        {
            index++;
        }

        return Path.Combine(parentPath, $"{folderName} {index}");
    }

    private static string GetUniqueFilePath(string folderPath, string fileName)
    {
        var candidate = Path.Combine(folderPath, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = fileName[..^extension.Length];
        var index = 2;
        while (File.Exists(Path.Combine(folderPath, $"{baseName} {index}{extension}")))
        {
            index++;
        }

        return Path.Combine(folderPath, $"{baseName} {index}{extension}");
    }

    private static string BuildUniqueRenamePath(string parentPath, string requestedName, string originalName)
    {
        var candidate = Path.Combine(parentPath, requestedName);
        if (!Path.GetFileName(candidate).Equals(originalName, StringComparison.OrdinalIgnoreCase)
            && (File.Exists(candidate) || Directory.Exists(candidate)))
        {
            var extension = Path.GetExtension(requestedName);
            var baseName = string.IsNullOrEmpty(extension) ? requestedName : requestedName[..^extension.Length];
            var index = 2;
            while (File.Exists(Path.Combine(parentPath, $"{baseName} {index}{extension}"))
                || Directory.Exists(Path.Combine(parentPath, $"{baseName} {index}{extension}")))
            {
                index++;
            }

            candidate = Path.Combine(parentPath, $"{baseName} {index}{extension}");
        }

        return candidate;
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            return; // 源目录不存在，跳过复制
        }

        Directory.CreateDirectory(targetPath);

        try
        {
            foreach (var filePath in Directory.GetFiles(sourcePath))
            {
                File.Copy(filePath, Path.Combine(targetPath, Path.GetFileName(filePath)));
            }

            foreach (var directoryPath in Directory.GetDirectories(sourcePath))
            {
                var childTargetPath = Path.Combine(targetPath, Path.GetFileName(directoryPath));
                CopyDirectory(directoryPath, childTargetPath);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 源目录在复制期间被删除，跳过
        }
        catch (UnauthorizedAccessException)
        {
            // 无权访问源目录，跳过
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();

    private static void SyncStaticObjectName(string filePath)
    {
        if (!filePath.EndsWith(StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var document = JsonSerializer.Deserialize<StaticObjectAssetDocument>(json, JsonOptions) ?? new StaticObjectAssetDocument();
            document.Name = Path.GetFileNameWithoutExtension(filePath);
            File.WriteAllText(filePath, JsonSerializer.Serialize(document, JsonOptions));
        }
        catch
        {
            // Ignore malformed .asset content and keep file operation successful.
        }
    }

    private static string ResolveAssetIcon(StaticObjectAssetDocument? document)
    {
        if (document?.Components.Any(component => component.Type.Equals("AudioSource", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return "🔊";
        }

        if (document?.Components.Any(component => component.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return "🖼️";
        }

        return "📦";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}

public sealed class AssetLibrarySnapshot
{
    public string RootPath { get; set; } = string.Empty;

    public List<AssetFolderDto> RootFolders { get; set; } = [];

    public List<AssetItemDto> AssetItems { get; set; } = [];
}

public sealed class StaticObjectAssetDocument
{
    public string Name { get; set; } = "新建静态对象类";

    public string Type { get; set; } = "StaticObjectClass";

    public List<StaticObjectComponentDocument> Components { get; set; } = [];
}

public sealed class StaticObjectComponentDocument
{
    public string Type { get; set; } = "Transform";

    public Dictionary<string, string> Properties { get; set; } = [];
}

public sealed class AssetFileMetadata
{
    public string DisplayName { get; set; } = string.Empty;

    public string Kind { get; set; } = "File";

    public string Icon { get; set; } = "📄";

    public string Description { get; set; } = string.Empty;
}
