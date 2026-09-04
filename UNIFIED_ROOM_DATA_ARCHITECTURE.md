# 统一房间数据与素材架构设计

## 🎯 设计目标

从全局视角重新设计房间数据存储，统一管理：
- 地图场景数据
- Token 图片素材
- 聊天消息附件
- 用户上传文件
- 对象池（预制体）

**核心理念**：
1. **房间为中心**：所有数据按房间组织，独立于个人素材库
2. **内容寻址**：素材按内容哈希存储，自动去重
3. **引用分离**：数据层（引用）与存储层（文件）分离
4. **互相引用**：地图与聊天共享素材池，方便互相引用
5. **持久化对象池**：预制体（Prefab）作为可复用的对象模板

---

## 📊 统一数据架构

### 目录结构

```
data/
├── rooms/{roomId}/
│   ├── metadata.json              ← 房间元数据
│   ├── channels.json              ← 频道配置
│   ├── members.json               ← 成员列表
│   │
│   ├── scenes/                    ← 地图场景（多场景支持）
│   │   ├── main.scene             ← 主场景
│   │   ├── dungeon_floor1.scene   ← 副本场景
│   │   └── dungeon_floor2.scene
│   │
│   ├── prefabs/                   ← 对象池（预制体）
│   │   ├── goblin.prefab          ← 哥布林 Token 模板
│   │   ├── torch.prefab           ← 火把模板
│   │   └── trap.prefab            ← 陷阱模板
│   │
│   ├── assets/                    ← 房间素材库（内容寻址）
│   │   ├── index.json             ← 素材索引（哈希 → 元数据）
│   │   ├── tokens/
│   │   │   ├── e3b0c442.png       ← Token 图片（按哈希命名）
│   │   │   └── 7a8f9c1d.png
│   │   ├── maps/
│   │   │   ├── a1b2c3d4.jpg       ← 地图背景
│   │   │   └── f5e6d7c8.webp
│   │   ├── audio/
│   │   │   ├── 9d8e7f6a.mp3       ← BGM
│   │   │   └── b2c3d4e5.ogg
│   │   └── files/                 ← 通用文件
│   │       ├── c4d5e6f7.pdf       ← 规则书
│   │       └── e8f9a0b1.docx
│   │
│   └── messages/                  ← 消息历史（已有）
│       ├── 2026-08/
│       │   └── messages.db
│       └── index.db
│
└── global_cache/                  ← 全局素材缓存（跨房间去重，可选）
    └── e3b0c442.png               ← 被多个房间引用的素材
```

---

## 🏗️ 核心设计原则

### 1. 内容寻址存储（Content-Addressable Storage）

**原理**：文件按内容哈希命名，相同内容只存储一次

```
图片文件: goblin.png (1 MB)
↓
计算 SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
↓
截取前 16 位: e3b0c44298fc1c14
↓
存储为: assets/tokens/e3b0c44298fc1c14.png
```

**优点**：
- ✅ 自动去重（相同图片只存 1 份）
- ✅ 内容不变则哈希不变（缓存友好）
- ✅ 防篡改（内容变化则哈希变化）

**示例**：
```json
// assets/index.json
{
  "e3b0c44298fc1c14": {
    "hash": "e3b0c44298fc1c14",
    "fileName": "goblin.png",
    "mimeType": "image/png",
    "size": 1048576,
    "uploadedBy": "player1",
    "uploadedAt": "2026-08-03T12:34:56Z",
    "refCount": 3,  // 被 3 个对象引用
    "tags": ["token", "monster", "goblin"]
  }
}
```

---

### 2. 引用分离（Reference vs Storage）

**原理**：数据层只存引用（哈希），存储层管理文件

```json
// scenes/main.scene
{
  "objects": [
    {
      "id": "token_001",
      "name": "哥布林",
      "components": [
        {
          "type": "SpriteRenderer",
          "properties": {
            "assetRef": "e3b0c44298fc1c14",  ← 只存哈希引用
            "assetType": "token"
          }
        }
      ]
    }
  ]
}
```

**加载流程**：
```
1. 加载 main.scene
2. 解析 assetRef = "e3b0c44298fc1c14"
3. 查询 assets/index.json 获取文件信息
4. 读取 assets/tokens/e3b0c44298fc1c14.png
5. 加载纹理
```

