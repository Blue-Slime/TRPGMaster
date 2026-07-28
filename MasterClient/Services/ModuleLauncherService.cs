using System.Diagnostics;

namespace MasterClient.Services;

/// <summary>
/// 模块（编辑器）启动服务
/// </summary>
public class ModuleLauncherService
{
    private readonly Dictionary<string, Process?> _runningModules = new();

    public event Action<string>? ModuleStarted;
    public event Action<string>? ModuleExited;
    public event Action<string, string>? ModuleLaunchFailed;

    /// <summary>
    /// 启动指定模块
    /// </summary>
    public bool LaunchModule(string moduleName, string? arguments = null)
    {
        try
        {
            // 如果模块已在运行，激活它
            if (_runningModules.TryGetValue(moduleName, out var existingProcess) &&
                existingProcess != null && !existingProcess.HasExited)
            {
                ActivateModule(moduleName);
                return true;
            }

            var exePath = FindModulePath(moduleName);
            if (string.IsNullOrEmpty(exePath))
            {
                var error = $"未找到 {moduleName} 模块";
                ModuleLaunchFailed?.Invoke(moduleName, error);
                Debug.WriteLine($"[ModuleLauncher] {error}");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                ModuleLaunchFailed?.Invoke(moduleName, "进程启动失败");
                return false;
            }

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => OnModuleExited(moduleName, process);

            _runningModules[moduleName] = process;
            ModuleStarted?.Invoke(moduleName);
            Debug.WriteLine($"[ModuleLauncher] 已启动: {moduleName}");

            return true;
        }
        catch (Exception ex)
        {
            ModuleLaunchFailed?.Invoke(moduleName, ex.Message);
            Debug.WriteLine($"[ModuleLauncher] 启动失败 {moduleName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 查找模块可执行文件路径
    /// </summary>
    private string? FindModulePath(string moduleName)
    {
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var moduleDirName = $"TRPGMaster.{moduleName}";
        var exeName = $"TRPGMaster.{moduleName}.exe";

        var possiblePaths = new[]
        {
            // 同一目录
            Path.Combine(currentDir, exeName),
            // 平级目录
            Path.Combine(currentDir, "..", moduleDirName, exeName),
            // 开发环境 Debug
            Path.Combine(currentDir, "..", "..", "..", moduleDirName, "bin", "Debug", "net8.0", exeName),
            // 开发环境 Release
            Path.Combine(currentDir, "..", "..", "..", moduleDirName, "bin", "Release", "net8.0", exeName)
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                Debug.WriteLine($"[ModuleLauncher] 找到模块: {fullPath}");
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 激活已运行的模块窗口
    /// </summary>
    private void ActivateModule(string moduleName)
    {
        // TODO: 通过IPC或Windows API激活窗口
        Debug.WriteLine($"[ModuleLauncher] 模块已在运行: {moduleName}");
    }

    private void OnModuleExited(string moduleName, Process process)
    {
        process.Dispose();
        _runningModules.Remove(moduleName);
        ModuleExited?.Invoke(moduleName);
        Debug.WriteLine($"[ModuleLauncher] 已退出: {moduleName}");
    }

    /// <summary>
    /// 检查模块是否正在运行
    /// </summary>
    public bool IsModuleRunning(string moduleName)
    {
        return _runningModules.TryGetValue(moduleName, out var process) &&
               process != null && !process.HasExited;
    }

    /// <summary>
    /// 关闭指定模块
    /// </summary>
    public void CloseModule(string moduleName)
    {
        if (_runningModules.TryGetValue(moduleName, out var process) &&
            process != null && !process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModuleLauncher] 关闭失败 {moduleName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 关闭所有模块
    /// </summary>
    public void CloseAllModules()
    {
        foreach (var moduleName in _runningModules.Keys.ToList())
        {
            CloseModule(moduleName);
        }
    }
}
