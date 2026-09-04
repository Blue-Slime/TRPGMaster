# Phase 1.1 完成报告：房间目录结构调整

## ✅ 已完成内容

### 1. 新增文件

#### 1.1 RoomAssetPaths.cs
- **路径管理中心**：统一管理房间内所有数据路径
- **核心方法**：
  - `GetScenesDirectory()` — scenes/ 目录
  - `GetMainScenePath()` — scenes/main.scene
  - `GetPrefabsDirectory()` — prefabs/ 目录
  - `GetAssetsDirectory()` — assets/ 根目录
  - `GetTokenAssetsDirectory()` — assets/tokens/
  - `GetMapAssetsDirectory()` — assets/maps/
  - `GetAudioAssetsDirectory()` — assets/audio/
  - `GetFileAssetsDirectory()` — assets/files/
  - `GetAssetIndexPath()` — assets/index.json
  - `GetAssetPath(roomId, type, hash, ext)` — 根据哈希获取素材路径
  - `EnsureDirectories()` — 确保所有必需目录存在
  - `GetLegacyScenePath()` — 兼容旧版 map.scene

#### 1.2 AssetMetadata.cs
- **素材元数据**：存储在 assets/index.json 中
- **字段**：
  - Hash — 内容哈希（唯一标识）
  - Type — 素材类型（token/map/audio/file）
  - FileName, DisplayName — 文件名和显示名
  - MimeType, Size, Width, Height — 文件信息
  - UploadedBy, UploadedAt — 上传者和时间
  - RefCount — 引用计数
  - References — 引用列表（哪些对象在使用）
  - Tags — 标签（搜索和分类）

- **AssetReference 类**：
  - Type — 引用类型（scene/prefab/message）
  - Path — 引用路径
  - ObjectId — 对象ID（scene 类型使用）

#### 1.3 AssetIndexManager.cs
- **索引管理器**：管理 assets/index.json
- **核心方法**：
  - `Load()` / `Save()` — 加载/保存索引
  - `HasAsset(hash)` — 检查素材是否存在
  - `GetAsset(hash)` — 获取素材元数据
  - `AddAsset(asset)` — 添加素材
  - `RemoveAsset(hash)` — 删除素材（同时删除物理文件）
  - `AddReference(hash, ref)` — 添加引用
  - `RemoveReference(hash, ref)` — 移除引用（**refCount == 0 立即删除素材，无保留期**）
  - `GetAllAssets()` — 获取所有素材
  - `GetAssetsByType(type)` — 按类型筛选
  - `SearchByTag(tag)` — 按标签搜索

#### 1.4 ContentHasher.cs
- **哈希计算工具**：SHA256 截取前 16 位
- **核心方法**：
  - `ComputeHash(filePath)` — 计算文件哈希
  - `ComputeHash(byte[])` — 计算字节数组哈希
  - `ComputeHash(Stream)` — 计算流哈希

### 2. 修改文件

#### 2.1 MapScenePersistence.cs

**SaveScene() 升级**：
```csharp
// 之前：data/rooms/{roomId}/map.scene
// 之后：data/rooms/{roomId}/scenes/main.scene

SaveScene(roomId, world, sceneName = "main")
```

**新增功能**：
- ✅ 支持多场景（传入 sceneName 参数）
- ✅ 原子写入（.tmp → 原子替换 → .backup）
- ✅ 防止保存时崩溃导致数据损坏

**LoadScene() 升级**：
```csharp
LoadScene(roomId, world, sceneName = "main")
```

**新增功能**：
- ✅ 自动从 .backup 恢复（主文件损坏时）
- ✅ 自动迁移旧版 map.scene 到 scenes/main.scene
- ✅ 迁移后将旧文件重命名为 map.scene.migrated

**DeleteScene() 升级**：
- 删除整个 scenes/ 目录（支持多场景）
- 清理旧版 map.scene 文件

---

## 📊 新目录结构

```
data/rooms/{roomId}/
├── scenes/                      ← 新增：场景目录
│   ├── main.scene               ← 主场景（从 map.scene 迁移）
│   ├── main.scene.backup        ← 自动备份
│   └── dungeon_floor1.scene     ← 支持多场景
│
├── prefabs/                     ← 新增：对象池目录
│   ├── goblin.prefab
│   └── torch.prefab
│
├── assets/                      ← 新增：房间素材库
│   ├── index.json               ← 素材索引
│   ├── tokens/
│   │   └── e3b0c44298fc1c14.png ← 按哈希命名
│   ├── maps/
│   │   └── a1b2c3d4e5f6.jpg
│   ├── audio/
│   │   └── 9d8e7f6a5b4c.mp3
│   └── files/
│       └── c4d5e6f7a8b9.pdf
│
├── map.scene.migrated           ← 旧文件（迁移后重命名）
│
└── messages/                    ← 现有：消息历史
    └── 2026-08/
        └── messages.db
```