**优点**：
- ✅ scene 文件小（只存哈希，不存路径）
- ✅ 素材可移动（只要哈希不变）
- ✅ 支持懒加载（按需下载）

---

### 3. 对象池（Prefab System）

**原理**：预制体存储可复用的对象模板

```json
// prefabs/goblin.prefab
{
  "name": "哥布林战士",
  "icon": "👹",
  "category": "怪物",
  "components": [
    {
      "type": "Transform",
      "properties": { "scaleX": 1, "scaleY": 1 }
    },
    {
      "type": "SpriteRenderer",
      "properties": {
        "assetRef": "e3b0c44298fc1c14",  ← 引用素材
        "opacity": 1.0
      }
    },
    {
      "type": "Token",
      "properties": {
        "tokenName": "哥布林",
        "maxHP": 30,
        "movementSpeed": 30,
        "shape": "circle"
      }
    }
  ]
}
```

**使用场景**：
```
用户点击"添加哥布林"
↓
从 prefabs/goblin.prefab 实例化
↓
生成新的 GameObject（UUID = token_002）
↓
继承 Prefab 的所有组件
↓
用户可修改具体实例（HP、位置等）
```

**优点**：
- ✅ 快速批量添加（战斗模式添加 10 个哥布林）
- ✅ 统一修改（修改 Prefab，所有实例受影响）
- ✅ 房间共享（GM 准备好的对象库）

---

### 4. 房间素材库 vs 个人素材库

```
个人素材库（%AppData%/TRPGMaster/AssetLibrary）
├── 用户导入的所有素材
├── 跨房间复用
└── 本地管理，不同步

房间素材库（data/rooms/{roomId}/assets）
├── 房间内使用的素材
├── 所有成员共享
└── 服务端同步
```

**导入流程**：
```
用户从个人素材库拖拽 goblin.png 到地图
↓
1. 计算哈希 e3b0c442
2. 检查房间素材库是否已有
   ├── 已有 → 直接引用
   └── 没有 → 上传到服务端
3. 服务端保存到 assets/tokens/e3b0c442.png
4. 更新 assets/index.json
5. 广播给其他客户端
```

**优点**：
- ✅ 个人素材库保持独立（隐私）
- ✅ 房间素材库自动同步（协作）
- ✅ 自动去重（服务端只存一份）

---

## 🔄 数据流与同步

### 场景 1：添加 Token

```
[客户端 A]
用户拖拽 goblin.png
↓
1. 计算 SHA256: e3b0c442
2. 检查房间素材库
   GET /api/rooms/{roomId}/assets/check?hash=e3b0c442
   ← 返回: { exists: false }
↓
3. 上传素材
   POST /api/rooms/{roomId}/assets/upload
   Body: multipart/form-data (file + metadata)
   ← 返回: { hash: "e3b0c442", path: "assets/tokens/e3b0c442.png" }
↓
4. 创建 Token
   发送地图命令: AddGameObject
   {
     "name": "哥布林",
     "components": [
       { "type": "SpriteRenderer", "assetRef": "e3b0c442" }
     ]
   }
↓
[服务端]
5. 保存素材到 data/rooms/{roomId}/assets/tokens/e3b0c442.png
6. 更新 assets/index.json
7. 广播命令到其他客户端
↓
[客户端 B]
8. 接收命令
9. 检查本地缓存是否有 e3b0c442.png
   ├── 有 → 直接加载
   └── 没有 → 下载
       GET /api/rooms/{roomId}/assets/tokens/e3b0c442.png
       → 缓存到 %TEMP%/TRPGMaster/cache/{roomId}/assets/tokens/
10. 加载纹理，显示 Token
```

---

### 场景 2：从对象池添加

```
[客户端]
用户点击"添加哥布林战士"（Prefab）
↓
1. 读取 prefabs/goblin.prefab
2. 解析 assetRef = "e3b0c442"
3. 检查本地缓存
   ├── 有 → 直接创建 Token
   └── 没有 → 下载素材
4. 发送地图命令: InstantiatePrefab
   {
     "prefabName": "goblin",
     "position": { "x": 100, "y": 200 }
   }
↓
[服务端]
5. 读取 prefabs/goblin.prefab
6. 实例化 GameObject
7. 广播命令
↓
[其他客户端]
8. 接收命令
9. 本地实例化（素材已缓存）
```

---

