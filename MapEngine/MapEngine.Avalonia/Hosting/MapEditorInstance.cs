using System;
using System.Threading;
using System.Threading.Tasks;
using MapEngine.Agent;
using MapEngine.Core.Commands;
using MapEngine.Core.Hosting;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Hosting;

/// <summary>
/// IMapEditorInstance 的具体实现。包装 MainWindowViewModel + AgentService，
/// 把 host 关注的能力（命令处理、快照导出、事件冒泡）整合到一个对象。
/// </summary>
internal sealed class MapEditorInstance : IMapEditorInstance
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AgentService _agentService;
    private readonly IMapEditorHost _host;
    private bool _disposed;

    public MapEditorInstance(MainWindowViewModel viewModel, IMapEditorHost host)
    {
        _viewModel = viewModel;
        _host = host;

        // 注入 CommandBus 到 AgentService
        _agentService = new AgentService(viewModel.CommandBus);

        // 把 CommandBus 的内部事件转发为对外的 MapEditorCommandEvent
        viewModel.CommandBus.CommandExecuted += OnCommandBusExecuted;
    }

    public event EventHandler<MapEditorCommandEvent>? CommandExecuted;

    public Task<string> ProcessMessageAsync(string jsonRpc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // AgentService 是同步实现，包一层 Task 以适配异步接口
            var result = _agentService.ProcessMessage(jsonRpc);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _host.Logger.Error("ProcessMessageAsync failed", ex);
            throw;
        }
    }

    public Task<string> ExportSceneSnapshotAsync(SnapshotScope scope = SnapshotScope.Full, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = SceneSnapshotBuilder.Build(_viewModel, scope);
        return Task.FromResult(snapshot);
    }

    public Task LoadSceneAsync(string scenePath, CancellationToken ct = default)
    {
        // TODO: 接入 SceneFileLoader / FakeProjectDataLoader 的实际加载流程
        // 目前的 ViewModel 在构造时一次性加载，运行期切换场景需要更大改动
        _host.Logger.Warn($"LoadSceneAsync not yet implemented (path={scenePath})");
        return Task.CompletedTask;
    }

    public Task SaveSceneAsync(CancellationToken ct = default)
    {
        // TODO: 序列化当前 HierarchyRoots 到 .scene 文件
        _host.Logger.Warn("SaveSceneAsync not yet implemented");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.CommandBus.CommandExecuted -= OnCommandBusExecuted;
    }

    private void OnCommandBusExecuted(object? sender, CommandExecutedEventArgs e)
    {
        var evt = new MapEditorCommandEvent
        {
            Description = e.Command.Description,
            Action = e.Action.ToString(),
            UserId = _host.CurrentUserId,
            Timestamp = DateTime.UtcNow,
        };
        try
        {
            CommandExecuted?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _host.Logger.Error("CommandExecuted handler threw", ex);
        }
    }
}
