using System;
using System.IO;
using System.Text.Json;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// 客户端全局配置文件的最小投影（只读取需要的字段）。
/// 完整模型定义在 MasterClient.Models.AppSettings。
/// </summary>
public sealed class ClientGlobalConfig
{
    /// <summary>全局素材库路径（所有模块共享）。</summary>
    public string? AssetLibraryPath { get; set; }
}

/// <summary>
/// 读取客户端全局配置文件（%APPDATA%/TRPGMaster/settings.json）中的素材库路径。
/// 该文件由 MasterClient 管理，地图编辑器只读不写。
/// </summary>
public static class GlobalConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>客户端全局配置文件路径。</summary>
    public static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TRPGMaster", "settings.json");
    }

    /// <summary>客户端全局配置文件是否存在。</summary>
    public static bool ConfigExists() => File.Exists(GetConfigPath());

    /// <summary>读取客户端全局配置。文件不存在或解析失败时返回 null，调用方需降级。</summary>
    public static ClientGlobalConfig? TryLoad()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClientGlobalConfig>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取全局素材库路径。文件缺失或字段为空时返回 null。</summary>
    public static string? TryGetAssetLibraryPath()
    {
        var path = TryLoad()?.AssetLibraryPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
