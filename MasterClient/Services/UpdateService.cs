using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterClient.Services;

/// <summary>
/// 版本更新检查服务
/// </summary>
public partial class UpdateService : ObservableObject
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // 当前版本
    public static readonly Version CurrentVersion = new Version(1, 0, 0);

    // 版本信息URL（可配置）
    private const string DefaultUpdateUrl = "https://api.trpgmaster.com/version.json";

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private string _updateNotes = string.Empty;

    [ObservableProperty]
    private string _downloadUrl = string.Empty;

    [ObservableProperty]
    private string _checkError = string.Empty;

    [ObservableProperty]
    private DateTime _lastCheckTime;

    public event Action<UpdateInfo>? UpdateAvailable;
    public event Action<string>? CheckFailed;

    /// <summary>
    /// 检查更新
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string? updateUrl = null)
    {
        if (IsChecking) return null;

        IsChecking = true;
        CheckError = string.Empty;

        try
        {
            var url = updateUrl ?? DefaultUpdateUrl;
            System.Diagnostics.Debug.WriteLine($"[UpdateService] 检查更新: {url}");

            var response = await _httpClient.GetStringAsync(url);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateInfo == null)
            {
                CheckError = "无法解析更新信息";
                CheckFailed?.Invoke(CheckError);
                return null;
            }

            LastCheckTime = DateTime.Now;
            LatestVersion = updateInfo.Version;
            UpdateNotes = updateInfo.ReleaseNotes ?? string.Empty;
            DownloadUrl = updateInfo.DownloadUrl ?? string.Empty;

            // 比较版本
            if (Version.TryParse(updateInfo.Version, out var latestVer))
            {
                HasUpdate = latestVer > CurrentVersion;

                if (HasUpdate)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] 发现新版本: {updateInfo.Version}");
                    UpdateAvailable?.Invoke(updateInfo);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateService] 已是最新版本: {CurrentVersion}");
                }
            }

            return updateInfo;
        }
        catch (HttpRequestException ex)
        {
            CheckError = "网络连接失败，请检查网络设置";
            CheckFailed?.Invoke(CheckError);
            System.Diagnostics.Debug.WriteLine($"[UpdateService] 网络错误: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            CheckError = "检查超时，请稍后重试";
            CheckFailed?.Invoke(CheckError);
            System.Diagnostics.Debug.WriteLine("[UpdateService] 检查超时");
            return null;
        }
        catch (JsonException ex)
        {
            CheckError = "更新信息格式错误";
            CheckFailed?.Invoke(CheckError);
            System.Diagnostics.Debug.WriteLine($"[UpdateService] JSON解析错误: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            CheckError = $"检查失败: {ex.Message}";
            CheckFailed?.Invoke(CheckError);
            System.Diagnostics.Debug.WriteLine($"[UpdateService] 检查更新错误: {ex.Message}");
            return null;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// 模拟检查更新（用于开发测试）
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateMockAsync(bool simulateHasUpdate = false)
    {
        if (IsChecking) return null;

        IsChecking = true;
        CheckError = string.Empty;

        try
        {
            // 模拟网络延迟
            await Task.Delay(1000);

            LastCheckTime = DateTime.Now;

            if (simulateHasUpdate)
            {
                var updateInfo = new UpdateInfo
                {
                    Version = "1.1.0",
                    ReleaseNotes = @"版本 1.1.0 更新内容：

• 新增: 云世界功能（VIP专享）
• 新增: 好友系统
• 优化: 启动速度提升 30%
• 修复: 若干已知问题",
                    DownloadUrl = "https://trpgmaster.com/download",
                    ReleaseDate = DateTime.Now,
                    MinimumVersion = "1.0.0",
                    IsRequired = false
                };

                LatestVersion = updateInfo.Version;
                UpdateNotes = updateInfo.ReleaseNotes;
                DownloadUrl = updateInfo.DownloadUrl;
                HasUpdate = true;

                UpdateAvailable?.Invoke(updateInfo);
                return updateInfo;
            }
            else
            {
                LatestVersion = CurrentVersion.ToString();
                HasUpdate = false;

                return new UpdateInfo
                {
                    Version = CurrentVersion.ToString(),
                    ReleaseNotes = "当前已是最新版本"
                };
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// 打开下载页面
    /// </summary>
    public void OpenDownloadPage()
    {
        if (string.IsNullOrEmpty(DownloadUrl))
        {
            DownloadUrl = "https://trpgmaster.com/download";
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DownloadUrl,
                UseShellExecute = true
            });
            System.Diagnostics.Debug.WriteLine($"[UpdateService] 打开下载页面: {DownloadUrl}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateService] 打开下载页面失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取版本显示字符串
    /// </summary>
    public string GetVersionDisplayString()
    {
        return $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build} Beta";
    }
}

/// <summary>
/// 更新信息
/// </summary>
public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string? ReleaseNotes { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? MinimumVersion { get; set; }
    public bool IsRequired { get; set; }
}
