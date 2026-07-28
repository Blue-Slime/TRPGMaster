using System;
using System.IO;

namespace MasterIM.Server;

/// <summary>
/// 共享路径解析（本地方案）。
/// 服务端实例与服务器管理界面必须使用同一个 data 目录，
/// 管理界面创建的房间才能被服务端实例读到。
/// </summary>
public static class SharedPaths
{
    /// <summary>环境变量名：显式指定共享数据目录</summary>
    public const string DataPathEnv = "TRPG_DATA_PATH";

    /// <summary>
    /// 解析共享数据目录。
    /// 优先级：环境变量 TRPG_DATA_PATH > 向上查找到含 TRPGMaster.sln 的目录下的 shared-data。
    /// </summary>
    public static string ResolveDataPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DataPathEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var root = FindSolutionRoot() ?? AppContext.BaseDirectory;
        return Path.Combine(root, "shared-data");
    }

    /// <summary>
    /// 从当前程序目录向上查找包含 TRPGMaster.sln 的解决方案根目录。
    /// </summary>
    private static string? FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TRPGMaster.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
