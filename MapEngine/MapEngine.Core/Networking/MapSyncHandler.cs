using System.Text.Json;
using MapEngine.Core.Commands;

namespace MapEngine.Core.Networking;

/// <summary>
/// 服务端地图同步处理器：接收客户端命令、执行、广播 Delta
/// </summary>
public sealed class MapSyncHandler
{
    private readonly World _world;
    private readonly CommandBus _commandBus;
    private int _version;
    private readonly object _lock = new();

    public event EventHandler<MapStateDelta>? DeltaBroadcast;

    public MapSyncHandler(World world, CommandBus commandBus)
    {
        _world = world;
        _commandBus = commandBus;
        _version = 0;
    }

    /// <summary>
    /// 处理客户端命令请求
    /// </summary>
    public MapCommandResult HandleCommand(MapCommandRequest request)
    {
        lock (_lock)
        {
            try
            {
                // 1. 反序列化命令（需要查询 World 获取旧值用于 Undo）
                var command = CommandSerializer.Deserialize(
                    request.CommandType,
                    request.Params,
                    _world
                );

                // 2. 执行命令
                _commandBus.Execute(command);

                // 3. 增加版本号
                _version++;

                // 4. 广播 Delta 给所有客户端（含发送者，由客户端自己过滤回声）
                var delta = new MapStateDelta
                {
                    CommandType = request.CommandType,
                    Params = request.Params,
                    Version = _version,
                    UserId = request.UserId  // 原样转发，客户端用于回声过滤
                };
                DeltaBroadcast?.Invoke(this, delta);

                // 5. 返回成功结果
                return new MapCommandResult
                {
                    Success = true,
                    NewVersion = _version,
                    CreatedObjectId = ExtractCreatedObjectId(command)
                };
            }
            catch (Exception ex)
            {
                return new MapCommandResult
                {
                    Success = false,
                    Error = ex.Message,
                    NewVersion = _version
                };
            }
        }
    }

    /// <summary>
    /// 处理客户端全量同步请求
    /// </summary>
    public MapFullSync HandleFullSyncRequest(MapFullSyncRequest request)
    {
        lock (_lock)
        {
            // 导出完整场景文档
            var document = MapEngine.Core.Components.SceneSerializer.ToDocument(_world.AllObjects());
            return new MapFullSync
            {
                Document = document,
                Version = _version
            };
        }
    }

    /// <summary>
    /// 获取当前版本号
    /// </summary>
    public int GetCurrentVersion()
    {
        lock (_lock)
        {
            return _version;
        }
    }

    /// <summary>
    /// 从命令中提取新创建的对象 ID（如果有）
    /// </summary>
    private static string? ExtractCreatedObjectId(IWorldCommand command)
    {
        return command switch
        {
            WorldAddObjectCommand cmd => cmd.ObjectId.ToString(),
            _ => null
        };
    }
}
