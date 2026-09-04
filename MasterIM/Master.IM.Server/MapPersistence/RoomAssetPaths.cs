using System;
using System.IO;

namespace MasterIM.Server.MapPersistence;

/// <summary>
/// 房间目录结构管理：统一管理房间内的所有数据路径
/// </summary>
public static class RoomAssetPaths
{
    /// <summary>
    /// 获取房间根目录
    /// </summary>
    public static string GetRoomRoot(string roomId)
    {
        var dataPath = SharedPaths.ResolveDataPath();
        return Path.Combine(dataPath, "rooms", roomId);
    }

    /// <summary>
    /// 获取场景目录（scenes/）
    /// </summary>
    public static string GetScenesDirectory(string roomId)
    {
        return Path.Combine(GetRoomRoot(roomId), "scenes");
    }

    /// <summary>
    /// 获取主场景路径（scenes/main.scene）
    /// </summary>
    public static string GetMainScenePath(string roomId)
    {
        return Path.Combine(GetScenesDirectory(roomId), "main.scene");
    }

    /// <summary>
    /// 获取指定场景路径（scenes/{sceneName}.scene）
    /// </summary>
    public static string GetScenePath(string roomId, string sceneName)
    {
        return Path.Combine(GetScenesDirectory(roomId), $"{sceneName}.scene");
    }

    /// <summary>
    /// 获取预制体目录（prefabs/）
    /// </summary>
    public static string GetPrefabsDirectory(string roomId)
    {
        return Path.Combine(GetRoomRoot(roomId), "prefabs");
    }

    /// <summary>
    /// 获取预制体路径（prefabs/{prefabName}.prefab）
    /// </summary>
    public static string GetPrefabPath(string roomId, string prefabName)
    {
        return Path.Combine(GetPrefabsDirectory(roomId), $"{prefabName}.prefab");
    }

    /// <summary>
    /// 获取房间素材库根目录（assets/）
    /// </summary>
    public static string GetAssetsDirectory(string roomId)
    {
        return Path.Combine(GetRoomRoot(roomId), "assets");
    }

    /// <summary>
    /// 获取素材索引文件路径（assets/index.json）
    /// </summary>
    public static string GetAssetIndexPath(string roomId)
    {
        return Path.Combine(GetAssetsDirectory(roomId), "index.json");
    }

    /// <summary>
    /// 获取 Token 素材目录（assets/tokens/）
    /// </summary>
    public static string GetTokenAssetsDirectory(string roomId)
    {
        return Path.Combine(GetAssetsDirectory(roomId), "tokens");
    }

    /// <summary>
    /// 获取地图素材目录（assets/maps/）
    /// </summary>
    public static string GetMapAssetsDirectory(string roomId)
    {
        return Path.Combine(GetAssetsDirectory(roomId), "maps");
    }

    /// <summary>
    /// 获取音频素材目录（assets/audio/）
    /// </summary>
    public static string GetAudioAssetsDirectory(string roomId)
    {
        return Path.Combine(GetAssetsDirectory(roomId), "audio");
    }

    /// <summary>
    /// 获取通用文件目录（assets/files/）
    /// </summary>
    public static string GetFileAssetsDirectory(string roomId)
    {
        return Path.Combine(GetAssetsDirectory(roomId), "files");
    }

    /// <summary>
    /// 根据素材类型获取素材目录
    /// </summary>
    public static string GetAssetDirectory(string roomId, string assetType)
    {
        return assetType.ToLowerInvariant() switch
        {
            "token" => GetTokenAssetsDirectory(roomId),
            "map" => GetMapAssetsDirectory(roomId),
            "audio" => GetAudioAssetsDirectory(roomId),
            "file" => GetFileAssetsDirectory(roomId),
            _ => GetFileAssetsDirectory(roomId)
        };
    }

    /// <summary>
    /// 获取素材文件路径（已废弃：现使用语义命名+元数据索引）
    /// 保留此方法仅用于删除遗留哈希命名文件，新代码应查询 asset-index.json 获取 relativePath。
    /// </summary>
    [Obsolete("使用语义命名架构，通过 asset-index.json 的 relativePath 字段获取实际路径")]
    public static string GetAssetPath(string roomId, string assetType, string hash, string extension)
    {
        var directory = GetAssetDirectory(roomId, assetType);
        return Path.Combine(directory, $"{hash}{extension}");
    }

    /// <summary>
    /// 确保所有必需的目录存在
    /// </summary>
    public static void EnsureDirectories(string roomId)
    {
        Directory.CreateDirectory(GetScenesDirectory(roomId));
        Directory.CreateDirectory(GetPrefabsDirectory(roomId));
        Directory.CreateDirectory(GetTokenAssetsDirectory(roomId));
        Directory.CreateDirectory(GetMapAssetsDirectory(roomId));
        Directory.CreateDirectory(GetAudioAssetsDirectory(roomId));
        Directory.CreateDirectory(GetFileAssetsDirectory(roomId));
    }
}
