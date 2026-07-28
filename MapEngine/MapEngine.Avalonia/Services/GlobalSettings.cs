using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapEngine.Avalonia.Services;

public sealed class GlobalSettings
{
    public string PackageName { get; set; } = "默认地图包";
    public int FeetPerCell { get; set; } = 5;
    public int DefaultZoomPercent { get; set; } = 100;
    public bool ShowGrid { get; set; } = true;
    public int AgentTcpPort { get; set; } = 47821;
    public string AssetImportMode { get; set; } = "copy";
}

public static class GlobalSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
