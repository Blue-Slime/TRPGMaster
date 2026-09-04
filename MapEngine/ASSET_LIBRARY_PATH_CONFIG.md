# 素材库路径配置功能

## 功能说明

素材库路径支持三级优先级配置系统：

- **P1: 全局配置路径**（由启动器设置，所有模块共享）
- **P2: 模块默认路径**（可执行文件目录下的 `AssetLibrary`）
- **P3: 模块自定义路径**（在地图模块设置中配置）

用户在地图编辑器设置对话框中选择使用哪种路径来源。

## 三级优先级说明

### P1: 全局配置路径（由客户端设置）

**配置文件**: `%AppData%\TRPGMaster\settings.json`

**管理方式**: 由客户端主界面统一管理，所有模块（地图/卡牌/骰子）共享此路径。

**降级机制**: 如果全局配置文件不存在或 `assetLibraryPath` 字段为空，自动降级到模块默认路径。

**示例配置**:
```json
{
  "defaultRoomsPath": "G:\\跑团大师\\Rooms",
  "assetLibraryPath": "D:\\SharedAssets\\TRPGMaster",
  "userId": "player",
  "display": { "theme": "Dark" }
}
```

**查看方式**: 在地图编辑器设置对话框中查看（只读，灰色显示）。

### P2: 模块默认路径

**路径**: `<可执行文件目录>\AssetLibrary`

**使用场景**:
- 全局配置文件不存在时自动降级
- 用户在设置中手动选择"P2: 使用模块默认路径"

**特点**: 
- 不需要任何配置，开箱即用
- 开发环境自动适配（项目根目录下的 `AssetLibrary`）

### P3: 模块自定义路径

**配置文件**: `%AppData%\MapEditor\settings.json`

**字段**: `mapModuleCustomAssetPath`

**使用场景**: 用户希望地图模块使用独立的素材库路径，不跟随全局配置。

**示例配置**:
```json
{
  "mapModuleAssetPathMode": "Custom",
  "mapModuleCustomAssetPath": "D:\\MyMapAssets"
}
```

## 使用方法

### 1. 打开设置对话框

从主窗口菜单或工具栏点击"设置"按钮。

### 2. 选择路径模式

在"📁 素材库路径"区域，选择以下三个选项之一：

**选项 1: P1: 使用全局配置路径（由客户端设置）**
- 下方显示全局配置文件中的路径（只读）
- 如果显示"未配置"，系统会自动降级到 P2 模块默认路径
- 要修改全局路径，需要在客户端主界面设置中修改

**选项 2: P2: 使用模块默认路径**
- 强制使用可执行文件目录下的 `AssetLibrary` 文件夹
- 下方显示实际的模块默认路径（只读）
- 适合希望每个模块独立管理素材的用户

**选项 3: P3: 使用模块自定义路径**
- 在文本框中输入自定义路径，或点击"浏览..."选择文件夹
- 适合高级用户自定义素材库位置

### 3. 查看当前生效路径

"✓ 当前生效路径"区域显示系统实际使用的路径（考虑了降级机制）。

### 4. 保存并重启

- 点击"保存"按钮保存配置
- **重要**：修改后需要重启编辑器才能生效

## 配置文件位置

### 全局配置（由客户端维护）
```
%AppData%\TRPGMaster\settings.json
```

### 模块配置（由地图编辑器维护）
```
%AppData%\MapEditor\settings.json
```

可以点击"📂 打开模块配置文件夹"按钮快速访问。

## 配置文件示例

### 全局配置（settings.json）
```json
{
  "defaultRoomsPath": "G:\\跑团大师\\Rooms",
  "assetLibraryPath": "D:\\SharedAssets\\TRPGMaster",
  "userId": "player",
  "rememberPassword": true,
  "display": {
    "theme": "Dark",
    "fontSize": 14
  }
}
```

### 模块配置（settings.json）
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

**路径模式字段值**:
- `"Global"` - 使用全局配置路径
- `"ModuleDefault"` - 使用模块默认路径
- `"Custom"` - 使用模块自定义路径

