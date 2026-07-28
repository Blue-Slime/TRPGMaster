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

    public static string RenameFolder(string folderPath, string newName)
    {
        var parentPath = Path.GetDirectoryName(folderPath)
            ?? throw new InvalidOperationException("素材文件夹缺少父目录。");
        var targetPath = BuildUniqueRenamePath(parentPath, newName, Path.GetFileName(folderPath));
        Directory.Move(folderPath, targetPath);
        return targetPath;
    }

    public static void DeleteFolder(string folderPath)
        => Directory.Delete(folderPath, recursive: true);

    public static string CopyFolderToParent(string sourceFolderPath, string targetParentPath)
    {
        var targetPath = GetUniqueDirectoryPath(targetParentPath, Path.GetFileName(sourceFolderPath));
        CopyDirectory(sourceFolderPath, targetPath);
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

    public static string RenameFile(string filePath, string newName)
    {
        var parentPath = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("素材文件缺少父目录。");
        var targetFileName = PreserveKnownExtension(Path.GetFileName(filePath), newName);
        var targetPath = BuildUniqueRenamePath(parentPath, targetFileName, Path.GetFileName(filePath));
        File.Move(filePath, targetPath);
        SyncStaticObjectName(targetPath);
        return targetPath;
    }

    public static void DeleteFile(string filePath)
        => File.Delete(filePath);

    public static string CopyFileToFolder(string sourceFilePath, string targetFolderPath)
    {
        var targetPath = GetUniqueFilePath(targetFolderPath, Path.GetFileName(sourceFilePath));
        File.Copy(sourceFilePath, targetPath);
        SyncStaticObjectName(targetPath);
        return targetPath;
    }

    private static string ResolveRootPath(string? configuredRootFolder)
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(configuredRootFolder))
        {
            return Path.IsPathRooted(configuredRootFolder)
                ? configuredRootFolder
                : Path.Combine(currentDirectory, configuredRootFolder);
        }

        var projectPath = Path.Combine(currentDirectory, DefaultRootFolderName);
        if (File.Exists(Path.Combine(currentDirectory, "MapVttApp.csproj")) || Directory.Exists(projectPath))
        {
            return projectPath;
        }

        return Path.Combine(AppContext.BaseDirectory, DefaultRootFolderName);
    }

    private static void EnsureRootScaffold(string rootPath)
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
                            Properties = new Dictionary<string, string>
                            {
                                ["sprite"] = "example.png"
                            }
                        }
                    ]
                },
                JsonOptions);
            File.WriteAllText(samplePath, json);
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

        foreach (var directory in Directory.GetDirectories(path).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            folder.Children.Add(BuildFolder(directory));
        }

        return folder;
    }

    private static void CollectItems(AssetFolderDto folder, List<AssetItemDto> items)
    {
        foreach (var filePath in Directory.GetFiles(folder.FullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(BuildItem(filePath, folder.FullPath));
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
        Directory.CreateDirectory(targetPath);

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
