# 资产管理架构：语义命名 + 哈希引用

## 设计理念

**存储层**：所有文件使用语义命名（用户可读，便于维护）  
**引用层**：所有引用使用 SHA256 哈希值（内容寻址，稳定可靠）  
**元数据层**：哈希 ↔ 文件路径映射（.metadata/asset-index.json）

---

## 文件系统结构

### 本地库（个人资产）
```
AssetLibrary/
  ├─ tokens/
  │   ├─ 战士.png              (语义命名)
  │   └─ 怪物/
  │       └─ 哥布林.png
  ├─ maps/
  │   └─ 森林地块.jpg
  ├─ tiles/
  │   └─ 墙壁-01.png
  └─ .metadata/
      └─ asset-index.json      (哈希→路径映射)
```

### 房间库（共享资产）
```
room-abc/AssetLibrary/
  ├─ tokens/
  │   ├─ 战士.png              (从本地复制，保留语义名)
  │   └─ 哥布林.png
  ├─ maps/
  │   └─ 森林地块.jpg
  └─ .metadata/
      └─ asset-index.json
```

---

## 元数据索引结构

**asset-index.json**:
```json
{
  "version": "1.0",
  "assets": {
    "sha256:abc123def456...": {
      "hash": "abc123def456...",
      "algorithm": "sha256",
      "relativePath": "tokens/战士.png",
      "fileName": "战士.png",
      "mimeType": "image/png",
      "size": 102400,
      "width": 512,
      "height": 512,
      "importedAt": "2026-08-03T10:00:00Z"
    }
  }
}
```

---

## 工作流程

### 1. 导入到本地库

```
用户拖入: 战士.png
  ↓
1. 计算 SHA256 哈希: abc123def456...
2. 检查本地库索引是否已有此哈希
   - 存在 → 提示用户"内容重复"，询问是否继续
   - 不存在 → 继续
3. 保存文件: AssetLibrary/tokens/战士.png (保留语义命名)
4. 更新 asset-index.json:
   {
     "sha256:abc123def": {
       "relativePath": "tokens/战士.png",
       "fileName": "战士.png",
       ...
     }
   }
5. 创建 .asset 对象（如果需要）:
   {
     "name": "战士Token",
     "components": [{
       "type": "SpriteRenderer",
       "properties": {
         "sprite": "sha256:abc123def456..."  // 哈希引用
       }
     }]
   }
```

### 2. 拖入地图（静默转换为房间资产）

```
用户从本地库拖 "战士Token" 到地图
  ↓
1. 读取 .asset: sprite = "sha256:abc123def"
2. 检查房间库索引是否已有此哈希
   - 存在 → 跳过复制（静默去重）
   - 不存在 → 执行复制流程
3. 复制流程:
   a. 从本地库索引查找: sha256:abc123def → "tokens/战士.png"
   b. 读取文件内容
   c. 复制到房间库: room-abc/AssetLibrary/tokens/战士.png
   d. 更新房间库 asset-index.json
4. 地图引用: sprite = "sha256:abc123def"
```

### 3. 渲染查找（房间优先）

```
渲染需要: sprite = "sha256:abc123def"
  ↓
1. 查房间库 asset-index.json:
   sha256:abc123def → "tokens/战士.png"
   → 完整路径: room-abc/AssetLibrary/tokens/战士.png
2. 文件存在 → 返回路径
3. 文件不存在 → 降级到本地库:
   查本地库 asset-index.json → AssetLibrary/tokens/战士.png
4. 仍不存在 → 后备搜索（兼容旧版哈希命名文件）
```

---

## 实现状态

### ✅ Phase 1: 数据结构（已完成）
- [x] AssetMetadata 数据类
- [x] AssetMetadataStore 索引管理器
- [x] AssetIndex JSON 序列化

### ✅ Phase 2: AssetImporter 改造（已完成）
- [x] 计算 SHA256 哈希
- [x] 语义命名存储
- [x] 更新元数据索引
- [x] 去重检测逻辑（基础）

### ✅ Phase 3: MapSpriteAssetResolver 改造（已完成）
- [x] 房间优先查找
- [x] 本地库降级
- [x] 哈希引用解析
- [x] 后备递归搜索（兼容旧数据）

### ⏳ Phase 4: 集成到素材库 UI（待实施）
- [ ] 导入时显示去重提示
- [ ] 拖入地图时静默转换为房间资产
- [ ] 素材库面板调用新版 AssetImporter

### ⏳ Phase 5: 重命名/移动支持（待实施）
- [ ] 文件重命名时更新索引
- [ ] 文件移动时更新 relativePath
- [ ] 文件夹移动时批量更新

