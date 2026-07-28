using System.Text.Json;

namespace MapEngine.Core.Assets;

public static class AssetImporter
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
    };

    public sealed class ImportResult
    {
        public required string AssetFilePath { get; init; }
        public required string ImageFilePath { get; init; }
        public required string DisplayName { get; init; }
        public required Guid AssetGuid { get; init; }
    }

    public static ImportResult? ImportImage(string sourceImagePath, string targetFolder, AssetDatabase? assetDb = null)
    {
        if (!File.Exists(sourceImagePath))
            return null;

        var ext = Path.GetExtension(sourceImagePath);
        if (!SupportedImageExtensions.Contains(ext))
            return null;

        if (!Directory.Exists(targetFolder))
            Directory.CreateDirectory(targetFolder);

        var baseName = Path.GetFileNameWithoutExtension(sourceImagePath);
        var imageDest = GetUniquePath(targetFolder, baseName, ext);
        File.Copy(sourceImagePath, imageDest, overwrite: false);

        var assetPath = Path.ChangeExtension(imageDest, ".asset");
        var assetContent = new
        {
            name = Path.GetFileNameWithoutExtension(imageDest),
            type = "StaticObjectClass",
            components = new object[]
            {
                new { type = "Transform", properties = new { x = 0, y = 0, scaleX = 1, scaleY = 1 } },
                new { type = "SpriteRenderer", properties = new { sourceAssetPath = imageDest, sourceAssetKind = "Image", opacity = 1.0 } }
            }
        };

        var json = JsonSerializer.Serialize(assetContent, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(assetPath, json);

        var guid = assetDb?.GetOrAssignGuid(assetPath) ?? Guid.NewGuid();

        return new ImportResult
        {
            AssetFilePath = assetPath,
            ImageFilePath = imageDest,
            DisplayName = Path.GetFileNameWithoutExtension(imageDest),
            AssetGuid = guid
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
