# 素材导入和库源切换功能

## 功能概述

本功能实现了两个核心特性：

1. **哈希命名的素材导入** — 图片进入素材库时自动重命名为 `{SHA256}{ext}`，避免文件名冲突
2. **本地/房间库切换** — 客户端模式下支持在「本地库」和「房间库」之间切换，房间库支持多人协同

## 1. 哈希命名的素材导入

### 问题背景

- 旧版导入使用原文件名直接复制，多人上传同名文件会互相覆盖
- `.asset` 文件引用精灵图时使用 `"sprite": "example.png"` 这种相对路径，文件名变化后引用失效
- 需要一套稳定的命名规则，让文件名本身就能标识内容（内容寻址）

### 去重和命名规则

#### 图片文件（哈希命名，内容去重）

**规则**: 相同内容的图片只保存一份，文件名为 `{SHA256}{ext}`

```
森林地块A.png (SHA256: abc123...) → abc123def.png
森林地块B.png (SHA256: abc123...) → 跳过复制（哈希相同，已存在）
沙漠地块.png  (SHA256: 789xyz...) → 789xyzabc.png
```

**特点**:
- ✅ 相同内容的图片自动去重（即使文件名不同）
- ✅ 不同内容的图片不会冲突（哈希必然不同）
- ✅ 文件名稳定（内容不变，哈希不变）
- ❌ **不允许** 文件名相同但内容不同的图片（会被哈希重命名，丢失原文件名）

#### .asset 文件（用户友好命名，允许重名）

**规则**: 保留用户友好的原文件名，冲突时自动编号

```
森林地块.asset     → 森林地块.asset
森林地块.asset     → 森林地块 2.asset  (第二次导入同名图片)
森林地块.asset     → 森林地块 3.asset  (第三次)
```

**特点**:
- ✅ 用户看到的是有意义的文件名（"森林地块"而非"abc123def"）
- ✅ 允许多个 `.asset` 引用同一张图片（内容去重，对象不去重）
- ✅ 冲突时自动编号，不会覆盖已有文件
- ✅ **允许** 同名 `.asset` 对象存在（编号区分）

#### 示例：三人同时上传相同内容的图片

```
用户 A 拖入 "森林-春.png" (内容 X)
  → 图片: abc123def.png
  → 对象: 森林-春.asset (assetRef: "abc123def")

用户 B 拖入 "forest_spring.png" (内容 X，相同)
  → 图片: 跳过复制（abc123def.png 已存在）
  → 对象: forest_spring.asset (assetRef: "abc123def")

用户 C 拖入 "森林-春.png" (内容 X，相同)
  → 图片: 跳过复制（abc123def.png 已存在）
  → 对象: 森林-春 2.asset (assetRef: "abc123def")
```

**结果**: 三个用户创建了 3 个对象，但只占用 1 份图片存储空间。

### 实现方案

#### 导入时哈希命名

**入口点**: `AssetImporter.ImportImageFile()`

```csharp
// MapEngine.Avalonia/Services/AssetImporter.cs
public static string ImportImageFile(string sourceImagePath, string targetFolderPath)
{
    var ext = Path.GetExtension(sourceImagePath);
    var hash = ComputeSHA256(sourceImagePath);
    var targetFileName = $"{hash}{ext}";
    var targetPath = Path.Combine(targetFolderPath, targetFileName);
    
    if (!File.Exists(targetPath))
    {
        File.Copy(sourceImagePath, targetPath);
    }
    
    return targetPath;  // G:\Rooms\room-abc\AssetLibrary\abc123def.png
}
```

#### 引用时使用 AssetRef

**`.asset` 文件格式**（新）:

```json
{
  "name": "森林地块",
  "type": "StaticObjectClass",
  "components": [
    {
      "type": "Transform"
    },
    {
      "type": "SpriteRenderer",
      "properties": {
        "assetRef": "abc123def456.png"
      }
    }
  ]
}
```

**关键变化**:
- `"sprite": "example.png"` → `"assetRef": "abc123def.png"`
- 字段名从 `sprite` 改为 `assetRef`（明确表示这是资产引用，不是直接路径）
- 值从原始文件名改为哈希文件名

### 修改的文件

1. **AssetImporter.cs** (新建) — 导入逻辑集中点
   - `ImportImageFile()` — 计算哈希并复制到目标文件夹
   - `ComputeSHA256()` — SHA256 哈希计算

