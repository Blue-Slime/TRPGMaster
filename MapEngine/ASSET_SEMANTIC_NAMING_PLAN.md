# 语义命名 + 哈希引用混合架构实施计划

## 当前状态分析

### 现有实现
- **AssetImporter.ImportImage()** — 导入图片到素材库，生成哈希命名的图片 + 语义命名的 .asset 对象
- **MapSpriteAssetResolver** — 根据哈希引用查找图片文件
- **素材库拖放** — `MapEditorView.AssetLibrary_Drop()` 调用 AssetImporter 导入
- **拖入地图** — `MapDropTarget_Drop()` → `CommitMapDragPreview()` → 读取 .asset 中的 assetRef（哈希）

### 存在的问题
1. ❌ 图片使用哈希命名（`abc123def.png`），用户不可读
2. ❌ 无元数据索引（.metadata/asset-index.json）
3. ❌ 无层级结构（tokens/maps/tiles）
4. ❌ 无本地库 vs 房间库区分
5. ❌ 拖入地图时没有"转换为房间资产"的逻辑

---

## 目标架构

### 文件系统结构

```
本地库:
  AssetLibrary/
    ├─ tokens/
    │   ├─ 战士.png              (语义命名)
    │   └─ 怪物/
    │       └─ 哥布林.png
    ├─ maps/
    │   └─ 森林地块.jpg
    └─ .metadata/
        └─ asset-index.json      (哈希→路径映射)

房间库:
  Rooms/room-abc/AssetLibrary/
    ├─ tokens/
    │   └─ 战士.png              (从本地库复制)
    ├─ maps/
    │   └─ 森林地块.jpg
    └─ .metadata/
        └─ asset-index.json
```

### .asset 引用格式

```json
{
  "name": "战士Token",
  "type": "StaticObjectClass",
  "components": [
    {
      "type": "SpriteRenderer",
      "properties": {
        "assetRef": "sha256:abc123def456...",  // 哈希引用
        "sourceAssetKind": "Image"
      }
    }
  ]
}
```

### asset-index.json 格式

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
      "importedAt": "2026-08-03T10:00:00Z",
      "tags": []
    }
  }
}
```

---

## 实施步骤

### Phase 1: 核心数据结构（已完成）
- [x] **AssetMetadata.cs** — 元数据和索引结构
- [x] **AssetMetadataStore.cs** — 元数据存储和查询

### Phase 2: 改造 AssetImporter
- [ ] 支持层级结构导入（tokens/maps/tiles）
- [ ] 图片保留语义命名（不再哈希命名）
- [ ] 计算哈希并写入 metadata
- [ ] 去重检测（哈希已存在时提示用户）
- [ ] 返回哈希引用（`sha256:abc123...`）

### Phase 3: 改造 MapSpriteAssetResolver
- [ ] 支持哈希引用解析（`sha256:abc123...` → 文件路径）
- [ ] 房间库优先查找
- [ ] 降级到本地库
- [ ] 集成 AssetMetadataStore

### Phase 4: 房间资产转换逻辑
- [ ] 拖入地图时触发转换
- [ ] 检查房间库是否已有此哈希（去重）
- [ ] 复制文件到房间库（保留层级结构）
- [ ] 更新房间库 metadata

### Phase 5: UI 集成
- [ ] 导入时的去重提示对话框
- [ ] 拖入地图时的"已同步到房间库"提示
- [ ] 素材库支持层级结构显示

### Phase 6: 迁移现有数据
- [ ] 扫描现有哈希命名的图片
- [ ] 重命名为语义名称
- [ ] 构建 metadata 索引
- [ ] 更新 .asset 文件中的引用

---

## 关键改动点

### 1. AssetImporter.ImportImage() 签名变化

**旧版**:
```csharp
public static ImportResult? ImportImage(string sourceImagePath, string targetFolder, AssetDatabase? assetDb = null)
{
    var hash = ContentHasher.ComputeHash(sourceImagePath);
    var hashFileName = $"{hash}{ext}";  // 哈希命名
    var hashImagePath = Path.Combine(targetFolder, hashFileName);
    ...
}
```

**新版**:
```csharp
public static ImportResult? ImportImage(
    string sourceImagePath, 
    string targetFolder,
    string? targetSubfolder = null,  // 新增：子文件夹（tokens/maps）
    AssetMetadataStore? metadataStore = null,  // 新增：元数据存储
    AssetDatabase? assetDb = null)
{
    var hash = ContentHasher.ComputeHash(sourceImagePath);
    
    // 去重检测
    if (metadataStore?.ContainsHash(hash) == true)
    {
        return new ImportResult { Status = ImportStatus.DuplicateContent, Hash = hash };
    }
    
    // 语义命名
    var originalName = Path.GetFileName(sourceImagePath);
    var targetPath = Path.Combine(targetFolder, targetSubfolder ?? "", originalName);
    var uniquePath = GetUniquePath(targetPath);  // 文件名冲突时自动编号
    
    File.Copy(sourceImagePath, uniquePath);
    
    // 写入元数据
    metadataStore?.Register(hash, new AssetMetadata { ... });
    
    return new ImportResult { Hash = $"sha256:{hash}", ... };
}
```

### 2. MapSpriteAssetResolver 改造

**旧版**:
```csharp
public static string? ResolveSpritePath(string assetRef)
{
    // 直接查找哈希命名的文件
    var path = Path.Combine(AssetLibraryRoot, $"{assetRef}.png");
    return File.Exists(path) ? path : null;
}
```

**新版**:
```csharp
public class MapSpriteAssetResolver
{
    private readonly AssetMetadataStore? _roomMetadata;
    private readonly AssetMetadataStore _localMetadata;
    private readonly string? _roomRoot;
    private readonly string _localRoot;
    
