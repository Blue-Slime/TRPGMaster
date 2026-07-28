using System.Text.Json;
using System.Threading.Tasks;
using MapEngine.Core.Networking;
using MasterIM.SDK;

namespace MasterClient.Services;

/// <summary>
/// 通过 WebSocket (IMClient) 向服务端发送地图命令。
/// 实现 INetworkSender，供 CommandBus 在 Undo/Redo/Execute 时调用。
///
/// 使用方式：
///   var sender = new WebSocketCommandSender(imClient, myUserId);
///   commandBus.SetNetworkSender(sender);
///   // 之后 Execute/Undo/Redo 会自动通过 WebSocket 同步到服务端
/// </summary>
public sealed class WebSocketCommandSender : INetworkSender
{
    private readonly IMClient _client;
    private readonly string _userId;

    public WebSocketCommandSender(IMClient client, string userId)
    {
        _client = client;
        _userId = userId;
    }

    /// <summary>
    /// 将命令 JSON 发送给服务端 (map:command)。
    /// commandJson 由 CommandSerializer.Serialize() / SerializeInverse() 产生，
    /// 格式为 {"command_type":"...","params":{...}}。
    /// 这里注入 UserId 字段后再发送。
    /// </summary>
    public async Task SendAsync(string commandJson)
    {
        // 注入 user_id 和 expected_version，生成完整 MapCommandRequest JSON
        using var doc = JsonDocument.Parse(commandJson);
        var root = doc.RootElement;

        var enriched = new
        {
            CommandType = root.GetProperty("command_type").GetString() ?? "",
            Params       = root.GetProperty("params"),
            UserId       = _userId,
            ExpectedVersion = 0   // 乐观更新，当前不做版本冲突检测
        };

        var fullJson = JsonSerializer.Serialize(enriched);
        await _client.SendMapCommandAsync(fullJson);
    }
}