2. **MainWindow.axaml.cs** — 拖拽导入入口
   - `AssetLibrary_Drop()` — 调用 `AssetImporter.ImportImageFile()` 而非直接 `File.Copy()`

3. **AssetLibraryFileSystemService.cs** — 素材库初始化
   - `EnsureRootScaffold()` — 示例 `.asset` 文件使用 `"assetRef"` 字段
   - `CreateStaticObjectFile()` — 新建静态对象时不写 `sprite` 字段

4. **MapSpriteAssetResolver.cs** — 精灵图解析器
   - `FindByAssetRef()` — 根据哈希文件名查找精灵图，支持多个根目录
   - `FindByHash()` — 向后兼容旧的查找方式（仅查本地库）

5. **MapSceneBuilder.cs** — 场景构建器
   - `BuildSprites()` — 读取 `"assetRef"` 字段并调用 `resolver.FindByAssetRef()`

6. **CLI 导入命令** (假设存在) — 命令行导入入口
   - 应使用 `AssetImporter.ImportImageFile()` 而非自行复制

### 无兼容性代码

**原则**: 商用前删除所有旧数据，不保留向后兼容逻辑

- 不支持读取旧的 `"sprite"` 字段
- 不支持原文件名查找
- 导入时强制重命名，不保留原名

### 测试验证

- [x] 拖拽图片到素材库 → 文件重命名为 `{hash}{ext}`
- [x] 新建 `.asset` 并设置精灵图 → `"assetRef"` 字段写入哈希文件名
- [x] 地图场景加载 Token → 精灵图正确显示
- [x] 构建成功，无编译错误

---

## 2. 本地/房间库切换

### 功能说明

客户端模式下，素材库支持两种数据源：

- **本地库** — `%UserProfile%\Documents\TRPGMaster\AssetLibrary`，单机使用，跨房间复用
- **房间库** — `G:\Rooms\<roomId>\AssetLibrary`，房间成员共享，支持多人协同

用户通过素材库面板标题栏的「本地/房间」单选按钮切换。

### 双库优先级和去重

#### 当前实现：单库显示模式

**重要**: 当前版本的素材库面板是「单库显示」模式，不是「双库合并」模式。

- 选择「本地」→ 只显示本地库的文件夹和素材
- 选择「房间」→ 只显示房间库的文件夹和素材
- **不会** 同时显示两个库的内容

#### 精灵图查找：双库搜索模式

虽然面板是单库显示，但 **地图渲染** 时的精灵图查找支持双库搜索：

```
场景加载时，Token 引用 assetRef="abc123def.png"
  
MapSpriteAssetResolver.FindByAssetRef("abc123def.png")
  1. 查找本地库: C:\Users\...\AssetLibrary\abc123def.png
     → 找到 ✓ 返回本地路径
     
  2. 查找房间库: G:\Rooms\room-abc\AssetLibrary\abc123def.png
     → 仅当本地库未找到时才查房间库
```

**设计原则**: 本地库优先，允许用户"覆盖"房间库的默认资产

#### 典型场景

**场景 1: 用户切换到「房间」库，看不到本地素材**

```
本地库有 50 张地块
用户切换到「房间」库
  → 素材面板只显示房间库的内容
  → 本地库的 50 张地块不在列表中
  
但是：
  → 如果地图上已经放置了本地库的 Token
  → 渲染时依然能找到图片（双库搜索生效）
```

**场景 2: 用户想同时使用两个库的素材**

```
当前做法：
  1. 在「本地」库选择素材 A，拖到地图
  2. 切换到「房间」库，选择素材 B，拖到地图
  3. 两个 Token 都能正常显示（查找时双库搜索）

未来改进：
  - 支持「合并视图」模式，同时显示两个库
  - 用图标区分素材来源（本地/房间）
```

**场景 3: 本地库"覆盖"房间库的默认资产**

```
房间库有默认 Token: abc123def.png (官方头像)
用户本地库导入自定义头像（内容不同，但故意用相同哈希名）
  
切换到「房间」库：
  → 面板显示房间库的 abc123def.png（官方版本）
  
但地图渲染时：
  → 查找优先级: 本地库 > 房间库
  → 实际渲染本地库的自定义头像
```

#### 查找逻辑代码

