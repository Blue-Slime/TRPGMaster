using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapEngine.Avalonia.Services;

public static class MapSpriteAssetResolver
{
    private static readonly ConcurrentDictionary<string, string?> SpritePathCache = new(StringComparer.OrdinalIgnoreCase);

    public static string? ResolveSpritePath(string? sourceAssetPath, string? sourceAssetKind)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            return null;
        }

        var normalizedPath = Path.IsPathRooted(sourceAssetPath)
            ? Path.GetFullPath(sourceAssetPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sourceAssetPath));

        return SpritePathCache.GetOrAdd(normalizedPath, path => ResolveSpritePathCore(path, sourceAssetKind));
    }

    public static void Invalidate(string? sourceAssetPath)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
        {
            return;
        }

        SpritePathCache.TryRemove(Path.GetFullPath(sourceAssetPath), out _);
    }

    private static string? ResolveSpritePathCore(string sourceAssetPath, string? sourceAssetKind)
    {
        if (!File.Exists(sourceAssetPath))
        {
            return null;
        }

        if (string.Equals(sourceAssetKind, "Image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceAssetKind, "AnimatedImage", StringComparison.OrdinalIgnoreCase))
        {
            return sourceAssetPath;
        }

        if (!sourceAssetPath.EndsWith(AssetLibraryFileSystemService.StaticObjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(sourceAssetPath);
            var document = JsonSerializer.Deserialize<StaticObjectAssetDocument>(json);
            var spriteReference = document?.Components
                .FirstOrDefault(component => component.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))
                ?.Properties.TryGetValue("sprite", out var spritePath) == true
                    ? spritePath
                    : null;

            if (string.IsNullOrWhiteSpace(spriteReference))
            {
                return null;
            }

            var assetDirectory = Path.GetDirectoryName(sourceAssetPath) ?? string.Empty;
            var resolvedPath = Path.IsPathRooted(spriteReference)
                ? spriteReference
                : Path.Combine(assetDirectory, spriteReference);

            return File.Exists(resolvedPath)
                ? Path.GetFullPath(resolvedPath)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
