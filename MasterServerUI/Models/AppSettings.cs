using System;
using System.IO;
using System.Text.Json;

namespace MasterServerUI.Models;

/// <summary>
/// 本地服务器管理台配置。存储到 %APPDATA%/TRPGMaster/serverui-settings.json。
/// 替代旧项目的 TRPGMaster.Core.Configuration.AppSettings。
/// </summary>
public class AppSettings
{
    public NetworkSettings Network { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public UISettings UI { get; set; } = new();

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TRPGMaster");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "serverui-settings.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // 读取失败时回退到默认配置
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 忽略保存失败
        }
    }
}

public class NetworkSettings
{
    public int Port { get; set; } = 7890;
    public string BindAddress { get; set; } = "localhost";
    public int MaxConnections { get; set; } = 1000;
    public int KeepAliveInterval { get; set; } = 30;
    public int ConnectionTimeout { get; set; } = 10;
}

public class StorageSettings
{
    public string DataPath { get; set; } = "./data";
    public int MaxCacheSize { get; set; } = 1024;
    public int MessageRetentionDays { get; set; } = 90;
}

public class SecuritySettings
{
    public bool RequirePassword { get; set; } = false;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public class UISettings
{
    public int WindowWidth { get; set; } = 1400;
    public int WindowHeight { get; set; } = 800;
    public bool AutoRefresh { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 2;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool AutoStartServer { get; set; } = false;
}
