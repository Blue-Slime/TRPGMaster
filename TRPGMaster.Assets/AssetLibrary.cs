using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using TRPGMaster.Assets.Models;

namespace TRPGMaster.Assets;

public sealed class AssetLibrary
{
    private readonly string _root;
    private readonly string _filesDir;
    private readonly string _thumbsDir;
    private readonly string _manifestPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private AssetManifest _manifest;
    private readonly Dictionary<string, SKBitmap> _bitmapCache = new();

    public AssetLibrary(string rootDirectory)
    {
        _root = rootDirectory;
        _filesDir = Path.Combine(_root, "files");
        _thumbsDir = Path.Combine(_root, "thumbs");
        _manifestPath = Path.Combine(_root, "manifest.json");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_filesDir);
        Directory.CreateDirectory(_thumbsDir);

        _manifest = LoadManifest();
    }

    public IReadOnlyDictionary<string, AssetEntry> Assets => _manifest.Assets;

    public AssetEntry ImportImage(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Source file not found.", sourceFilePath);

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext)) throw new InvalidOperationException("Missing file extension.");

        var sha = ComputeSha256Hex(sourceFilePath);
        var id = sha;

        if (_manifest.Assets.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var relPath = Path.Combine("files", id + ext);
        var dstPath = Path.Combine(_root, relPath);

        if (!File.Exists(dstPath))
        {
            File.Copy(sourceFilePath, dstPath, overwrite: false);
        }

        int? width = null;
        int? height = null;
        string? thumbRel = null;

        using (var bmp = SKBitmap.Decode(dstPath))
        {
            if (bmp is not null)
            {
                width = bmp.Width;
                height = bmp.Height;

                var thumb = CreateThumbnail(bmp, 256);
                if (thumb is not null)
                {
                    thumbRel = Path.Combine("thumbs", id + ".png");
                    var thumbPath = Path.Combine(_root, thumbRel);
                    using var image = SKImage.FromBitmap(thumb);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                    using var fs = File.Open(thumbPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    data.SaveTo(fs);
                }
            }
        }

        var entry = new AssetEntry
        {
            Id = id,
            Type = "image",
            Sha256 = sha,
            FileName = Path.GetFileName(sourceFilePath),
            FileExtension = ext,
            RelativePath = NormalizeSlashes(relPath),
            ThumbnailRelativePath = thumbRel is null ? null : NormalizeSlashes(thumbRel),
            Width = width,
            Height = height
        };

        _manifest.Assets[id] = entry;
        SaveManifest();
        return entry;
    }

    public SKBitmap? GetBitmap(string assetId)
    {
        if (_bitmapCache.TryGetValue(assetId, out var cached)) return cached;
        if (!_manifest.Assets.TryGetValue(assetId, out var entry)) return null;

        var fullPath = Path.Combine(_root, entry.RelativePath);
        if (!File.Exists(fullPath)) return null;

        var bmp = SKBitmap.Decode(fullPath);
        if (bmp is null) return null;
        _bitmapCache[assetId] = bmp;
        return bmp;
    }

    private AssetManifest LoadManifest()
    {
        if (!File.Exists(_manifestPath)) return new AssetManifest();
        var json = File.ReadAllText(_manifestPath);
        var manifest = JsonSerializer.Deserialize<AssetManifest>(json, _jsonOptions);
        return manifest ?? new AssetManifest();
    }

    private void SaveManifest()
    {
        var json = JsonSerializer.Serialize(_manifest, _jsonOptions);
        File.WriteAllText(_manifestPath, json);
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static SKBitmap? CreateThumbnail(SKBitmap source, int maxSize)
    {
        var w = source.Width;
        var h = source.Height;
        if (w <= 0 || h <= 0) return null;

        var scale = Math.Min((float)maxSize / w, (float)maxSize / h);
        if (scale >= 1f) return source.Copy();

        var newW = Math.Max(1, (int)Math.Round(w * scale));
        var newH = Math.Max(1, (int)Math.Round(h * scale));

        var resized = new SKBitmap(newW, newH);
        using var canvas = new SKCanvas(resized);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, newW, newH), paint);
        return resized;
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
