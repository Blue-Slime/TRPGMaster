namespace MapEngine.Core.Commands;

public interface ICommand
{
    string Description { get; }
    void Execute(ISceneState state);
    void Undo(ISceneState state);
}
