using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapEngine.Avalonia.Services;

/// <summary>素材库路径来源模式（在地图模块设置界面选择）。</summary>
public enum AssetPathMode
{
    /// <summary>P1：使用全局配置路径（由启动器设置）。查不到时降级到模块默认路径。</summary>
    Global = 0,

    /// <summary>P2：强制使用地图模块默认路径（可执行文件目录下的 AssetLibrary）。</summary>
    ModuleDefault = 1,

    /// <summary>P3：使用模块自定义路径。路径为空时降级到模块默认路径。</summary>
    Custom = 2
}

public sealed class GlobalSettings
{
    public string PackageName { get; set; } = "默认地图包";
    public int FeetPerCell { get; set; } = 5;
    public int DefaultZoomPercent { get; set; } = 100;
    public bool ShowGrid { get; set; } = true;
    public int AgentTcpPort { get; set; } = 47821;
    public string AssetImportMode { get; set; } = "copy";

    /// <summary>素材库路径来源模式，默认使用全局配置。</summary>
    public AssetPathMode MapModuleAssetPathMode { get; set; } = AssetPathMode.Global;

    /// <summary>P3: 地图模块自定义素材库路径，仅在 <see cref="MapModuleAssetPathMode"/> 为 Custom 时生效。</summary>
    public string? MapModuleCustomAssetPath { get; set; }
}

public static class GlobalSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MapEditor", "settings.json");
    }

    public static GlobalSettings Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
            return new GlobalSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GlobalSettings>(json, Options) ?? new GlobalSettings();
        }
        catch
        {
            return new GlobalSettings();
        }
    }

    public static void Save(GlobalSettings settings)
    {
        var path = GetSettingsPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(path, json);
    }
}
