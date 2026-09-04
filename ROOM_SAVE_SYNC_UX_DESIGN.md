# 云房间保存与同步体验设计

## 🎯 设计目标

从完整房间体验出发，平衡以下四个维度：

| 维度 | 目标 | 冲突点 |
|------|------|--------|
| **数据安全** | 零丢失，崩溃可恢复 | ⚔️ 性能开销 |
| **实时性** | 操作即时同步到其他玩家 | ⚔️ 网络延迟 |
| **性能** | 流畅不卡顿，低磁盘 I/O | ⚔️ 保存频率 |
| **用户感知** | 自动化，无需关心保存 | ⚔️ 保存状态透明度 |

---

## 📊 用户场景分析

### 场景 1：战斗模式（高频操作）

**特征**：
- 5-10 个玩家同时在线
- 频繁移动 Token（每秒 10-20 次操作）
- 持续时间：30-60 分钟

**需求**：
- ✅ 实时同步（其他玩家立即看到）
- ✅ 操作流畅不卡顿
- ⚠️ 数据安全次要（短期内不会崩溃）

**保存策略**：
```
实时广播：立即（0ms 延迟）
磁盘保存：防抖延迟 3 秒
定期快照：每 5 分钟强制保存一次
```

---

### 场景 2：探索模式（中频操作）

**特征**：
- 2-5 个玩家在线
- 偶尔移动 Token，添加标记
- 持续时间：1-2 小时

**需求**：
- ✅ 实时同步
- ✅ 数据安全（防止长时间未保存）
- ⚠️ 性能不敏感

**保存策略**：
```
实时广播：立即（0ms 延迟）
磁盘保存：防抖延迟 5 秒
定期快照：每 10 分钟强制保存一次
```

---

### 场景 3：地图编辑模式（超高频操作）

**特征**：
- GM 单人编辑
- 疯狂绘制形状、添加对象（每秒 30+ 次操作）
- 持续时间：10-30 分钟

**需求**：
- ⚠️ 无需实时广播（单人操作）
- ✅ 绝对流畅（不能卡顿）
- ✅ 数据安全（编辑成果不能丢）

**保存策略**：
```
实时广播：无（单人模式）
磁盘保存：防抖延迟 10 秒
定期快照：每 3 分钟强制保存一次
停止编辑：2 秒后立即保存
```

---

### 场景 4：旁观模式（只读）

**特征**：
- 玩家只看不动
- 接收其他玩家的操作

**需求**：
- ✅ 实时接收更新
- ✅ 不产生保存开销

**保存策略**：
```
实时广播：接收（0ms 延迟）
磁盘保存：无操作，不触发
```

---

## 🏗️ 三层保存架构

### 第 1 层：内存层（实时同步）

**职责**：玩家操作的实时广播

```
玩家 A 操作 → 内存 World 更新 → 立即广播 Delta 
→ 玩家 B/C/D 实时收到 → 更新本地视图
```

**延迟**：< 50ms（网络往返）  
**频率**：无限制（每次操作都广播）  
**数据丢失风险**：服务端崩溃丢失未保存数据

---

### 第 2 层：防抖保存层（智能延迟）

**职责**：将高频操作合并为低频磁盘写入

```
操作流：op1 → op2 → op3 → ... → op10 （2秒内）
保存：   [等待] [等待] [等待] ... [最后一次操作后 2 秒] → 保存
```

**延迟**：操作停止后 2-5 秒  
**频率**：动态调整（高频操作时自动延长）  
**数据丢失风险**：最多丢失最后 5 秒数据

**实现**：
```csharp
private Timer? _saveTimer;
private int _pendingOperations = 0;

commandBus.CommandExecuted += (sender, eventArgs) =>
{
    if (eventArgs.Action == CommandAction.Execute)
    {
        _pendingOperations++;
        _saveTimer?.Dispose();
        
        // 动态延迟：操作越频繁，延迟越长
        int delay = _pendingOperations > 20 ? 5000 : 2000;
        
        _saveTimer = new Timer(_ =>
        {
            MapScenePersistence.SaveScene(rid, world);
            _pendingOperations = 0;
        }, null, delay, Timeout.Infinite);
    }
};
```

---

### 第 3 层：定期快照层（兜底保护）

