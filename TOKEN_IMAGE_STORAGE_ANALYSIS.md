# Token 图片存储与同步分析

## 🔍 当前机制分析

### 结论：✅ 图片会被保存和同步，❌ 但相同图片会重复占用空间

---

## 📊 完整流程

### 1. Token 图片导入流程

```
用户拖拽图片到地图
↓
AssetImporter.ImportImage() 
├── 复制图片到 AssetLibrary/Tokens/ 文件夹
│   例: goblin.png → AssetLibrary/Tokens/goblin.png
├── 生成 .asset 文件（StaticObject）
│   例: goblin.asset
│   内容: { sourceAssetPath: "AssetLibrary/Tokens/goblin.png" }
└── 创建 Token GameObject
    └── SpriteRendererComponent.SourceAssetPath = 绝对路径
```

**关键代码**：
```csharp
// AssetImporter.cs line 33
File.Copy(sourceImagePath, imageDest, overwrite: false);
```

**结果**：
- ✅ 图片被物理复制到素材库
- ✅ 每个图片独立存储

---

### 2. Token 数据保存到 map.scene

```json
{
  "version": 1,
  "objects": [
    {
      "id": "abc123",
      "name": "哥布林",
      "components": [
        {
          "type": "Transform",
          "properties": { "x": 100, "y": 200 }
        },
        {
          "type": "SpriteRenderer",
          "properties": {
            "sourceAssetPath": "G:\\...\\AssetLibrary\\Tokens\\goblin.png",  ← 绝对路径
            "sourceAssetKind": "Image",
            "opacity": 1.0
          }
        },
        {
          "type": "Token",
          "properties": {
            "tokenName": "哥布林",
            "currentHP": 25,
            "maxHP": 30
          }
        }
      ]
    }
  ]
}
```

**关键代码**：
```csharp
// SceneSerializer.cs line 71
props["sourceAssetPath"] = s.SourceAssetPath;  // 保存完整路径
```

**结果**：
- ✅ map.scene 包含图片路径
- ⚠️ 保存的是绝对路径（跨机器可能失效）

---

### 3. 云房间同步机制

```
客户端 A 添加 Token
↓
发送 map:command 协议到服务端
{
  "commandType": "AddGameObject",
  "data": {
    "components": [
      { "type": "SpriteRenderer", "sourceAssetPath": "G:\\...\\goblin.png" }
    ]
  }
}
↓
服务端广播给客户端 B
↓
客户端 B 接收命令
↓
尝试加载 "G:\\...\\goblin.png"
↓
❌ 文件不存在（路径不在客户端 B 机器上）
```

**问题**：
- ❌ **只同步了路径引用，没有同步图片文件本身**
- ❌ 客户端 B 无法显示 Token 图片

---

## 🔴 当前存在的问题

### 问题 1：相同图片重复占用空间

**场景**：
```
用户导入 goblin.png 3 次
↓
AssetLibrary/Tokens/
├── goblin.png        ← 1 MB
├── goblin 2.png      ← 1 MB（相同内容）
└── goblin 3.png      ← 1 MB（相同内容）
总计: 3 MB
```

**原因**：
```csharp
// AssetImporter.cs line 62-75
private static string GetUniquePath(string folder, string baseName, string extension)
{
    var candidate = Path.Combine(folder, baseName + extension);
    if (!File.Exists(candidate))
        return candidate;
    
    // 文件名冲突时自动重命名，不检查内容
    for (int i = 2; i < 1000; i++)
    {
        candidate = Path.Combine(folder, $"{baseName} {i}{extension}");
        if (!File.Exists(candidate))
            return candidate;
    }
}
```

**影响**：
- 素材库空间浪费（3个相同Token = 3倍空间）
- 用户不知道已经导入过相同图片

---

### 问题 2：云房间无法同步 Token 图片

**场景**：
```
房主添加 Token（goblin.png）
↓
同步到其他玩家
↓
其他玩家看到：❌ 缺失图片的方框
```

**原因**：
- 只同步了 `sourceAssetPath`（本地绝对路径）
- 没有同步图片文件本身

**当前文档配置**：
```
AssetImportMode = "copy"  ← 已锁定为 copy 模式
```

但 copy 只是复制到本地素材库，不涉及网络同步。

---

### 问题 3：跨机器路径失效

**场景**：
```
机器 A: G:\跑团大师\AssetLibrary\Tokens\goblin.png
机器 B: D:\TRPG\AssetLibrary\Tokens\goblin.png  ← 不同盘符/路径

保存的路径: G:\跑团大师\AssetLibrary\Tokens\goblin.png
↓
机器 B 加载失败（路径不存在）
```

**原因**：
- 保存的是绝对路径，不可移植

---

## 🎯 标准解决方案