```csharp
// MapEngine.Avalonia/Graphics/MapSpriteAssetResolver.cs
public string? FindByAssetRef(string assetRef)
{
    // 1. 先搜本地库
    var localPath = Path.Combine(_localAssetRoot, assetRef);
    if (File.Exists(localPath)) return localPath;
    
    // 2. 再搜房间库（仅当本地库未找到）
    if (_roomAssetRoot != null)
    {
        var roomPath = Path.Combine(_roomAssetRoot, assetRef);
        if (File.Exists(roomPath)) return roomPath;
    }
    
    // 3. 两个库都没找到
    return null;
}
```

### 用户提示和最佳实践

#### 行为总结表

| 操作 | 图片文件 | .asset 对象 | 素材面板显示 | 地图渲染查找 |
|------|---------|------------|-------------|-------------|
| 导入相同内容的图片 | ❌ 只保存一份（哈希去重） | ✅ 创建多个对象（允许重名） | 显示所有对象 | 查找唯一图片 |
| 导入相同文件名的图片 | ✅ 重命名为哈希（不冲突） | ✅ 对象自动编号（不覆盖） | 显示所有对象 | 查找各自图片 |
| 切换到「本地」库 | - | - | **只显示本地库** | 本地库 → 房间库 |
| 切换到「房间」库 | - | - | **只显示房间库** | 本地库 → 房间库 |
| 两个库有相同哈希图片 | - | - | 显示当前库的对象 | **本地库优先** |

#### 关键规则

✅ **允许的操作**:
- 多个 `.asset` 对象引用同一张图片（内容去重，对象不去重）
- 同名 `.asset` 对象共存（自动编号：`森林地块.asset` → `森林地块 2.asset`）
- 本地库和房间库有相同哈希的图片（查找时本地库优先）

❌ **不允许的操作**:
- 相同哈希的图片重复存储（自动去重，`File.Copy` 会跳过已存在的文件）
- 不同内容但相同文件名的图片共存（会被哈希重命名，原文件名丢失）

#### 导入提示

当用户拖入图片到素材库时，应显示：

```
✓ 导入成功: 森林地块.asset
  → 图片: abc123def.png (SHA256 哈希命名)
  → 大小: 512 KB
  → 去重: 跳过复制（内容已存在）
```

**去重提示说明**:
- "跳过复制" = 相同内容的图片已在库中，节省了存储空间
- 但 `.asset` 对象依然创建，用户可以独立配置属性

#### 切换库提示

当用户点击「房间」按钮时：

```
⚠️ 切换到房间库
  → 素材将与房间成员共享
  → 本地库的自定义素材优先级更高（可覆盖房间默认资产）
  → 离开房间后可随时切回本地库
```

#### 冲突解决最佳实践

**问题**: 两个库都有 `abc123def.png`，但我想用房间库版本怎么办？

**解决方案**:
1. 删除本地库的 `abc123def.png`
2. 刷新素材库（会自动回退到房间库版本）

或者：

1. 从本地库导出要保留的 `.asset` 对象
2. 清空本地库
3. 重新导入需要的素材

**设计原则**: 本地库是"个人工作区"，房间库是"团队共享区"。用户对本地库有完全控制权。

### 架构设计

#### 宿主接口扩展

```csharp
// MapEngine.Core/Hosting/IMapEditorHost.cs
public interface IMapEditorHost
{
    string ProjectRootPath { get; }
    string? RoomAssetLibraryPath { get; }  // 新增：房间素材库根路径，单机模式返回 null
    // ...
}
```

#### 单机宿主

```csharp
// MapEngine.Avalonia/Services/StandaloneMapEditorHost.cs
public string? RoomAssetLibraryPath => null;  // 单机模式无房间库
```

#### 客户端适配器

```csharp
// TRPGMaster.Desktop/Modules/MapEngineHostAdapter.cs
public string? RoomAssetLibraryPath
{
    get
    {
        if (_client.CurrentRoomId == null) return null;
        var roomDataPath = Path.Combine(_client.Settings.DefaultRoomsPath, _client.CurrentRoomId);
        return Path.Combine(roomDataPath, "AssetLibrary");
    }
}
```

#### ViewModel 切换逻辑