    public string? ResolveSpritePath(string assetRef)
    {
        var hash = NormalizeHash(assetRef);  // "sha256:abc" → "abc"
        
        // 1. 优先查房间库
        if (_roomMetadata != null)
        {
            var roomPath = _roomMetadata.GetPathByHash(hash);
            if (roomPath != null)
            {
                var fullPath = Path.Combine(_roomRoot!, roomPath);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }
        
        // 2. 降级到本地库
        var localPath = _localMetadata.GetPathByHash(hash);
        if (localPath != null)
        {
            var fullPath = Path.Combine(_localRoot, localPath);
            if (File.Exists(fullPath))
                return fullPath;
        }
        
        return null;
    }
}
```

### 3. 房间资产转换（新增）

```csharp
// 在 MainWindowViewModel 或专门的服务中
public void ConvertToRoomAsset(string hash)
{
    if (_roomAssetMetadata == null || _roomAssetLibraryRoot == null)
        return;  // 单机模式，无房间库
    
    // 检查房间库是否已有
    if (_roomAssetMetadata.ContainsHash(hash))
        return;  // 已存在，跳过（静默去重）
    
    // 从本地库复制到房间库
    var localPath = _localAssetMetadata.GetPathByHash(hash);
    if (localPath == null)
        throw new FileNotFoundException($"Hash not found: {hash}");
    
    var sourceFile = Path.Combine(_localAssetLibraryRoot, localPath);
    var targetFile = Path.Combine(_roomAssetLibraryRoot, localPath);  // 保持相同层级
    
    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
    File.Copy(sourceFile, targetFile, overwrite: false);
    
    // 注册到房间库 metadata
    var metadata = _localAssetMetadata.GetMetadata(hash);
    if (metadata != null)
    {
        _roomAssetMetadata.Register(hash, metadata);
    }
}
```

---

## 待解决问题

### Q1: 本地库和房间库的根目录从哪里获取？

**答**: 
- 本地库：`AssetLibraryFileSystemService.ResolveEffectiveRootPath()`
- 房间库：当前项目在单机模式，暂时没有房间概念，先预留接口

### Q2: 何时触发"转换为房间资产"？

**答**: 在 `CommitMapDragPreview()` 中：
```csharp
// MainWindowViewModel.CommitMapDragPreview()
var item = _mapDragPreview;
if (item.AssetRef != null)
{
    ConvertToRoomAsset(item.AssetRef);  // 新增：转换为房间资产
}
_hierarchyRoot.Add(item);
```

### Q3: 层级结构的子文件夹从哪里来？

**答**: 根据文件类型自动分类：
- 图片 → `tokens/` 或 `maps/`（可配置，默认 `tokens/`）
- 音频 → `audio/`
- 其他 → 根目录

或者：UI 上添加"导入到..."下拉选择器

---

## 下一步

开始 Phase 2: 改造 AssetImporter