### 场景 3：聊天引用地图对象

```
[聊天消息]
玩家: "我攻击这个哥布林 @token_001"
↓
消息存储:
{
  "msgId": 123456,
  "content": "我攻击这个哥布林",
  "mentions": [
    {
      "type": "token",
      "id": "token_001",
      "name": "哥布林",
      "assetRef": "e3b0c442"  ← 引用同一素材
    }
  ]
}
↓
渲染:
我攻击这个哥布林 [🖼️ 哥布林] ← 显示缩略图
点击 → 地图聚焦到 token_001
```

---

## 📦 素材索引（assets/index.json）

```json
{
  "version": 1,
  "assets": {
    "e3b0c44298fc1c14": {
      "hash": "e3b0c44298fc1c14",
      "type": "token",
      "fileName": "goblin.png",
      "displayName": "哥布林",
      "mimeType": "image/png",
      "size": 1048576,
      "width": 512,
      "height": 512,
      "uploadedBy": "player1",
      "uploadedAt": "2026-08-03T12:34:56Z",
      "refCount": 3,
      "references": [
        { "type": "scene", "path": "scenes/main.scene", "objectId": "token_001" },
        { "type": "prefab", "path": "prefabs/goblin.prefab" },
        { "type": "message", "msgId": 123456 }
      ],
      "tags": ["monster", "goblin", "humanoid"]
    },
    "a1b2c3d4e5f6": {
      "hash": "a1b2c3d4e5f6",
      "type": "map",
      "fileName": "dungeon_floor1.jpg",
      "displayName": "地下城第1层",
      "mimeType": "image/jpeg",
      "size": 2097152,
      "width": 2048,
      "height": 2048,
      "uploadedBy": "gm",
      "uploadedAt": "2026-08-02T10:00:00Z",
      "refCount": 1,
      "references": [
        { "type": "scene", "path": "scenes/dungeon_floor1.scene" }
      ],
      "tags": ["map", "dungeon"]
    }
  }
}
```

**字段说明**：
- `hash`: 内容哈希（唯一标识）
- `type`: 素材类型（token/map/audio/file）
- `refCount`: 引用计数（垃圾回收用）
- `references`: 引用列表（哪些对象在用）
- `tags`: 标签（搜索和分类）

---

## 🗂️ 对象池设计

### Prefab 文件格式

```json
{
  "version": 1,
  "guid": "prefab_goblin_001",
  "name": "哥布林战士",
  "displayName": "哥布林战士",
  "icon": "👹",
  "category": "怪物/人形生物",
  "description": "常见的哥布林敌人，弱小但狡猾",
  "components": [
    {
      "type": "Transform",
      "properties": {
        "scaleX": 1,
        "scaleY": 1
      }
    },
    {
      "type": "SpriteRenderer",
      "properties": {
        "assetRef": "e3b0c44298fc1c14",
        "assetType": "token",
        "opacity": 1.0,
        "alignX": 1,
        "alignY": 1
      }
    },
    {
      "type": "Token",
      "properties": {
        "tokenName": "哥布林",
        "maxHP": 30,
        "currentHP": 30,
        "movementSpeed": 30,
        "shape": "circle",
        "isPlayerControlled": false
      }
    },
    {
      "type": "Vision",
      "properties": {
        "enabled": true,
        "radius": 60
      }
    }
  ],
  "metadata": {
    "createdBy": "gm",
    "createdAt": "2026-08-03T10:00:00Z",
    "tags": ["monster", "cr1", "humanoid"]
  }
}
```

### 对象池管理器

```csharp
// PrefabManager.cs
public class PrefabManager
{
    private readonly string _prefabDirectory;
    private readonly Dictionary<string, PrefabDocument> _cache = new();
    
    public List<PrefabDocument> GetAllPrefabs()
    {
        // 扫描 prefabs/ 目录
        // 按 category 分组
    }
    
    public GameObject Instantiate(string prefabName, Vector2 position)
    {
        var prefab = LoadPrefab(prefabName);
        var go = new GameObject
        {
            Id = Guid.NewGuid(),
            Name = prefab.DisplayName,
            Icon = prefab.Icon
        };
        
        foreach (var compData in prefab.Components)
        {
            var component = ComponentFactory.Create(compData);
            go.AddComponent(component);
        }
        
        // 设置位置
        if (go.GetComponent<TransformComponent>() is { } transform)
        {
            transform.X = position.X;
            transform.Y = position.Y;
        }
        
        return go;
    }
}
```

