using System;
using System.IO;
using System.Text.Json;
using MapEngine.Core;
using MapEngine.Core.Components;

namespace MasterIM.Server.MapPersistence;

/// <summary>
/// 地图场景持久化服务：负责将 World 保存到磁盘和从磁盘加载
/// </summary>
public static class MapScenePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 保存地图场景到磁盘（使用新目录结构 scenes/main.scene）
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <param name="world">World 对象</param>
    /// <param name="sceneName">场景名称，默认 "main"</param>
    public static void SaveScene(string roomId, World world, string sceneName = "main")
    {
        try
        {
            var scenePath = RoomAssetPaths.GetScenePath(roomId, sceneName);

            // 确保目录存在
            RoomAssetPaths.EnsureDirectories(roomId);

            // 导出场景文档
            var document = SceneSerializer.ToDocument(world.AllObjects());

            // 序列化为 JSON
            var json = JsonSerializer.Serialize(document, JsonOptions);

            // 原子写入：先写临时文件，再重命名
            var tempPath = scenePath + ".tmp";
            var backupPath = scenePath + ".backup";

            // 1. 写入临时文件
            File.WriteAllText(tempPath, json);

            // 2. 如果旧文件存在，备份
            if (File.Exists(scenePath))
            {
                File.Copy(scenePath, backupPath, overwrite: true);
            }

            // 3. 原子替换
            File.Move(tempPath, scenePath, overwrite: true);

            // 4. 清理临时文件
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            // 记录日志但不抛出异常，避免阻塞命令执行
            Console.WriteLine($"[MapPersistence] 保存场景失败 RoomId={roomId}: {ex.Message}");
        }
    }

    /// <summary>
    /// 从磁盘加载地图场景
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <param name="world">World 对象</param>
    /// <param name="sceneName">场景名称，默认 "main"</param>
    /// <returns>是否成功加载</returns>
    public static bool LoadScene(string roomId, World world, string sceneName = "main")
    {
        try
        {
            var scenePath = RoomAssetPaths.GetScenePath(roomId, sceneName);

            if (!File.Exists(scenePath))
            {
                // 尝试从 backup 恢复
                var backupPath = scenePath + ".backup";
                if (File.Exists(backupPath))
                {
                    Console.WriteLine($"[MapPersistence] 主场景损坏，从 backup 恢复 RoomId={roomId}");
                    File.Copy(backupPath, scenePath, overwrite: true);
                }
                else
                {
                    Console.WriteLine($"[MapPersistence] 场景文件不存在 RoomId={roomId}，使用空白场景");
                    return false;
                }
            }

            // 读取 JSON
            var json = File.ReadAllText(scenePath);

            // 反序列化场景文档
            var document = JsonSerializer.Deserialize<SceneDocument>(json, JsonOptions);
            if (document == null)
            {
                Console.WriteLine($"[MapPersistence] 场景文档反序列化失败 RoomId={roomId}");
                return false;
            }

            // 导入到 World
            var objects = SceneSerializer.FromDocument(document);
            foreach (var obj in objects)
            {
                world.AddObject(obj);
            }

            Console.WriteLine($"[MapPersistence] 场景加载成功 RoomId={roomId}，对象数={objects.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapPersistence] 加载场景失败 RoomId={roomId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 删除房间的地图场景文件（房间删除时调用）
    /// </summary>
    public static void DeleteScene(string roomId)
    {
        try
        {
            var scenesDir = RoomAssetPaths.GetScenesDirectory(roomId);
            if (Directory.Exists(scenesDir))
            {
                Directory.Delete(scenesDir, recursive: true);
                Console.WriteLine($"[MapPersistence] 场景目录已删除 RoomId={roomId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapPersistence] 删除场景失败 RoomId={roomId}: {ex.Message}");
        }
    }
}