**职责**：防止长时间未保存导致数据丢失

```
定时器每 5 分钟触发 → 检查是否有未保存的修改 → 强制保存
```

**延迟**：最多 5 分钟  
**频率**：固定周期（5-10 分钟）  
**数据丢失风险**：极低（除非服务器硬件故障）

**实现**：
```csharp
private DateTime _lastSaveTime = DateTime.UtcNow;
private bool _isDirty = false;

// 定时快照（每 5 分钟）
var snapshotTimer = new Timer(_ =>
{
    if (_isDirty && (DateTime.UtcNow - _lastSaveTime).TotalMinutes >= 5)
    {
        MapScenePersistence.SaveScene(rid, world);
        _lastSaveTime = DateTime.UtcNow;
        _isDirty = false;
    }
}, null, 300000, 300000);

commandBus.CommandExecuted += (sender, eventArgs) =>
{
    _isDirty = true;
};
```

---

## 🎨 用户感知设计

### 方案 A：完全静默（推荐）

**理念**：用户无需关心保存，系统自动处理

**UI 表现**：
- ✅ 无保存按钮
- ✅ 无保存提示
- ✅ 无保存状态指示器

**优点**：
- 符合现代应用体验（Google Docs / Figma）
- 用户零心智负担

**缺点**：
- 用户不知道何时保存成功
- 崩溃时不知道丢失多少数据

---

### 方案 B：轻量提示

**理念**：关键操作后显示轻量提示

**UI 表现**：
```
[地图编辑器右下角]
┌─────────────────┐
│ ✓ 已自动保存    │  ← 2 秒后淡出
└─────────────────┘
```

**触发时机**：
- 添加/删除对象后 → 显示"已保存"
- 长时间编辑后 → 显示"已自动保存（5 分钟前）"

**优点**：
- 增强用户安全感
- 提示不干扰操作

**缺点**：
- 增加视觉噪音

---

### 方案 C：保存指示器（传统方案，不推荐）

**UI 表现**：
```
[标题栏]
地图编辑器 - 哥布林洞窟 [未保存 *]
```

**缺点**：
- ❌ 与自动保存理念冲突
- ❌ 用户会担心"忘记保存"
- ❌ 增加心智负担

---

## 🛡️ 数据安全保障

### 1. 崩溃恢复机制

**策略**：保存临时快照 + 主存档

```
data/rooms/{roomId}/
├── map.scene           ← 主存档（防抖保存）
├── map.scene.backup    ← 上一次保存的备份
└── map.scene.temp      ← 临时快照（每次定期快照）
```

**恢复流程**：
```
启动时检查：
1. map.scene 存在且有效 → 加载
2. map.scene 损坏 → 尝试 map.scene.backup
3. backup 也损坏 → 尝试 map.scene.temp
4. 全部失败 → 使用空白场景，记录错误日志
```

---

### 2. 保存原子性

**问题**：保存过程中崩溃导致文件损坏

**解决方案**：写入临时文件 → 原子替换

```csharp
public static void SaveScene(string roomId, World world)
{
    var scenePath = GetScenePath(roomId);
    var tempPath = scenePath + ".tmp";
    var backupPath = scenePath + ".backup";
    
    try
    {
        // 1. 导出场景文档
        var document = SceneSerializer.ToDocument(world.AllObjects());
        var json = JsonSerializer.Serialize(document, JsonOptions);
        
        // 2. 写入临时文件
        File.WriteAllText(tempPath, json);
        
        // 3. 备份旧文件
        if (File.Exists(scenePath))
        {
            File.Copy(scenePath, backupPath, overwrite: true);
        }
        
        // 4. 原子替换（Windows: File.Move 不是原子操作，需要用 File.Replace）
        if (File.Exists(scenePath))
        {
            File.Replace(tempPath, scenePath, backupPath);
        }
        else
        {
            File.Move(tempPath, scenePath);
        }
        
        _lastSaveTime = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MapPersistence] 保存失败: {ex.Message}");
    }
    finally
    {
        // 清理临时文件
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }
    }
}
```

---

### 3. 保存版本校验

**问题**：多客户端同时编辑可能产生版本冲突

**解决方案**：存档包含版本号