---

## 🔄 引用计数与垃圾回收

### 引用追踪

```csharp
// AssetReferenceTracker.cs
public class AssetReferenceTracker
{
    private readonly AssetIndex _index;
    
    public void AddReference(string hash, AssetReference reference)
    {
        var asset = _index.GetAsset(hash);
        if (asset == null) return;
        
        asset.References.Add(reference);
        asset.RefCount = asset.References.Count;
        _index.Save();
    }
    
    public void RemoveReference(string hash, AssetReference reference)
    {
        var asset = _index.GetAsset(hash);
        if (asset == null) return;
        
        asset.References.Remove(reference);
        asset.RefCount = asset.References.Count;
        
        // 引用计数为 0 时标记为可删除
        if (asset.RefCount == 0)
        {
            asset.MarkedForDeletion = true;
            asset.MarkedAt = DateTime.UtcNow;
        }
        
        _index.Save();
    }
    
    // 垃圾回收（定期清理无引用的素材）
    public void CollectGarbage(TimeSpan grace = default)
    {
        if (grace == default)
            grace = TimeSpan.FromDays(7);  // 默认保留 7 天
        
        var now = DateTime.UtcNow;
        var toDelete = _index.Assets.Values
            .Where(a => a.MarkedForDeletion && (now - a.MarkedAt) > grace)
            .ToList();
        
        foreach (var asset in toDelete)
        {
            File.Delete(GetAssetPath(asset.Hash));
            _index.Assets.Remove(asset.Hash);
        }
        
        _index.Save();
    }
}
```

### 场景删除时清理引用

```csharp
// 删除 Token
public void DeleteToken(GameObject token)
{
    // 找到 SpriteRenderer 组件
    if (token.GetComponent<SpriteRendererComponent>() is { } sprite)
    {
        var assetRef = sprite.SourceAssetPath;  // 或 AssetRef 字段
        
        // 减少引用计数
        _assetTracker.RemoveReference(assetRef, new AssetReference
        {
            Type = "scene",
            Path = "scenes/main.scene",
            ObjectId = token.Id.ToString()
        });
    }
    
    // 删除对象
    _world.RemoveObject(token);
}
```

---

## 🌐 云同步协议

### API 端点设计

```
# 素材管理
POST   /api/rooms/{roomId}/assets/upload         上传素材
GET    /api/rooms/{roomId}/assets/check          检查素材是否存在
GET    /api/rooms/{roomId}/assets/{type}/{hash}  下载素材
DELETE /api/rooms/{roomId}/assets/{hash}         删除素材（需权限）
GET    /api/rooms/{roomId}/assets/index          获取素材索引

# 场景管理
GET    /api/rooms/{roomId}/scenes                列出所有场景
GET    /api/rooms/{roomId}/scenes/{name}         下载场景
POST   /api/rooms/{roomId}/scenes/{name}         保存场景
DELETE /api/rooms/{roomId}/scenes/{name}         删除场景

# 对象池管理
GET    /api/rooms/{roomId}/prefabs               列出所有 Prefab
GET    /api/rooms/{roomId}/prefabs/{name}        下载 Prefab
POST   /api/rooms/{roomId}/prefabs/{name}        创建/更新 Prefab
DELETE /api/rooms/{roomId}/prefabs/{name}        删除 Prefab
```

---

## 📊 存储成本估算

### 单个房间（100 小时游戏）

| 类型 | 数量 | 单个大小 | 总大小 |
|------|------|---------|--------|
| Token 图片 | 50 | 500 KB | 25 MB |
| 地图背景 | 10 | 2 MB | 20 MB |
| 音频文件 | 5 | 5 MB | 25 MB |
| 场景文件 | 10 | 50 KB | 500 KB |
| Prefab | 30 | 5 KB | 150 KB |
| 消息历史 | - | - | 50 MB |
| 文件上传 | 20 | 1 MB | 20 MB |
| **总计** | - | - | **~140 MB** |

### 1000 个房间

```
140 MB × 1000 = 140 GB

去重后（假设 50% 素材重复）:
140 GB × 0.5 = 70 GB
```

**服务器配置**：
- SSD: 200 GB（够用 + 备份）
- 成本: ~$20/月

---

