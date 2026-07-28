using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterIM.SDK;
using MasterIM.Server;

namespace MasterIM.IntegrationTest;

/// <summary>
/// 地图引擎命令同步集成测试
/// 验证：多客户端通过 WebSocket 同步地图命令的完整流程
/// </summary>
public class MapSyncIntegrationTest
{
    public static async Task Main()
    {
        Console.WriteLine("=== 地图同步集成测试 ===\n");

        // Step 1: 启动服务端
        var server = new MasterServerInstance();
        await server.StartAsync(5555);
        Console.WriteLine("✓ 服务端已启动 (端口 5555)\n");

        // Step 2: 创建测试房间
        var room = await server.CreateRoomAsync("测试房间", "admin", 10);
        Console.WriteLine($"✓ 测试房间已创建: {room.RoomId}\n");

        // Step 3: 创建两个客户端连接
        var clientA = new IMClient();
        var clientB = new IMClient();

        var deltasA = new List<string>();
        var deltasB = new List<string>();
        var fullSyncA = new List<string>();

        clientA.OnMapDeltaJson += json => { deltasA.Add(json); Console.WriteLine($"[Client A] 收到 Delta: {json.Substring(0, Math.Min(80, json.Length))}..."); };
        clientB.OnMapDeltaJson += json => { deltasB.Add(json); Console.WriteLine($"[Client B] 收到 Delta: {json.Substring(0, Math.Min(80, json.Length))}..."); };
        clientA.OnMapFullSyncJson += json => { fullSyncA.Add(json); Console.WriteLine($"[Client A] 收到 FullSync: {json.Substring(0, Math.Min(80, json.Length))}..."); };

        var resultA = await clientA.ConnectAsync($"ws://localhost:5555/im", "alice", room.RoomId, "channel_lobby");
        var resultB = await clientB.ConnectAsync($"ws://localhost:5555/im", "bob", room.RoomId, "channel_lobby");

        if (resultA.Result != MasterIM.SDK.JoinResult.Success || resultB.Result != MasterIM.SDK.JoinResult.Success)
        {
            Console.WriteLine($"✗ 客户端连接失败: A={resultA.Result}, B={resultB.Result}");
            return;
        }

        Console.WriteLine("✓ 两个客户端已连接\n");
        await Task.Delay(500);  // 等待连接稳定

        // Step 4: 客户端 A 发送地图命令（AddObject）
        Console.WriteLine("--- Test 1: 客户端 A 发送 AddObject 命令 ---");
        var addObjectCommand = new
        {
            command_type = "AddObject",
            @params = new
            {
                object_id = Guid.NewGuid().ToString(),
                name = "测试对象",
                x = 100.0,
                y = 200.0
            }
        };

        var commandJson = JsonSerializer.Serialize(addObjectCommand);
        var requestJson = JsonSerializer.Serialize(new
        {
            CommandType = addObjectCommand.command_type,
            Params = JsonDocument.Parse(JsonSerializer.Serialize(addObjectCommand.@params)).RootElement,
            UserId = "alice",
            ExpectedVersion = 0
        });

        await clientA.SendMapCommandAsync(requestJson);
        await Task.Delay(1000);  // 等待广播

        // 验证：客户端 A 和 B 都应该收到 Delta
        Console.WriteLine($"\n[验证] Client A 收到 {deltasA.Count} 个 Delta");
        Console.WriteLine($"[验证] Client B 收到 {deltasB.Count} 个 Delta");

        if (deltasA.Count > 0 && deltasB.Count > 0)
        {
            Console.WriteLine("✓ Delta 广播成功\n");
        }
        else
        {
            Console.WriteLine("✗ Delta 广播失败\n");
        }

        // Step 5: 客户端 B 发送 MoveObject 命令
        Console.WriteLine("--- Test 2: 客户端 B 发送 MoveObject 命令 ---");
        deltasA.Clear();
        deltasB.Clear();

        var moveCommand = new
        {
            command_type = "MoveObject",
            @params = new
            {
                object_id = addObjectCommand.@params.object_id,
                x = 300.0,
                y = 400.0
            }
        };

        var moveRequestJson = JsonSerializer.Serialize(new
        {
            CommandType = moveCommand.command_type,
            Params = JsonDocument.Parse(JsonSerializer.Serialize(moveCommand.@params)).RootElement,
            UserId = "bob",
            ExpectedVersion = 0
        });

        await clientB.SendMapCommandAsync(moveRequestJson);
        await Task.Delay(1000);

        Console.WriteLine($"\n[验证] Client A 收到 {deltasA.Count} 个 Delta");
        Console.WriteLine($"[验证] Client B 收到 {deltasB.Count} 个 Delta");

        if (deltasA.Count > 0 && deltasB.Count > 0)
        {
            Console.WriteLine("✓ 双向同步成功\n");
        }
        else
        {
            Console.WriteLine("✗ 双向同步失败\n");
        }

        // Step 6: 客户端 A 请求全量同步
        Console.WriteLine("--- Test 3: 客户端 A 请求全量同步 ---");
        await clientA.RequestMapFullSyncAsync();
        await Task.Delay(1000);

        Console.WriteLine($"\n[验证] Client A 收到 {fullSyncA.Count} 个 FullSync");

        if (fullSyncA.Count > 0)
        {
            Console.WriteLine("✓ 全量同步成功\n");
            var syncDoc = JsonDocument.Parse(fullSyncA[0]);
            if (syncDoc.RootElement.TryGetProperty("Version", out var ver))
            {
                Console.WriteLine($"  服务端版本号: {ver.GetInt32()}");
            }
        }
        else
        {
            Console.WriteLine("✗ 全量同步失败\n");
        }

        // Step 7: 清理
        Console.WriteLine("\n=== 测试完成，清理资源 ===");
        clientA.Dispose();
        clientB.Dispose();
        await server.StopAsync();
        Console.WriteLine("✓ 服务端已停止");
    }
}