```csharp
// MapEngine.Avalonia/ViewModels/Partials/MainWindowViewModel.AssetLibrary.cs
private string _localAssetLibraryRootPath;   // 本地库根路径（来自 GlobalSettings）
private string? _roomAssetLibraryRootPath;   // 房间库根路径（来自 Host）
private string _assetLibraryRootPath;        // 当前生效根路径

public bool IsLocalAssetSourceActive
{
    get => _assetLibraryRootPath == _localAssetLibraryRootPath;
    set
    {
        if (value)
        {
            _assetLibraryRootPath = _localAssetLibraryRootPath;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRoomAssetSourceActive));
            ReloadAssetLibrary();
        }
    }
}

public bool IsRoomAssetSourceActive
{
    get => _assetLibraryRootPath == _roomAssetLibraryRootPath;
    set
    {
        if (value && _roomAssetLibraryRootPath != null)
        {
            _assetLibraryRootPath = _roomAssetLibraryRootPath;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalAssetSourceActive));
            ReloadAssetLibrary();
        }
    }
}

public bool IsRoomAssetLibraryAvailable => _roomAssetLibraryRootPath != null;
```

#### 双库解析

```csharp
// MapEngine.Avalonia/Graphics/MapSpriteAssetResolver.cs
public string? FindByAssetRef(string assetRef)
{
    // 先搜本地库
    var localPath = Path.Combine(_localAssetRoot, assetRef);
    if (File.Exists(localPath)) return localPath;
    
    // 再搜房间库
    if (_roomAssetRoot != null)
    {
        var roomPath = Path.Combine(_roomAssetRoot, assetRef);
        if (File.Exists(roomPath)) return roomPath;
    }
    
    return null;
}
```

**查找顺序**: 本地库优先，房间库次之。这样房间库可以"覆盖"本地库的同名资产。

### UI 设计

**位置**: 素材库面板标题栏，搜索框和面包屑之间

```xml
<StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="4">
    <RadioButton Content="本地" FontSize="10" Padding="8,2"
                 IsChecked="{Binding IsLocalAssetSourceActive, Mode=TwoWay}"
                 ToolTip.Tip="使用本地素材库（跨房间复用）"/>
    <RadioButton Content="房间" FontSize="10" Padding="8,2"
                 IsChecked="{Binding IsRoomAssetSourceActive, Mode=TwoWay}"
                 IsEnabled="{Binding IsRoomAssetLibraryAvailable}"
                 ToolTip.Tip="使用房间素材库（房间成员共享）"/>
</StackPanel>
```

**样式**: Owlbear 胶囊风格，10px 小字，灰色背景，选中时高亮

### 修改的文件

1. **IMapEditorHost.cs** — 宿主接口新增 `RoomAssetLibraryPath` 属性

2. **StandaloneMapEditorHost.cs** — 单机宿主实现返回 `null`

3. **MapEngineHostAdapter.cs** — 客户端适配器从 `_client.Settings.DefaultRoomsPath` 计算房间库路径

4. **MainWindowViewModel.AssetLibrary.cs** — 新增：
   - `_localAssetLibraryRootPath` / `_roomAssetLibraryRootPath` 字段
   - `IsLocalAssetSourceActive` / `IsRoomAssetSourceActive` / `IsRoomAssetLibraryAvailable` 属性
   - 切换逻辑触发 `ReloadAssetLibrary()`

5. **MainWindowViewModel.cs** (构造函数) — 初始化两个库根路径：
   ```csharp
   _localAssetLibraryRootPath = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
   _roomAssetLibraryRootPath = _host.RoomAssetLibraryPath;
   _assetLibraryRootPath = _localAssetLibraryRootPath;  // 默认本地库
   ```

6. **MapSpriteAssetResolver.cs** — 新增：
   - `_roomAssetRoot` 字段（可空）
   - 构造函数接受两个根路径
   - `FindByAssetRef()` 依次搜索本地库和房间库

7. **MapSceneBuilder.cs** — `Build()` 方法注入房间库根路径到 resolver：
   ```csharp
   var resolver = new MapSpriteAssetResolver(
       viewModel._localAssetLibraryRootPath,
       viewModel._roomAssetLibraryRootPath
   );
   ```

8. **MainWindow.axaml** — 素材库标题栏新增本地/房间切换 UI（Grid 列数从 6 列改为 7 列）

### 测试验证

- [x] 单机模式下「房间」按钮禁用（灰色）
- [ ] 客户端模式下「房间」按钮启用，点击切换后素材库重新加载
- [ ] 本地库有精灵图 A，房间库有精灵图 B → 切换后地图显示对应精灵图
- [ ] 两个库都有相同哈希的精灵图 → 本地库优先
- [ ] 构建成功，无编译错误

---

## 技术决策

### 为什么用 SHA256 而非文件名？

- **内容寻址** — 相同内容的文件得到相同哈希，自动去重
- **避免冲突** — 多人上传时不会因文件名相同而覆盖
- **稳定引用** — 文件内容不变，哈希不变，引用永远有效