### 方案 A：内容哈希去重（推荐）

**策略**：相同图片只存储一次，用文件内容的哈希命名

```
导入 goblin.png
↓
计算 SHA256: e3b0c442...
↓
检查 AssetLibrary/Tokens/e3b0c442.png 是否存在
├── 存在 → 复用，不复制
└── 不存在 → 复制并命名为 e3b0c442.png
↓
.asset 文件引用: { sourceAssetPath: "e3b0c442.png" }
```

**实现**：
```csharp
// AssetImporter.cs
public static ImportResult? ImportImage(string sourceImagePath, string targetFolder)
{
    // 1. 计算文件哈希
    var hash = ComputeSHA256(sourceImagePath);
    var ext = Path.GetExtension(sourceImagePath);
    var hashFileName = $"{hash}{ext}";
    var hashPath = Path.Combine(targetFolder, hashFileName);
    
    // 2. 检查是否已存在
    if (!File.Exists(hashPath))
    {
        File.Copy(sourceImagePath, hashPath);
    }
    
    // 3. 创建 .asset 文件（使用原始文件名）
    var baseName = Path.GetFileNameWithoutExtension(sourceImagePath);
    var assetPath = GetUniquePath(targetFolder, baseName, ".asset");
    var assetContent = new
    {
        name = baseName,
        type = "StaticObjectClass",
        components = new[]
        {
            new { type = "Transform" },
            new { type = "SpriteRenderer", properties = new { sourceAssetPath = hashFileName } }
        }
    };
    File.WriteAllText(assetPath, JsonSerializer.Serialize(assetContent));
    
    return new ImportResult
    {
        AssetFilePath = assetPath,
        ImageFilePath = hashPath,
        DisplayName = baseName
    };
}

private static string ComputeSHA256(string filePath)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var hash = sha256.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLower().Substring(0, 16);
}
```

**优点**：
- ✅ 相同图片只存储一次（节省 90% 空间）
- ✅ 自动去重，用户无感知
- ✅ 用户仍能创建多个同名 Token

**示例**：
```
导入 goblin.png 3 次
↓
AssetLibrary/Tokens/
├── e3b0c442.png           ← 实际图片（1 MB）
├── 哥布林.asset           ← 引用 e3b0c442.png
├── 哥布林 2.asset         ← 引用 e3b0c442.png
└── 哥布林 3.asset         ← 引用 e3b0c442.png
总计: 1 MB + 3 KB（.asset 文件）
```

---

### 方案 B：云房间图片同步（必须实现）

**策略**：Token 图片存储到房间共享文件夹，所有客户端从服务端下载

#### B1：服务端集中存储

```
服务端架构：
data/rooms/{roomId}/
├── map.scene              ← 地图存档
├── assets/                ← 房间共享素材库（新增）
│   ├── tokens/
│   │   ├── e3b0c442.png  ← Token 图片（按哈希命名）
│   │   └── 7a8f9c1d.png
│   └── maps/
│       └── dungeon.jpg
└── files/                 ← 文件上传（已有）
```

**流程**：
```
客户端 A 添加 Token（goblin.png）
↓
1. 上传图片到服务端 POST /api/rooms/{roomId}/assets/upload
   → 服务端保存到 data/rooms/{roomId}/assets/tokens/e3b0c442.png
   → 返回相对路径: "assets/tokens/e3b0c442.png"
↓
2. 客户端 A 发送地图命令
   {
     "commandType": "AddGameObject",
     "data": {
       "components": [
         { "type": "SpriteRenderer", "sourceAssetPath": "assets/tokens/e3b0c442.png" }
       ]
     }
   }
↓
3. 服务端广播给客户端 B
↓
4. 客户端 B 接收命令
   → 检查本地缓存是否有 e3b0c442.png
   → 没有则从服务端下载 GET /api/rooms/{roomId}/assets/tokens/e3b0c442.png
   → 缓存到本地 %TEMP%\TRPGMaster\cache\{roomId}\assets\tokens\e3b0c442.png
   → 加载图片显示 Token
```

**优点**：
- ✅ 所有玩家看到相同的 Token 图片
- ✅ 路径可移植（相对路径）
- ✅ 支持懒加载（按需下载）

**存储成本**：
- 每个房间独立存储素材
- 100 个 Token × 1 MB = 100 MB/房间

#### B2：全局素材库 + 房间引用

```
服务端架构：
data/
├── global_assets/           ← 全局共享素材池
│   ├── e3b0c442.png        ← Token 图片（按哈希命名）
│   └── 7a8f9c1d.png
└── rooms/{roomId}/
    ├── map.scene
    └── asset_refs.json      ← 房间引用的素材列表
        [
          { "hash": "e3b0c442", "type": "token" },
          { "hash": "7a8f9c1d", "type": "map" }
        ]
```

