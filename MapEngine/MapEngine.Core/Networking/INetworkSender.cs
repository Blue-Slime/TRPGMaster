namespace MapEngine.Core.Networking;

/// <summary>
/// 网络发送器接口（CommandBus 使用，屏蔽底层传输细节）
/// </summary>
public interface INetworkSender
{
    /// <summary>
    /// 发送序列化后的命令 JSON 到服务端
    /// </summary>
    Task SendAsync(string commandJson);
}