### 为什么本地库优先于房间库？

- **用户控制** — 用户可以通过本地库"覆盖"房间库的默认资产
- **离线友好** — 本地库始终可用，不依赖房间连接
- **性能** — 本地磁盘访问比网络共享更快

### 为什么不支持旧的 `sprite` 字段？

- **商用前清理** — 用户要求"不希望有任何面向旧版的兼容性代码"
- **简化维护** — 单一字段名减少分支逻辑
- **明确语义** — `assetRef` 比 `sprite` 更清晰表达"这是资产引用"

---

## 常见问题

### Q1: 为什么我导入了一张图片，但素材库里看不到图片文件，只有 .asset 对象？

**答**: 图片文件被哈希重命名了（例如 `abc123def.png`），而素材面板只显示 `.asset` 对象。

- ✅ 对象名称保留用户友好的原文件名（"森林地块.asset"）
- ✅ 图片文件存储为哈希名（"abc123def.png"），避免冲突
- ✅ `.asset` 对象通过 `assetRef` 字段引用图片哈希

### Q2: 我导入了两张内容完全相同的图片（文件名不同），为什么只占用了一份存储空间？

**答**: 哈希去重机制自动识别相同内容，只保存一份图片文件。

```
导入 "森林A.png" → 图片: abc123def.png, 对象: 森林A.asset
导入 "森林B.png" (内容相同) → 图片: 跳过复制, 对象: 森林B.asset

结果: 2 个对象共享 1 张图片
```

### Q3: 我切换到「房间」库后，为什么看不到本地库的素材了？

**答**: 当前版本是「单库显示」模式，不会同时显示两个库。

- 选择「本地」→ 只显示本地库
- 选择「房间」→ 只显示房间库
- **但是**: 地图渲染时会同时搜索两个库（本地库优先）

**变通方法**: 在两个库之间来回切换，分别拖入需要的素材到地图。

### Q4: 两个库都有 abc123def.png，地图会显示哪个？

**答**: 本地库优先。

```
本地库: abc123def.png (自定义头像)
房间库: abc123def.png (官方头像)

地图渲染: 显示本地库的自定义头像
```

**如果想用房间库版本**: 删除本地库的 `abc123def.png`，刷新地图。

### Q5: 为什么我的图片文件名变成了一串乱码（abc123def...）？

**答**: 这是 SHA256 哈希值，不是乱码，是文件内容的唯一标识符。

**优点**:
- ✅ 相同内容的文件自动去重（节省空间）
- ✅ 不同内容的文件永远不会冲突
- ✅ 内容不变，文件名不变（引用稳定）

**对用户的影响**: 几乎没有。用户通过 `.asset` 对象操作素材，看到的是友好名称，而非哈希文件名。

### Q6: 我能手动修改图片的哈希文件名吗？

**答**: ❌ 不建议。

- 如果你重命名 `abc123def.png` → `my-sprite.png`，所有引用它的 `.asset` 对象会失效（找不到图片）
- `.asset` 对象的 `assetRef` 字段存储的是哈希值，不是文件名

**正确做法**: 修改 `.asset` 对象的 `name` 字段（用户看到的名称），而不是图片文件名。

### Q7: 房间库的素材会占用我的本地磁盘空间吗？

**答**: 取决于部署方式。

- 如果房间库在网络共享目录（`G:\Rooms\...`）→ 不占用本地空间
- 如果房间库在本地磁盘（测试环境）→ 占用本地空间

**实际生产环境**: 房间库会部署在服务器/NAS，玩家通过网络访问，不占本地空间。

### Q8: 我删除了 .asset 对象，图片文件会被删除吗？

**答**: ❌ 不会。图片文件和 `.asset` 对象是独立的。

- 删除 `.asset` 对象 → 素材面板里看不到这个对象了
- 图片文件依然存在（`abc123def.png`）
- 其他 `.asset` 对象如果引用同一张图片，不受影响

**手动清理孤立图片**: 当前版本没有自动清理功能，需要手动删除不再使用的图片文件。

---

## 相关文档

- [Map Implementation Status](map-implementation-status.md) — 地图模块完整实现状态表
- [Sprite Loading Flow](sprite-loading-flow.md) — Token 精灵图加载显示完整流程
- [TRPG Networking Complete Status](trpg-networking-complete-status.md) — 联机系统实现状态总结