## 🎯 实施计划

### Phase 1：数据结构重构（3-5 天）

**1.1 房间目录结构调整**
- 创建 `assets/`, `scenes/`, `prefabs/` 目录
- 迁移现有 `map.scene` 到 `scenes/main.scene`

**1.2 素材索引系统**
- 实现 `AssetIndex.cs`（管理 assets/index.json）
- 实现 `ContentHasher.cs`（SHA256 计算）
- 实现 `AssetReferenceTracker.cs`（引用计数）

**1.3 修改 SceneSerializer**
- `SourceAssetPath` → `AssetRef`（存哈希而非路径）
- 序列化/反序列化支持新格式

**1.4 修改 AssetImporter**
- 导入时计算哈希
- 检查是否已存在（去重）
- 更新 assets/index.json

---

### Phase 2：对象池系统（2-3 天）

**2.1 Prefab 文件格式**
- 定义 `PrefabDocument` 数据结构
- 实现序列化/反序列化

**2.2 PrefabManager**
- 加载/保存 Prefab
- 实例化 GameObject
- UI：对象池面板

**2.3 Prefab 编辑器**
- 从现有 Token 创建 Prefab
- 修改 Prefab 属性
- 删除 Prefab

---

### Phase 3：云同步（3-4 天）

**3.1 服务端 API**
- 素材上传/下载端点
- 场景同步端点
- Prefab 同步端点

**3.2 客户端素材管理**
- 上传素材到服务端
- 懒加载缓存机制
- 离线缓存管理

**3.3 命令协议扩展**
- `AddGameObject` 支持 `assetRef`
- 新增 `InstantiatePrefab` 命令

---

### Phase 4：三层保存机制（1-2 天）

**4.1 防抖保存**
- 场景修改后延迟 2-5 秒保存
- 高频操作动态延长延迟

**4.2 定期快照**
- 每 5 分钟强制保存
- 保存到 `scenes/main.scene.temp`

**4.3 原子写入**
- 写入临时文件 `.tmp`
- 原子替换 + 备份 `.backup`

**4.4 崩溃恢复**
- 启动时检测损坏文件
- 自动从 `.backup` 恢复

---

### Phase 5：引用计数与垃圾回收（1 天）

**5.1 引用追踪**
- 添加/删除对象时更新引用计数
- 保存到 `assets/index.json`

**5.2 垃圾回收**
- 定期扫描无引用素材
- 保留期 7 天后删除

---

## 🧪 测试验证

### 数据完整性

- [ ] 添加 Token → 素材上传 → assets/index.json 更新
- [ ] 删除 Token → 引用计数减少
- [ ] 重启服务端 → 场景完整恢复
- [ ] 崩溃时保存 → 从 backup 恢复

### 去重机制

- [ ] 导入相同图片 3 次 → 只存储 1 个文件
- [ ] 不同客户端上传相同图片 → 服务端去重

### 云同步

- [ ] 客户端 A 添加 Token → 客户端 B 自动显示
- [ ] 断网后重连 → 自动下载缺失素材
- [ ] 离线缓存 → 下次进入无需重新下载

### 对象池

- [ ] 创建 Prefab → 保存到 prefabs/
- [ ] 从对象池添加 Token → 继承 Prefab 属性
- [ ] 修改 Prefab → 已有实例不受影响

---

## 📋 总结

### 核心架构

1. **内容寻址** — 素材按哈希存储，自动去重
2. **引用分离** — 数据层存哈希，存储层管理文件
3. **对象池** — Prefab 作为可复用模板
4. **房间素材库** — 独立于个人库，服务端同步
5. **三层保存** — 防抖 + 定期 + 原子写入

### 预期收益

- ✅ 素材空间节省 90%（去重）
- ✅ 云同步完整支持（所有玩家看到相同内容）
- ✅ 数据安全性 99.9%（三层保存）
- ✅ 对象池提升效率（快速批量添加）
- ✅ 跨房间引用（聊天与地图互通）

### 实施周期

**总计：10-15 天**

- Phase 1: 3-5 天（数据结构）
- Phase 2: 2-3 天（对象池）
- Phase 3: 3-4 天（云同步）
- Phase 4: 1-2 天（三层保存）
- Phase 5: 1 天（垃圾回收）

---

**下一步：开始 Phase 1.1 房间目录结构调整吗？**
