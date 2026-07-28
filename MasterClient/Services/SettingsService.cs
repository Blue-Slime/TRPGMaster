using System.Text.Json;
using MasterClient.Models;

namespace MasterClient.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings? _settings;

    public SettingsService()
    {
        // 路径: %APPDATA%/TRPGMaster/settings.json
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "TRPGMaster");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");

        // 创建 rooms 索引目录
        var roomsIndexFolder = Path.Combine(appFolder, "rooms");
        Directory.CreateDirectory(roomsIndexFolder);
    }

    public AppSettings Settings => _settings ??= Load();

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // 确保嵌套对象不为 null
                settings.Display ??= new DisplayPreferences();
                settings.TypingIndicator ??= new TypingIndicatorPreferences();
                settings.FileTransfer ??= new FileTransferPreferences();
                settings.Notification ??= new NotificationPreferences();

                return settings;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] 加载设置失败: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] 保存设置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录一次进房（去重键：服务器地址 + 房间ID + 用户ID），置顶、限 20 条。
    /// </summary>
    public void AddRecentRoom(RecentRoom recent)
    {
        recent.LastJoinedAt = DateTime.Now;

        // 移除已存在的相同房间（同服务器+房间+用户）
        Settings.RecentRooms.RemoveAll(r =>
            r.ServerAddress == recent.ServerAddress &&
            r.RoomId == recent.RoomId &&
            r.UserId == recent.UserId);

        // 添加到列表开头
        Settings.RecentRooms.Insert(0, recent);

        // 限制最多 20 个
        if (Settings.RecentRooms.Count > 20)
        {
            Settings.RecentRooms = Settings.RecentRooms.Take(20).ToList();
        }

        Save();
    }

    public void RemoveRecentRoom(RecentRoom recent)
    {
        Settings.RecentRooms.RemoveAll(r =>
            r.ServerAddress == recent.ServerAddress &&
            r.RoomId == recent.RoomId &&
            r.UserId == recent.UserId);
        Save();
    }

    public void ClearRecentRooms()
    {
        Settings.RecentRooms.Clear();
        Save();
    }
}
