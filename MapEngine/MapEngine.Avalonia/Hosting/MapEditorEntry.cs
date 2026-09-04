using Avalonia.Controls;
using MapEngine.Core.Hosting;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Avalonia.Services;

namespace MapEngine.Avalonia.Hosting;

/// <summary>
/// MapEditor 模块的统一入口。
/// TRPGMaster 主程序或独立 Shell 通过此类获取 IMapEditorInstance。
/// </summary>
public static class MapEditorEntry
{
    /// <summary>
    /// 将 MapEditor UI 挂载到宿主提供的 ContentControl 容器中。
    /// 适用于 TRPGMaster 主程序以 in-proc 方式嵌入 MapEditor 面板。
    /// </summary>
    /// <param name="container">宿主提供的容器控件，MapEditor 主窗口内容将被赋给它的 Content 属性</param>
    /// <param name="host">宿主上下文（用户身份、项目路径、日志）</param>
    /// <param name="options">启动选项（可选）</param>
    /// <returns>与该实例通信的 IMapEditorInstance 句柄</returns>
    public static IMapEditorInstance Mount(
        ContentControl container,
        IMapEditorHost host,
        MapEditorStartupOptions? options = null)
    {
        options ??= new MapEditorStartupOptions();

        MapSpriteAssetResolver.RoomAssetRoot = host.RoomAssetLibraryPath;

        var vm = new MainWindowViewModel();
        var view = new MapEngine.Avalonia.Views.MapEditorView { DataContext = vm };

        container.Content = view;

        var instance = new MapEditorInstance(vm, host);
        host.Logger.Info($"MapEditor mounted (projectRoot={host.ProjectRootPath})");
        return instance;
    }

    /// <summary>
    /// 以独立窗口模式启动 MapEditor（供 MapEditor.Shell 调用）。
    /// 返回的 IMapEditorInstance 可供外部脚本/AI 调用，但窗口生命周期由 Avalonia Application 管理。
    /// </summary>
    /// <param name="host">宿主上下文</param>
    /// <param name="options">启动选项（可选）</param>
    /// <returns>与该实例通信的 IMapEditorInstance 句柄</returns>
    public static IMapEditorInstance CreateStandaloneInstance(
        IMapEditorHost host,
        MapEditorStartupOptions? options = null)
    {
        options ??= new MapEditorStartupOptions();

        MapSpriteAssetResolver.RoomAssetRoot = host.RoomAssetLibraryPath;

        // 独立模式下 ViewModel 由 App.axaml.cs 构造并赋给 MainWindow.DataContext。
        // 这里创建一个临时 VM 仅供 AgentService 使用；Shell 负责把同一个 VM 传给窗口。
        // 实际接入时 Shell 应调用 CreateFromViewModel(vm, host)。
        var vm = new MainWindowViewModel();
        var instance = new MapEditorInstance(vm, host);
        host.Logger.Info($"MapEditor standalone instance created (projectRoot={host.ProjectRootPath})");
        return instance;
    }

    /// <summary>
    /// 从已有的 MainWindowViewModel 创建 IMapEditorInstance。
    /// 供 MapEditor.Shell 在 App.axaml.cs / MainWindow 已构造 VM 后调用，
    /// 避免重复构造 ViewModel。
    /// </summary>
    public static IMapEditorInstance CreateFromViewModel(
        MainWindowViewModel viewModel,
        IMapEditorHost host)
    {
        var instance = new MapEditorInstance(viewModel, host);
        host.Logger.Info("MapEditor instance created from existing ViewModel");
        return instance;
    }
}

/// <summary>
/// MapEditor 启动选项（目前为占位，后续扩展）。
/// </summary>
public sealed class MapEditorStartupOptions
{
    /// <summary>首选渲染后端（null = 使用默认 Wgl→AngleEgl→Software 链）</summary>
    public string? PreferredRenderBackend { get; init; }

    /// <summary>Agent TCP 监听端口（0 = 不启动 TCP Agent；仅 stdio 模式）</summary>
    public int AgentTcpPort { get; init; } = 0;

    /// <summary>是否在状态栏显示指令日志</summary>
    public bool ShowCommandLog { get; init; } = true;
}
