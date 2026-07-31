using MapEngine.Core.Networking;

namespace MapEngine.Core.Commands;

public sealed class CommandBus
{
    private readonly Stack<IWorldCommand> _undoStack = new();
    private readonly Stack<IWorldCommand> _redoStack = new();
    private readonly World _world;
    private INetworkSender? _networkSender;

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public CommandBus(World world)
    {
        _world = world;
    }

    /// <summary>联机时注入 NetworkSender，单机时保持 null</summary>
    public void SetNetworkSender(INetworkSender? sender)
    {
        _networkSender = sender;
    }

    /// <summary>本地命令入口（手动编辑/AI）</summary>
    public void Execute(IWorldCommand command)
    {
        command.Execute(_world);
        _undoStack.Push(command);
        _redoStack.Clear();

        // ILocalOnlyCommand 是纯 ViewModel 层操作，不走网络
        if (_networkSender != null && command is not ILocalOnlyCommand)
        {
            var json = CommandSerializer.Serialize(command);
            _ = _networkSender.SendAsync(json);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Execute));
    }

    /// <summary>远程命令入口（收到服务端广播时调用）</summary>
    public void ApplyRemote(IWorldCommand command)
    {
        command.Execute(_world);
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.RemoteApply));
    }

    public void Undo()
    {
        if (!CanUndo) return;

        var command = _undoStack.Pop();
        command.Undo(_world);
        _redoStack.Push(command);

        if (_networkSender != null && command is not ILocalOnlyCommand)
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

        if (_networkSender != null && command is not ILocalOnlyCommand)
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
