using System.Text.Json;
using MapEngine.Core.Commands;

namespace MapEngine.Core.Networking;

/// <summary>
/// 客户端地图同步器：发送命令、接收并应用 Delta
/// </summary>
public sealed class MapSyncClient
{
    private readonly World _world;
    private readonly CommandBus _commandBus;
    private readonly string _myUserId;
    private int _localVersion;
    private readonly object _lock = new();

    public event EventHandler<string>? ConnectionError;

    public MapSyncClient(World world, CommandBus commandBus, string myUserId)
    {
        _world = world;
        _commandBus = commandBus;
        _myUserId = myUserId;
        _localVersion = 0;
    }

    /// <summary>
    /// 应用服务端广播的 Delta
    /// </summary>
    public void ApplyDelta(MapStateDelta delta)
    {
        lock (_lock)
        {
            // 1. 检测版本跳号（需要全量同步）
            if (delta.Version != _localVersion + 1)
            {
                ConnectionError?.Invoke(this, $"Version mismatch: expected {_localVersion + 1}, got {delta.Version}");
                return;
            }

            // 2. 回声过滤：自己的命令已在 CommandBus.Execute() 执行过了
            if (delta.UserId == _myUserId)
            {
                _localVersion = delta.Version;  // 仅更新版本号
                return;
            }

            try
            {
                // 3. 反序列化命令
                var command = CommandSerializer.Deserialize(
                    delta.CommandType,
                    delta.Params,
                    _world
                );

                // 4. 应用远程命令（不加入 Undo 栈）
                _commandBus.ApplyRemote(command);

                // 5. 更新本地版本号
                _localVersion = delta.Version;
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke(this, $"Failed to apply delta: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 应用全量同步数据
    /// </summary>
    public void ApplyFullSync(MapFullSync fullSync)
    {
        lock (_lock)
        {
            try
            {
                // 1. 清空本地 World
                foreach (var obj in _world.AllObjects().ToList())
                {
                    _world.RemoveObject(obj);
                }

                // 2. 从文档重建场景
                var objects = MapEngine.Core.Components.SceneSerializer.FromDocument(fullSync.Document);
                foreach (var obj in objects)
                {
                    _world.AddObject(obj, null);
                }

                // 3. 更新版本号
                _localVersion = fullSync.Version;

                // 4. 清空 Undo/Redo 栈
                _commandBus.Clear();
            }
            catch (Exception ex)
            {
                ConnectionError?.Invoke(this, $"Failed to apply full sync: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取当前本地版本号
    /// </summary>
    public int GetLocalVersion()
    {
        lock (_lock)
        {
            return _localVersion;
        }
    }

    /// <summary>
    /// 设置初始版本号（连接时从服务端获取）
    /// </summary>
    public void SetInitialVersion(int version)
    {
        lock (_lock)
        {
            _localVersion = version;
        }
    }
}
