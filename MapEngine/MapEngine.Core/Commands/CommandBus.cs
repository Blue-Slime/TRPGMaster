using MapEngine.Core.Networking;

namespace MapEngine.Core.Commands;

public sealed class CommandBus
{
    private readonly Stack<IWorldCommand> _undoStack = new();
    private readonly Stack<IWorldCommand> _redoStack = new();
    private readonly World _world;
    private INetworkSender? _networkSender;
    private ISceneState? _legacyState;

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public CommandBus(World world)
    {
        _world = world;
    }

    /// <summary>临时方法：用于支持旧的 VmXxxCommand，待迁移完成后删除</summary>
    public void SetLegacyState(ISceneState state)
    {
        _legacyState = state;
    }

    /// <summary>联机时调用此方法注入 NetworkSender，单机时保持 null</summary>
    public void SetNetworkSender(INetworkSender? sender)
    {
        _networkSender = sender;
    }

    /// <summary>本地命令入口（手动编辑/AI）</summary>
    public void Execute(IWorldCommand command)
    {
        // 1. 本地立即执行（乐观更新）
        command.Execute(_world);
        _undoStack.Push(command);
        _redoStack.Clear();

        // 2. 联机环境：序列化并发送（不阻塞）
        if (_networkSender != null)
        {
            var json = CommandSerializer.Serialize(command);
            _ = _networkSender.SendAsync(json);
        }

        // 3. 触发事件
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Execute));
    }

    /// <summary>临时重载：支持旧的 ICommand (VmXxxCommand)，待迁移完成后删除</summary>
    public void Execute(ICommand legacyCommand)
    {
        if (_legacyState is null)
            throw new InvalidOperationException("Legacy command requires SetLegacyState() to be called first.");

        var adapter = new LegacyCommandAdapter(legacyCommand, _legacyState);
        Execute(adapter);
    }

    /// <summary>远程命令入口（收到服务端广播时调用）</summary>
    public void ApplyRemote(IWorldCommand command)
    {
        command.Execute(_world);
        // 不入栈 - 别人的操作不应出现在自己的 Undo 历史中
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.RemoteApply));
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var command = _undoStack.Pop();
        command.Undo(_world);
        _redoStack.Push(command);

        // 联机环境：序列化逆命令并发送
        if (_networkSender != null)
        {
            var json = CommandSerializer.SerializeInverse(command);
            _ = _networkSender.SendAsync(json);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Undo));
    }

    public void Redo()
    {
        if (!CanRedo) return;

        var command = _redoStack.Pop();
        command.Execute(_world);
        _undoStack.Push(command);

        // 联机环境：重新执行命令并发送
        if (_networkSender != null)
        {
            var json = CommandSerializer.Serialize(command);
            _ = _networkSender.SendAsync(json);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Redo));
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    public World GetWorld() => _world;
}

/// <summary>临时适配器：将旧的 ICommand 包装成 IWorldCommand</summary>
internal sealed class LegacyCommandAdapter : IWorldCommand
{
    private readonly ICommand _legacyCommand;
    private readonly ISceneState _state;

    public LegacyCommandAdapter(ICommand legacyCommand, ISceneState state)
    {
        _legacyCommand = legacyCommand;
        _state = state;
    }

    public string Description => _legacyCommand.Description;

    public void Execute(World world)
    {
        _legacyCommand.Execute(_state);
    }

    public void Undo(World world)
    {
        _legacyCommand.Undo(_state);
    }
}

public enum CommandAction
{
    Execute,
    Undo,
    Redo,
    RemoteApply
}

public sealed class CommandExecutedEventArgs : EventArgs
{
    public CommandExecutedEventArgs(IWorldCommand command, CommandAction action)
    {
        Command = command;
        Action = action;
    }

    public IWorldCommand Command { get; }
    public CommandAction Action { get; }
}
