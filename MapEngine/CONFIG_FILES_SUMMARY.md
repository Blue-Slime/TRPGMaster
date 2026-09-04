# 配置文件统一管理方案

## 配置文件汇总表

| 项目 | 配置文件路径 | 负责类 | 用途 | 关键字段 |
|------|------------|--------|------|---------|
| **MasterClient (主客户端)** | `%APPDATA%\TRPGMaster\settings.json` | `SettingsService` → `AppSettings` | 客户端全局设置（登录/窗口/显示/通知/最近房间/**素材库路径**） | `DefaultRoomsPath`, `AssetLibraryPath`, `UserId`, `Display`, `TypingIndicator`, `FileTransfer`, `Notification`, `RecentRooms` |
| **MasterServerUI (服务器管理台)** | `%APPDATA%\TRPGMaster\serverui-settings.json` | `AppSettings` (静态方法) | 服务器管理台设置（网络/存储/安全/UI） | `Network.Port`, `Storage.DataPath`, `Security`, `UI` |
| **MapEngine (地图编辑器)** | `%APPDATA%\MapEditor\settings.json` | `GlobalSettingsStore` → `GlobalSettings` | 地图编辑器模块设置（地图包/网格/素材路径模式） | `PackageName`, `FeetPerCell`, `ShowGrid`, `AgentTcpPort`, `MapModuleAssetPathMode`, `MapModuleCustomAssetPath` |

## 设计原则

### ✅ 采用的方案：统一全局配置到客户端主配置

**配置层级**：
- **全局配置** — `%APPDATA%\TRPGMaster\settings.json`（所有模块共享）
- **模块配置** — `%APPDATA%\<ModuleName>\settings.json`（各模块专属）

**素材库路径三级优先级**：
1. **P1: 全局配置路径** — 客户端主配置中的 `assetLibraryPath` 字段
2. **P2: 模块默认路径** — 可执行文件目录下的 `AssetLibrary`
3. **P3: 模块自定义路径** — 地图模块配置中的 `mapModuleCustomAssetPath`

### 优势

1. **配置集中**：全局配置统一在 `TRPGMaster\settings.json`，避免分散
2. **所有权清晰**：客户端管理全局配置，模块只读全局配置，读写自己的模块配置
3. **扩展性好**：其他模块（卡牌/骰子/音乐）可以读取同一个全局配置
4. **用户友好**：只有两个配置文件路径，易于理解和维护

## 配置文件示例

### 客户端全局配置 (settings.json)

```json
{
  "defaultRoomsPath": "G:\\TRPGMaster\\Rooms",
  "assetLibraryPath": "D:\\SharedAssets\\TRPGMaster",
  "userId": "player",
  "rememberPassword": true,
  "display": {
    "theme": "Dark",
    "fontSize": 14,
    "showTimestamp": true
  },
  "typingIndicator": {
    "enabled": true,
    "sendCooldownSeconds": 4
  },
  "fileTransfer": {
    "chunkSizeKB": 64,
    "chunkTimeoutSeconds": 30
  },
  "notification": {
    "enableSound": true,
    "enableDesktopNotification": true
  },
  "recentRooms": []
}
```

### 地图模块配置 (MapEditor/settings.json)

```json
{
  "packageName": "默认地图包",
  "feetPerCell": 5,
  "defaultZoomPercent": 100,
  "showGrid": true,
  "agentTcpPort": 47821,
  "assetImportMode": "copy",
  "mapModuleAssetPathMode": "Global",
  "mapModuleCustomAssetPath": null
}
```

**路径模式字段值**：
- `"Global"` — 使用客户端全局配置路径
- `"ModuleDefault"` — 使用模块默认路径
- `"Custom"` — 使用模块自定义路径

### 服务器管理台配置 (serverui-settings.json)

```json
{
  "network": {
    "port": 7890,
    "bindAddress": "localhost",
    "maxConnections": 1000
  },
  "storage": {
    "dataPath": "./data",
    "maxCacheSize": 1024
  },
  "security": {
    "requirePassword": false,
    "maxLoginAttempts": 5
  },
  "ui": {
    "windowWidth": 1400,
    "windowHeight": 800,
    "autoRefresh": true
  }
}
```

## 配置文件读取规则

### 地图编辑器读取全局素材库路径

```csharp
// MapEngine.Avalonia/Services/GlobalConfigStore.cs
public static string? TryGetAssetLibraryPath()
{
    // 读取 %APPDATA%\TRPGMaster\settings.json
    var config = TryLoad();
    return string.IsNullOrWhiteSpace(config?.AssetLibraryPath) ? null : config.AssetLibraryPath;
}
```

### 三级优先级解析逻辑

```csharp
// MapEngine.Avalonia/Services/AssetLibraryFileSystemService.cs
private static string ResolveRootPath(string? configuredRootFolder)
{
    var settings = GlobalSettingsStore.Load();

    switch (settings.MapModuleAssetPathMode)
    {
        case AssetPathMode.Global:
            // P1: 读取客户端全局配置
            var globalPath = GlobalConfigStore.TryGetAssetLibraryPath();
            if (!string.IsNullOrWhiteSpace(globalPath))
                return Path.IsPathRooted(globalPath) ? globalPath : Path.Combine(AppContext.BaseDirectory, globalPath);
            break; // 降级到模块默认路径

        case AssetPathMode.ModuleDefault:
            // P2: 强制使用模块默认路径
            break;

        case AssetPathMode.Custom:
            // P3: 使用模块自定义路径
            if (!string.IsNullOrWhiteSpace(settings.MapModuleCustomAssetPath))
                return Path.IsPathRooted(settings.MapModuleCustomAssetPath) ? settings.MapModuleCustomAssetPath : Path.Combine(AppContext.BaseDirectory, settings.MapModuleCustomAssetPath);
            break; // 降级到模块默认路径
    }

    // 最终降级：模块默认路径
    return GetModuleDefaultPath();
}
```

## 迁移说明

### 从旧方案迁移

**旧方案**：`%APPDATA%\TRPGMaster\global-settings.json` 存储全局素材库路径

**新方案**：`%APPDATA%\TRPGMaster\settings.json` 中添加 `assetLibraryPath` 字段

**迁移步骤**：
1. ❌ 删除旧的 `global-settings.json` 文件（如果存在）
2. ✅ 在客户端 `settings.json` 中添加 `assetLibraryPath` 字段
3. ✅ MapEngine 的 `GlobalConfigStore` 改为读取 `settings.json` 而不是 `global-settings.json`
4. ✅ 更新 UI 提示文本：将"启动器"改为"客户端"

## 相关文档

- [ASSET_LIBRARY_PATH_CONFIG.md](./ASSET_LIBRARY_PATH_CONFIG.md) — 素材库路径配置详细说明
- [GlobalConfigStore.cs](./MapEngine.Avalonia/Services/GlobalConfigStore.cs) — 读取客户端全局配置
- [GlobalSettings.cs](./MapEngine.Avalonia/Services/GlobalSettings.cs) — 地图模块配置
- [AssetLibraryFileSystemService.cs](./MapEngine.Avalonia/Services/AssetLibraryFileSystemService.cs) — 素材库路径解析逻辑
- [MasterClient/Models/AppSettings.cs](../MasterClient/Models/AppSettings.cs) — 客户端全局配置模型