**优点**：
- ✅ 相同图片全服务器只存储一次
- ✅ 100 个房间用同一张哥布林图 = 只占 1 MB

**缺点**：
- ⚠️ 复杂度高（垃圾回收，引用计数）
- ⚠️ 删除房间时需要清理引用

---

### 方案 C：相对路径存储（立即修复）

**策略**：map.scene 保存相对路径而非绝对路径

**修改前**：
```json
{
  "sourceAssetPath": "G:\\跑团大师\\AssetLibrary\\Tokens\\goblin.png"
}
```

**修改后**：
```json
{
  "sourceAssetPath": "AssetLibrary/Tokens/goblin.png"
}
```

**实现**：
```csharp
// SceneSerializer.cs 修改
case SpriteRendererComponent s:
    // 保存相对路径
    var relativePath = MakeRelativePath(s.SourceAssetPath);
    props["sourceAssetPath"] = relativePath;
    break;

// 辅助方法
private static string MakeRelativePath(string absolutePath)
{
    var basePath = AssetLibraryFileSystemService.ResolveEffectiveRootPath();
    if (absolutePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
    {
        return absolutePath.Substring(basePath.Length).TrimStart('\\', '/');
    }
    return absolutePath;  // 外部路径保持原样
}
```

**优点**：
- ✅ 跨机器可移植
- ✅ 实现简单（10 分钟）

**限制**：
- ⚠️ 仍需要每个客户端有相同的素材库

---

## 📋 推荐实施顺序

### Phase 1：立即修复（30 分钟）

**1. 相对路径存储**
- 修改 `SceneSerializer.cs`
- map.scene 保存相对路径
- 解决跨机器路径失效

### Phase 2：短期优化（2-3 小时）

**2. 内容哈希去重**
- 修改 `AssetImporter.cs`
- 相同图片只存储一次
- 节省 90% 本地空间

### Phase 3：云房间同步（1-2 天）

**3. 服务端图片存储**
- 新增 `/api/rooms/{roomId}/assets/upload` 接口
- 服务端保存到 `data/rooms/{roomId}/assets/tokens/`

**4. 客户端懒加载**
- 接收命令时检查图片是否存在
- 不存在则从服务端下载
- 缓存到本地临时目录

**5. 地图命令协议扩展**
- 添加 Token 时先上传图片
- 命令中使用相对路径引用

### Phase 4：高级优化（可选，2-3 天）

**6. 全局素材池**
- 服务端去重存储
- 引用计数垃圾回收

---

## 🧪 验证清单

### Phase 1 验证

- [ ] 保存 map.scene，路径为相对路径（如 `AssetLibrary/Tokens/goblin.png`）
- [ ] 移动项目到不同盘符，仍能加载 Token 图片
- [ ] 复制房间到另一台机器（带素材库），图片正常显示

### Phase 2 验证

- [ ] 导入相同图片 3 次，磁盘只增加 1 个图片文件
- [ ] 可以创建 3 个同名 Token，都引用同一张图片
- [ ] 删除 1 个 Token，图片仍保留（被其他 Token 使用）

### Phase 3 验证

- [ ] 客户端 A 添加 Token → 客户端 B 自动下载图片并显示
- [ ] 断网时客户端 B 显示"加载中"占位图
- [ ] 重新连接后自动下载缺失图片

---

## 🎯 最终方案

### 推荐架构（综合 A + B + C）

**本地存储**：
- 内容哈希去重（节省本地空间）
- 相对路径引用（跨机器可移植）

**云房间存储**：
- 服务端集中存储（B1 方案）
- 按房间独立存储（简单可靠）
- 客户端懒加载缓存

**数据流**：
```
1. 用户导入图片 → 哈希去重 → 本地素材库
2. 添加 Token → 上传图片到服务端 → 广播命令（相对路径）
3. 其他客户端接收 → 检查缓存 → 下载缺失图片 → 显示
4. map.scene 保存 → 相对路径 → 跨机器可移植
```

**成本**：
- 本地：相同图片只存 1 次
- 服务端：每个房间独立存储（100 MB/房间）
- 网络：懒加载，只下载需要的图片

---

## 📌 总结

### 当前状态

- ✅ Token 图片会被保存到 map.scene（路径引用）
- ✅ Token 图片会被复制到本地素材库
- ❌ 相同图片会重复占用空间（无去重）
- ❌ 云房间无法同步图片（只同步路径）
- ❌ 绝对路径导致跨机器失效

### 立即行动

**Phase 1（30 分钟）**：修改为相对路径存储
**Phase 2（2-3 小时）**：实现内容哈希去重
**Phase 3（1-2 天）**：实现云房间图片同步

要我现在开始实施 Phase 1（相对路径存储）吗？