```json
{
  "Version": 1,
  "SavedAt": "2026-08-03T12:34:56Z",
  "SaveVersion": 42,  ← 服务端版本号
  "Objects": [...]
}
```

**加载时校验**：
```csharp
if (document.SaveVersion > _currentVersion)
{
    // 存档比当前内存新，直接加载
    LoadFromDocument(document);
    _currentVersion = document.SaveVersion;
}
else
{
    // 存档比内存旧，忽略（内存数据更新）
    Console.WriteLine($"[MapPersistence] 存档版本过时，跳过加载");
}
```

---

## 🚀 推荐最终方案

### 三层保存策略

| 层级 | 触发时机 | 延迟 | 频率限制 |
|------|---------|------|---------|
| **实时广播** | 每次操作 | 0ms | 无限制 |
| **防抖保存** | 操作停止后 | 2-5秒 | 动态调整 |
| **定期快照** | 固定周期 | 5分钟 | 每 5 分钟 1 次 |

### 用户感知

**方案 B（轻量提示）**：
- 关键操作后显示"✓ 已保存"（2 秒淡出）
- 右下角小图标：💾 灰色（未保存）→ 绿色（已保存）
- 不阻塞操作，不增加心智负担

### 数据安全

- ✅ 原子写入（防止保存时崩溃）
- ✅ 双备份（主存档 + backup）
- ✅ 版本校验（防止加载过期数据）
- ✅ 崩溃恢复（自动尝试 backup/temp）

---

## 📊 性能对比

| 方案 | 磁盘 I/O | 数据丢失风险 | 用户体验 | 实现复杂度 |
|------|---------|-------------|---------|-----------|
| **当前（立即保存）** | 100% | 0 秒 | ⭐⭐⭐ | 低 |
| **推荐方案** | 10% | 5 秒 | ⭐⭐⭐⭐⭐ | 中 |

**收益**：
- 磁盘 I/O 减少 90%
- 支持更多并发房间（10 倍）
- 用户体验提升（无卡顿）

**风险**：
- 最多丢失 5 秒数据（可接受）

---

## 🎯 实施计划

### Phase 1：核心功能（1 小时）

1. **实现防抖保存**（修改 RoomMapManager.cs）
2. **实现定期快照**（5 分钟强制保存）
3. **实现原子写入**（修改 MapScenePersistence.cs）
4. **测试基础功能**

### Phase 2：数据安全（30 分钟）

5. **添加 backup 机制**
6. **添加崩溃恢复逻辑**
7. **测试极端场景**（崩溃/文件损坏）

### Phase 3：用户感知（1 小时）

8. **客户端添加"已保存"提示**
9. **添加保存状态图标**（可选）
10. **测试用户体验**

### Phase 4：监控和优化（30 分钟）

11. **添加保存耗时日志**
12. **添加磁盘使用率监控**
13. **性能基准测试**

---

## 🧪 验证标准

### 功能测试

- [ ] 连续拖拽 Token 20 次 → 只保存 1 次（2 秒后）
- [ ] 停止操作 5 分钟 → 触发定期快照
- [ ] 保存过程中杀死进程 → 重启后加载 backup
- [ ] 损坏 map.scene → 自动加载 backup

### 性能测试

- [ ] 5 个玩家同时移动 Token → 磁盘 I/O < 10 次/秒
- [ ] 100 个对象 → 保存耗时 < 100ms
- [ ] 1000 个房间在线 → 服务器 CPU < 30%

### 用户体验测试

- [ ] 操作流畅无卡顿
- [ ] 保存提示不干扰操作
- [ ] 断线重连数据完整
- [ ] 崩溃后数据恢复

---

## 📌 总结

**推荐方案**：三层保存 + 轻量提示 + 原子写入

**核心理念**：
1. **实时同步**：玩家操作立即广播（用户体验）
2. **智能保存**：防抖 + 定期快照（性能优化）
3. **数据安全**：原子写入 + 双备份（容错保护）
4. **用户无感**：自动化处理，轻量提示（现代体验）

**最大收益**：
- 磁盘 I/O 减少 90%
- 支持 10 倍并发房间
- 用户体验流畅无卡顿
- 数据安全有保障

要我开始实施这个完整方案吗？预计 2-3 小时完成所有功能。