## 技术实现

### 路径解析优先级

`AssetLibraryFileSystemService.ResolveRootPath()` 按以下逻辑解析：

```csharp
switch (settings.MapModuleAssetPathMode)
{
    case AssetPathMode.Global:
        // 读取 %AppData%\TRPGMaster\global-settings.json
        var globalPath = GlobalConfigStore.TryGetAssetLibraryPath();
        if (globalPath != null) return globalPath;
        // 降级到模块默认路径
        break;

    case AssetPathMode.ModuleDefault:
        // 直接使用模块默认路径
        break;

    case AssetPathMode.Custom:
        // 读取模块配置文件中的自定义路径
        if (customPath != null) return customPath;
        // 降级到模块默认路径
        break;
}

return GetModuleDefaultPath();  // 最终降级
```

### 修改的文件

1. **GlobalConfigStore.cs** (新增) — 读取启动器维护的全局配置文件
2. **GlobalSettings.cs** — 添加 `AssetPathMode` 枚举和 `MapModuleAssetPathMode` 字段
3. **AssetLibraryFileSystemService.cs** — 实现三级优先级路径解析逻辑
4. **SettingsDialog.axaml** — UI 添加三个 RadioButton 选择路径模式
5. **SettingsDialog.axaml.cs** — 路径模式切换和保存逻辑

### 设计决策

**为什么使用客户端主配置文件？**
1. **统一管理**: 所有全局配置集中在客户端主配置文件中，避免配置分散
2. **降级清晰**: "查不到全局配置文件"或"字段为空"是可区分的降级状态
3. **未来扩展**: 其他模块（卡牌/骰子）也可以读取同一个配置文件
4. **所有权分离**: 客户端创建和修改，地图模块只读不写

**为什么用 RadioButton 而不是优先级链？**
1. **用户意图明确**: P2 和P3 是用户主动选择，不是自动降级
2. **避免困惑**: 如果 P3 总是覆盖 P2，用户可能不知道为什么全局配置失效了

## Bug 修复记录

### 问题：素材库被创建到系统图库文件夹

**原因**：
- 旧版 `ResolveRootPath()` 优先使用 `Directory.GetCurrentDirectory()`
- 从 Windows 资源管理器启动时，工作目录是用户的"我的文档"或"图片"文件夹

**修复**：
- 优先级改为 `AppContext.BaseDirectory`（可执行文件目录）
- 开发环境才使用 `GetCurrentDirectory()`（检测 .csproj 文件）

## 测试验证清单

### 基础功能
- [ ] 打开设置对话框，显示三个路径模式选项
- [ ] P1 显示全局配置路径（如果有）或"未配置"
- [ ] P2 显示模块默认路径
- [ ] P3 切换时，自定义路径输入框和浏览按钮启用/禁用
- [ ] 点击"浏览..."按钮，文件夹选择器正常工作

### 路径解析
- [ ] 模式=Global，全局配置存在 → 使用全局路径
- [ ] 模式=Global，全局配置不存在 → 降级到模块默认路径
- [ ] 模式=ModuleDefault → 使用模块默认路径
- [ ] 模式=Custom，自定义路径有效 → 使用自定义路径
- [ ] 模式=Custom，自定义路径为空 → 降级到模块默认路径

### 持久化
- [ ] 保存配置，`settings.json` 正确写入 `mapModuleAssetPathMode`
- [ ] 重启编辑器，路径模式和自定义路径保留
- [ ] 切换模式后保存，素材库从新路径加载

### 降级机制
- [ ] 删除全局配置文件，P1 模式自动降级到 P2
- [ ] 清空自定义路径，P3 模式自动降级到 P2
- [ ] "当前生效路径"正确显示实际使用的路径

## 相关文档

- **GlobalConfigStore.cs** — 全局配置文件读取服务
- **GlobalSettings.cs** — 模块配置和路径模式枚举
- **AssetLibraryFileSystemService.cs** — 素材库文件系统服务
- **SettingsDialog.axaml** — 设置对话框 UI