### ⏳ Phase 6: 测试验证（待实施）
- [ ] 导入图片 → 生成哈希索引
- [ ] 重复导入 → 显示去重提示
- [ ] 拖入地图 → 转换为房间资产
- [ ] 保存场景 → 哈希引用正确
- [ ] 加载场景 → 图片正确显示
- [ ] 重命名文件 → 引用不断

---

## 核心类

### AssetMetadata
```csharp
public class AssetMetadata
{
    public string Hash { get; set; }              // sha256:abc123...
    public string Algorithm { get; set; }          // "sha256"
    public string RelativePath { get; set; }       // "tokens/战士.png"
    public string FileName { get; set; }           // "战士.png"
    public string MimeType { get; set; }           // "image/png"
    public long Size { get; set; }                 // 文件大小（字节）
    public int? Width { get; set; }                // 图片宽度
    public int? Height { get; set; }               // 图片高度
    public DateTime ImportedAt { get; set; }       // 导入时间
}
```

### AssetMetadataStore
```csharp
public class AssetMetadataStore
{
    // 根据哈希查找文件路径
    public string? GetPathByHash(string hash);
    
    // 根据路径查找哈希
    public string? GetHashByPath(string relativePath);
    
    // 检查哈希是否存在（去重）
    public bool ContainsHash(string hash);
    
    // 注册新资产
    public void Register(string hash, AssetMetadata metadata);
    
    // 更新路径（重命名/移动）
    public void UpdatePath(string hash, string newRelativePath);
    
    // 删除条目
    public void Remove(string hash);
    
    // 保存索引
    public void Save();
}
```

### AssetImporter
```csharp
public class AssetImporter
{
    // 导入到本地库（带去重检测）
    public ImportResult ImportToLocal(
        string sourceFile, 
        string targetRelativePath);
    
    // 转换为房间资产（静默去重）
    public void ConvertToRoomAsset(string hash);
}
```

### MapSpriteAssetResolver
```csharp
public static class MapSpriteAssetResolver
{
    // 初始化元数据索引
    public static void Initialize();
    
    // 按哈希解析图片路径（房间优先）
    public static string? ResolveSpritePath(string? assetRef);
}
```

---

## 优势

| 特性 | 实现效果 |
|------|---------|
| ✅ 语义命名 | 所有文件都是 `战士.png`、`森林地块.jpg`，用户可读 |
| ✅ 文件夹层级 | 支持 `tokens/玩家角色/战士.png`，灵活组织 |
| ✅ 内容去重 | 相同内容只存一份（同一库内） |
| ✅ 引用稳定 | 重命名/移动文件不影响地图引用 |
| ✅ 房间优先 | 拖入地图时自动转换为房间资产 |
| ✅ 跨库共享 | 哈希引用在两个库通用 |
| ✅ 向后兼容 | 后备搜索支持旧版哈希命名文件 |

---

## 后续扩展（权限组）

**未来版本**可在房间库中添加权限分层：
```
room-abc/AssetLibrary/
  ├─ gm-only/        (GM 专属)
  │   └─ .metadata/
  ├─ shared/         (全员可见)
  │   └─ .metadata/
  └─ player-uploads/ (玩家上传)
      └─ .metadata/
```

每个权限组维护独立的 asset-index.json，跨组允许内容重复（权限隔离）。

---

## 修改的文件

1. **MapEngine.Core/World/AssetMetadata.cs** (新建) — 元数据数据类
2. **MapEngine.Core/World/AssetMetadataStore.cs** (新建) — 索引管理器
3. **MapEngine.Core/World/AssetImporter.cs** (改造) — 支持语义命名和哈希索引
4. **MapEngine.Avalonia/Services/MapSpriteAssetResolver.cs** (改造) — 房间优先查找
5. **MapEngine.Avalonia/Services/AssetLibraryFileSystemService.cs** (待改造) — UI 集成

---

## 测试清单

### 基础功能
- [ ] 导入图片到本地库 → 生成语义命名文件 + 哈希索引
- [ ] 导入相同内容 → 提示用户去重
- [ ] 拖入地图 → 图片正常显示

### 房间库功能
- [ ] 拖入地图 → 静默复制到房间库
- [ ] 相同图片拖入 → 房间库不重复存储
- [ ] 房间库文件 → 地图引用正确

### 引用稳定性
- [ ] 重命名本地文件 → 已导入的地图引用不断
- [ ] 移动文件到子文件夹 → 引用不断
- [ ] 删除本地文件 → 房间库中的副本仍可用

### 向后兼容
- [ ] 旧版哈希命名文件 → 仍能正确加载
- [ ] 新旧混合场景 → 正常渲染
