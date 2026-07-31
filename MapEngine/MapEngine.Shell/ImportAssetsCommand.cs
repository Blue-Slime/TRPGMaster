using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SixLabors.ImageSharp;

namespace MapEngine.Shell;

/// <summary>
/// 批量导入图片并生成 .asset 文件的命令行工具
/// </summary>
public static class ImportAssetsCommand
{
    private const string AssetExtension = ".asset";

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static void Execute(string? sourcePath = null, string? targetRoot = null)
    {
        sourcePath ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        targetRoot ??= Path.Combine(Directory.GetCurrentDirectory(), "AssetLibrary");

        Console.WriteLine($"[ImportAssets] 源文件夹: {sourcePath}");
        Console.WriteLine($"[ImportAssets] 目标根目录: {targetRoot}");

        if (!Directory.Exists(sourcePath))
        {
            Console.WriteLine($"[错误] 源文件夹不存在: {sourcePath}");
            return;
        }

        var imageFiles = Directory.GetFiles(sourcePath)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToArray();

        Console.WriteLine($"[ImportAssets] 找到 {imageFiles.Length} 个图片文件");

        var stats = new Dictionary<string, int>
        {
            ["Tokens"] = 0,
            ["Maps"] = 0,
            ["Props"] = 0,
            ["UI"] = 0
        };

        foreach (var imageFile in imageFiles)
        {
            try
            {
                var category = ClassifyImage(imageFile);
                var targetDir = Path.Combine(targetRoot, category);
                Directory.CreateDirectory(targetDir);

                var fileName = Path.GetFileName(imageFile);
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var targetImagePath = Path.Combine(targetDir, fileName);
                var targetAssetPath = Path.Combine(targetDir, baseName + AssetExtension);

                // 复制图片
                File.Copy(imageFile, targetImagePath, overwrite: true);

                // 生成 .asset 文件
                var assetContent = new
                {
                    Name = baseName,
                    Type = "StaticObjectClass",
                    Components = new object[]
                    {
                        new
                        {
                            Type = "Transform",
                            Properties = new Dictionary<string, string>()
                        },
                        new
                        {
                            Type = "SpriteRenderer",
                            Properties = new Dictionary<string, string>
                            {
                                ["sprite"] = fileName
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(assetContent, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                File.WriteAllText(targetAssetPath, json);

                stats[category]++;
                Console.WriteLine($"  ✓ {category}/{fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {Path.GetFileName(imageFile)}: {ex.Message}");
            }
        }

        Console.WriteLine("\n[完成] 导入统计:");
        Console.WriteLine($"  - Tokens: {stats["Tokens"]} 个");
        Console.WriteLine($"  - Maps: {stats["Maps"]} 个");
        Console.WriteLine($"  - Props: {stats["Props"]} 个");
        Console.WriteLine($"  - UI: {stats["UI"]} 个");
        Console.WriteLine($"  总计: {stats.Values.Sum()} 个素材");
    }

    private static string ClassifyImage(string imagePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant();

        // 按文件名模式匹配（优先级从高到低）
        if (ContainsAny(fileName, "logo", "icon", "头像", "壁纸"))
            return "UI";

        if (ContainsAny(fileName, "token", "角色", "人物"))
            return "Tokens";

        if (ContainsAny(fileName, "map", "地图"))
            return "Maps";

        if (ContainsAny(fileName, "prop", "道具"))
            return "Props";

        // 按图片尺寸分类
        try
        {
            using var image = Image.Load(imagePath);
            var width = image.Width;
            var height = image.Height;
            var aspectRatio = (double)width / height;

            // 小方图 → Token
            if (width < 800 && aspectRatio >= 0.8 && aspectRatio <= 1.2)
                return "Tokens";

            // 大图或宽图 → Map
            if (width >= 800 || aspectRatio > 1.5)
                return "Maps";

            // 默认 → Props
            return "Props";
        }
        catch
        {
            // 无法读取尺寸，默认分类为 Props
            return "Props";
        }
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
