using MasterIM.Server;

// 无头服务端：单进程多房间。复用 MasterServerInstance（与带界面的 MasterServerUI 同一套逻辑）。
//   dotnet run -- --port=5000 [--autoOpen] [--open=room_001,room_002]
// 默认房间需手动开启（--open 指定），或加 --autoOpen 让有人连即自动开启。

static string? GetArg(string[] a, string key)
{
    var p = $"--{key}=";
    foreach (var x in a) if (x.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return x.Substring(p.Length);
    return null;
}

var port = int.Parse(GetArg(args, "port") ?? Environment.GetEnvironmentVariable("TRPG_PORT") ?? "5000");
var autoOpen = args.Contains("--autoOpen");
var openList = (GetArg(args, "open") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var server = new MasterServerInstance();
Console.WriteLine($"[MasterServer] 数据目录: {server.DataPath}");

await server.StartAsync(port);
Console.WriteLine($"[MasterServer] 已启动（单进程多房间），监听 http://localhost:{port}/im");

if (autoOpen)
{
    // 通过反射式设置：RoomRuntimeManager 的 AutoOpenOnConnect 需在实例内开启
    // 简化处理：预开启所有已存在房间
    foreach (var r in await server.GetAllRoomsAsync())
        server.OpenRoom(r.RoomId);
    Console.WriteLine($"[MasterServer] 已开启全部已存在房间");
}
foreach (var rid in openList)
{
    server.OpenRoom(rid);
    Console.WriteLine($"[MasterServer] 已开启房间: {rid}");
}

Console.WriteLine("[MasterServer] 按 Ctrl+C 停止");
var tcs = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(); };
await tcs.Task;

await server.StopAsync();
Console.WriteLine("[MasterServer] 已停止");
