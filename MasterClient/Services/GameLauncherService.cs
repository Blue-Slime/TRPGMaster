using System.Diagnostics;
using MasterClient.Models;

namespace MasterClient.Services;

/// <summary>
/// 拉起 MasterClient 进程并传入进房参数（多进程集成）。
///
/// 命令行契约（已在 MasterClient 端实现，务必完全匹配）：
///   MasterClient.exe --server=&lt;ws地址&gt; --room-id=&lt;房间ID&gt; --channel-id=&lt;频道ID&gt; --user-id=&lt;用户ID&gt; --display-name=&lt;显示名&gt;
/// </summary>
public class GameLauncherService
{
    private readonly AuthService _authService;
    private Process? _gameProcess;

    public event Action? GameStarted;
    public event Action? GameExited;

    public bool IsGameRunning => _gameProcess != null && !_gameProcess.HasExited;

    public GameLauncherService(AuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// 启动客户端并进入指定房间（供 MainWindowViewModel 使用）。
    /// </summary>
    public Task<bool> LaunchGameAsync(RoomInfo room)
    {
        try
        {
            var exePath = FindClientExe();
            if (string.IsNullOrEmpty(exePath))
            {
                System.Diagnostics.Debug.WriteLine("[GameLauncher] 未找到 MasterClient 可执行文件，请先构建 MasterClient 项目。");
                return Task.FromResult(false);
            }

            // RoomInfo → 参数映射
            var serverUrl = NormalizeServerUrl(room.ServerAddress);
            var roomId = room.Id.ToString();
            const string channelId = "channel_lobby";
            var userId = _authService.CurrentUser?.Username ?? "player";
            var displayName = !string.IsNullOrWhiteSpace(_authService.CurrentUser?.Username)
                ? _authService.CurrentUser!.Username
                : (!string.IsNullOrWhiteSpace(room.Name) ? room.Name : "player");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add($"--server={serverUrl}");
            startInfo.ArgumentList.Add($"--room-id={roomId}");
            startInfo.ArgumentList.Add($"--channel-id={channelId}");
            startInfo.ArgumentList.Add($"--user-id={userId}");
            startInfo.ArgumentList.Add($"--display-name={displayName}");

            _gameProcess = Process.Start(startInfo);
            if (_gameProcess == null)
            {
                return Task.FromResult(false);
            }

            _gameProcess.EnableRaisingEvents = true;
            _gameProcess.Exited += OnGameProcessExited;

            GameStarted?.Invoke();

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameLauncher] 启动客户端失败: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 用显式参数直接拉起客户端进房（服务器地址 + 字符串房间ID + 频道 + 用户 + 显示名）。
    /// 用于"直连世界"这类全量输入进房，房间ID 保持字符串（如 room_001），不经过 Guid。
    /// </summary>
    public bool LaunchClientDirect(
        string serverAddress,
        string roomId,
        string channelId,
        string userId,
        string displayName,
        out string error)
    {
        error = string.Empty;
        try
        {
            var exePath = FindClientExe();
            if (string.IsNullOrEmpty(exePath))
            {
                error = "未找到 MasterClient 可执行文件，请先构建 MasterClient 项目。";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add($"--server={NormalizeServerUrl(serverAddress)}");
            startInfo.ArgumentList.Add($"--room-id={roomId}");
            startInfo.ArgumentList.Add($"--channel-id={(string.IsNullOrWhiteSpace(channelId) ? "channel_lobby" : channelId)}");
            startInfo.ArgumentList.Add($"--user-id={userId}");
            startInfo.ArgumentList.Add($"--display-name={(string.IsNullOrWhiteSpace(displayName) ? userId : displayName)}");

            _gameProcess = Process.Start(startInfo);
            if (_gameProcess == null)
            {
                error = "进程启动失败。";
                return false;
            }

            _gameProcess.EnableRaisingEvents = true;
            _gameProcess.Exited += OnGameProcessExited;
            GameStarted?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            error = $"启动客户端失败: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 将 RoomInfo.ServerAddress 规整为 ws://host:port/im 形式。
    /// 若已是 ws:// 或 wss:// 开头则原样返回。
    /// </summary>
    private static string NormalizeServerUrl(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return "ws://localhost:7890/im";

        if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return address;
        }

        return $"ws://{address}/im";
    }

    /// <summary>
    /// 查找 MasterClient.exe。优先同目录，其次从解决方案结构推断。
    /// </summary>
    private static string? FindClientExe()
    {
        var baseDir = AppContext.BaseDirectory;
        const string exeName = "MasterClient.exe";

        var candidates = new List<string>
        {
            Path.Combine(baseDir, exeName),
        };

        // 从含 TRPGMaster.sln 的解决方案根推断 MasterClient 输出目录
        var root = FindSolutionRoot(baseDir);
        if (root != null)
        {
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                candidates.Add(Path.Combine(root, "MasterClient", "bin", cfg, "net9.0", exeName));
            }
        }

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TRPGMaster.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        _gameProcess?.Dispose();
        _gameProcess = null;
        GameExited?.Invoke();
    }

    public void CloseGame()
    {
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            try
            {
                _gameProcess.Kill();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameLauncher] 关闭客户端失败: {ex.Message}");
            }
        }
    }
}
