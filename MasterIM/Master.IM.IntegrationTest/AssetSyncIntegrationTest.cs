using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MasterIM.SDK;
using MasterIM.Server;

namespace MasterIM.IntegrationTest;

/// <summary>
/// 素材分类存储 + 语义命名 + 哈希索引的端到端集成测试。
/// 验证：客户端上传 → 服务端分类落盘 → 清单拉取 → 增量下载到本地同构缓存。
/// </summary>
public class AssetSyncIntegrationTest
{
    private static int _pass, _fail;

    public static async Task Run()
    {
        Console.WriteLine("=== 素材同步集成测试（分类存储 + 语义命名 + 哈希去重）===\n");

        const int port = 5601;
        var server = new MasterServerInstance();
        await server.StartAsync(port);
        var room = await server.CreateRoomAsync("素材测试房", "admin", 10);
        Console.WriteLine($"✓ 服务端已启动 (端口 {port})，房间 {room.RoomId}\n");

        var work = Path.Combine(Path.GetTempPath(), "asset-sync-test-" + Guid.NewGuid().ToString("N")[..8]);
        var srcDir = Path.Combine(work, "src");
        var cacheDir = Path.Combine(work, "cache");
        Directory.CreateDirectory(srcDir);

        // 语义命名要经得起中文和空格
        var pngA = Write(srcDir, "战士 头像.png", "PNG_DATA_WARRIOR");
        var pngADup = Write(srcDir, "战士-副本.png", "PNG_DATA_WARRIOR");   // 同内容，异名
        var pngB = Write(srcDir, "法师.png", "PNG_DATA_MAGE");
        var wav = Write(srcDir, "战鼓.wav", "WAV_DATA_DRUM");
        var pdf = Write(srcDir, "规则书.pdf", "PDF_DATA_RULES");

        var client = new IMClient();
        var join = await client.ConnectAsync($"ws://localhost:{port}/im", "gm", room.RoomId, "channel_lobby");
        if (join.Result != JoinResult.Success)
        {
            Console.WriteLine($"✗ 客户端连接失败: {join.Result}");
            return;
        }
        Console.WriteLine("✓ 客户端已连接\n");

        // ── 上传 ────────────────────────────────────────────────────────────
        Console.WriteLine("--- Test 1: 上传并分类落盘 ---");
        var rA = await client.UploadAssetAsync(pngA, "token");
        Check("token 入 tokens/ 且保留原名", rA.Success && rA.RelativePath == "tokens/战士 头像.png", rA.RelativePath);
        Check("返回内容哈希", rA.Hash.Length > 0, rA.Hash);

        var rWav = await client.UploadAssetAsync(wav, "audio");
        Check("audio 入 audio/", rWav.RelativePath == "audio/战鼓.wav", rWav.RelativePath);

        var rPdf = await client.UploadAssetAsync(pdf, "file");
        Check("file 入 files/", rPdf.RelativePath == "files/规则书.pdf", rPdf.RelativePath);

        var rB = await client.UploadAssetAsync(pngB, "map");
        Check("map 入 maps/", rB.RelativePath == "maps/法师.png", rB.RelativePath);

        Console.WriteLine("\n--- Test 2: 哈希去重 ---");
        var rDup = await client.UploadAssetAsync(pngADup, "token");
        Check("同内容异名被去重", rDup.Deduplicated, $"deduplicated={rDup.Deduplicated}");
        Check("去重后复用同一哈希", rDup.Hash == rA.Hash, $"{rDup.Hash} vs {rA.Hash}");
        Check("去重后复用原路径", rDup.RelativePath == rA.RelativePath, rDup.RelativePath);

        Console.WriteLine("\n--- Test 3: 同名不同内容加序号 ---");
        var collideDir = Path.Combine(work, "collide");
        Directory.CreateDirectory(collideDir);
        var pngACollide = Write(collideDir, "战士 头像.png", "PNG_DATA_DIFFERENT");
        var rCol = await client.UploadAssetAsync(pngACollide, "token");
        Check("同名异内容不覆盖", rCol.RelativePath == "tokens/战士 头像_1.png", rCol.RelativePath);
        Check("产生新哈希", rCol.Hash != rA.Hash, rCol.Hash);

        // ── 清单 ────────────────────────────────────────────────────────────
        Console.WriteLine("\n--- Test 4: 清单拉取 ---");
        var index = await client.GetRoomAssetIndexAsync();
        Check("清单含 5 条（去重的不额外计）", index.Count == 5, $"count={index.Count}");
        Check("清单带 relativePath", index.All(a => a.RelativePath.Contains('/')), "ok");
        Check("清单带 size", index.All(a => a.Size > 0), "ok");
        Check("类型分布正确",
            index.Count(a => a.Type == "token") == 2 &&
            index.Count(a => a.Type == "audio") == 1 &&
            index.Count(a => a.Type == "file") == 1 &&
            index.Count(a => a.Type == "map") == 1,
            string.Join(",", index.Select(a => a.Type).OrderBy(x => x)));

        // ── 增量同步 ────────────────────────────────────────────────────────
        Console.WriteLine("\n--- Test 5: 首次全量同步到本地缓存 ---");
        var n1 = await client.SyncRoomAssetsAsync(cacheDir);
        Check("下载数 == 清单数", n1 == index.Count, $"downloaded={n1}");

        Check("本地缓存与服务端同构：tokens/战士 头像.png",
            File.Exists(Path.Combine(cacheDir, "tokens", "战士 头像.png")), "ok");
        Check("本地缓存与服务端同构：audio/战鼓.wav",
            File.Exists(Path.Combine(cacheDir, "audio", "战鼓.wav")), "ok");
        Check("本地缓存与服务端同构：files/规则书.pdf",
            File.Exists(Path.Combine(cacheDir, "files", "规则书.pdf")), "ok");
        Check("本地缓存与服务端同构：maps/法师.png",
            File.Exists(Path.Combine(cacheDir, "maps", "法师.png")), "ok");
        Check("内容与上传一致",
            File.ReadAllText(Path.Combine(cacheDir, "tokens", "战士 头像.png")) == "PNG_DATA_WARRIOR", "ok");

        Console.WriteLine("\n--- Test 6: 二次同步应零下载（增量） ---");
        var n2 = await client.SyncRoomAssetsAsync(cacheDir);
        Check("已有文件不重复下载", n2 == 0, $"downloaded={n2}");

        Console.WriteLine("\n--- Test 7: 删本地单个文件后只补该文件 ---");
        File.Delete(Path.Combine(cacheDir, "audio", "战鼓.wav"));
        var n3 = await client.SyncRoomAssetsAsync(cacheDir);
        Check("只补 1 个", n3 == 1, $"downloaded={n3}");
        Check("补回成功", File.Exists(Path.Combine(cacheDir, "audio", "战鼓.wav")), "ok");

        Console.WriteLine("\n--- Test 8: 按哈希单独下载 ---");
        var one = await client.DownloadAssetAsync(rB.Hash, Path.Combine(work, "single"));
        Check("按哈希取到文件", one is not null && File.Exists(one), one ?? "null");
        Check("落盘保持分类路径", one?.Replace('\\', '/').EndsWith("maps/法师.png") == true, one ?? "null");
        var bad = await client.DownloadAssetAsync("nonexistent_hash_0000", Path.Combine(work, "single"));
        Check("不存在的哈希返回 null", bad is null, bad ?? "null");

        // 校验服务端落盘目录本身
        Console.WriteLine("\n--- Test 9: 服务端落盘结构 ---");
        var assetsRoot = MasterIM.Server.MapPersistence.RoomAssetPaths.GetAssetsDirectory(room.RoomId);
        var onDisk = Directory.GetFiles(assetsRoot, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(assetsRoot, p).Replace('\\', '/'))
            .OrderBy(x => x).ToList();
        Console.WriteLine("    " + string.Join("\n    ", onDisk));
        Check("无哈希命名的文件（全部语义命名）",
            onDisk.All(p => !System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileNameWithoutExtension(p), @"^[0-9a-f]{16,}$")), "ok");
        Check("index.json 存在", onDisk.Contains("index.json"), "ok");

        client.Dispose();
        await server.StopAsync();
        try { Directory.Delete(work, true); } catch { }

        Console.WriteLine($"\n=== 结果: {_pass} 通过 / {_fail} 失败 ===");
        if (_fail > 0) Environment.ExitCode = 1;
    }

    private static string Write(string dir, string name, string content)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private static void Check(string name, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✓ {name}"); }
        else    { _fail++; Console.WriteLine($"  ✗ {name}  实际: {actual}"); }
    }
}