---

## 🔧 兼容性与迁移

### 自动迁移流程

```
服务端启动 → 房间首次访问
↓
LoadScene() 检测旧版 map.scene
↓
1. 复制到 scenes/main.scene
2. 重命名旧文件为 map.scene.migrated
3. 正常加载场景
↓
SaveScene() 后续保存到新路径
```

**用户无感知**：
- ✅ 旧房间自动迁移
- ✅ 不丢失任何数据
- ✅ 保留旧文件作为备份

---

## 🎯 核心设计决策

### 1. 引用计数零延迟删除

**决策**：refCount == 0 立即删除素材，无 7 天保留期

**理由**：
- 用户需求：不要误删保护
- 简化逻辑：无需垃圾回收定时器
- 即时释放：空间立即回收

**实现**：
```csharp
public void RemoveReference(string hash, AssetReference reference)
{
    asset.References.Remove(reference);
    asset.RefCount = asset.References.Count;
    
    if (asset.RefCount == 0)
    {
        RemoveAsset(hash);  // 立即删除
    }
}
```

### 2. 内容寻址存储（Content-Addressable）

**决策**：素材按哈希命名，存储在 assets/{type}/{hash}.{ext}

**优点**：
- ✅ 自动去重（相同内容只存一份）
- ✅ 缓存友好（哈希不变则内容不变）
- ✅ 防篡改（内容变化则哈希变化）

**示例**：
```
goblin.png → SHA256 → e3b0c44298fc1c14
存储为: assets/tokens/e3b0c44298fc1c14.png

用户导入 3 次 goblin.png
→ 磁盘只有 1 个文件
→ 3 个不同的引用指向它
```

### 3. 原子写入保护

**决策**：保存时先写 .tmp，再原子替换，保留 .backup

**防止问题**：
- 保存时崩溃 → 主文件损坏
- 磁盘满 → 写入失败导致数据丢失

**恢复流程**：
```
LoadScene() 尝试加载
↓
main.scene 损坏
↓
自动从 main.scene.backup 恢复
↓
成功加载
```

---

## 📋 测试验证清单

### 新目录结构

- [ ] 服务端启动后创建房间，自动生成 scenes/prefabs/assets/ 目录
- [ ] 场景保存到 scenes/main.scene
- [ ] 生成 main.scene.backup 备份文件

### 旧版迁移

- [ ] 有旧版 map.scene 的房间自动迁移到 scenes/main.scene
- [ ] 旧文件重命名为 map.scene.migrated
- [ ] 迁移后场景数据完整（对象、组件全部保留）

### 原子写入

- [ ] 保存场景时生成 .tmp 文件
- [ ] 保存成功后 .tmp 文件消失
- [ ] main.scene.backup 存在且可用

### 崩溃恢复

- [ ] 删除 main.scene，重启服务端
- [ ] 自动从 backup 恢复
- [ ] 场景数据完整

---

## 🚀 下一步：Phase 1.2

### 素材导入与哈希去重

**目标**：修改素材导入流程，实现内容寻址存储

**任务**：
1. 修改 AssetImporter（地图编辑器客户端）
   - 导入素材时计算哈希
   - 上传到服务端前检查是否已存在
   - 已存在则直接引用，不重复上传

2. 服务端素材上传 API
   - `POST /api/rooms/{roomId}/assets/upload`
   - 接收文件 + 元数据
   - 计算哈希
   - 保存到 assets/{type}/{hash}.{ext}
   - 更新 assets/index.json

3. 修改 SceneSerializer
   - `SourceAssetPath` → `AssetRef`（存哈希）
   - 序列化时只保存哈希
   - 反序列化时从索引查询文件路径

**预计时间**：3-4 小时

---

## 📊 Phase 1.1 统计

| 维度 | 数量 |
|------|------|
| 新增文件 | 4 个 |
| 修改文件 | 1 个 |
| 代码行数 | ~500 行 |
| 编译状态 | ✅ 成功（1 警告，0 错误）|
| 实施时间 | ~2 小时 |

---

## ✅ Phase 1.1 完成确认

- ✅ 房间目录结构调整完成
- ✅ 素材索引系统基础完成
- ✅ 内容哈希工具完成
- ✅ 原子写入保护完成
- ✅ 旧版自动迁移完成
- ✅ 引用计数零延迟删除完成
- ✅ 编译通过

**准备进入 Phase 1.2：素材导入与哈希去重**
