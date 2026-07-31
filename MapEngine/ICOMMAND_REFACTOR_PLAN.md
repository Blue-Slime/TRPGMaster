# ICommand 重构计划

## 目标
删除所有旧的 `ICommand` 实现，全部迁移到 `IWorldCommand` 接口，实现：
1. 命令可序列化（支持联机同步）
2. 命令操作 World/GameObject 层（ECS 权威）
3. ViewModel 层变为 World 的只读视图

## 待迁移命令清单

### ViewModelCommands.cs (6 个)
- [x] `VmAddEmptyObjectCommand` → `WorldAddObjectCommand` (已存在)
- [x] `VmCreateInstanceCommand` → `WorldAddObjectCommand` (已存在)
- [x] `VmDeleteHierarchyItemCommand` → `WorldDeleteObjectCommand` (已存在)
- [x] `VmMoveObjectCommand` → `WorldMoveObjectCommand` (已存在)
- [ ] `VmRenameCommand` → `WorldRenameCommand` (需新建)
- [ ] `VmSetPropertyCommand` → `WorldSetPropertyCommand` (需新建)

### HierarchyOrderCommands.cs (4 个)
- [ ] `VmMoveItemUpCommand` → `WorldReorderChildCommand` (需新建)
- [ ] `VmMoveItemDownCommand` → `WorldReorderChildCommand` (需新建)
- [ ] `VmPromoteItemCommand` → `WorldReparentCommand` (需新建)
- [ ] `VmDemoteItemCommand` → `WorldReparentCommand` (需新建)

### WallHandleCommands.cs (3 个)
- [ ] `VmMoveWallHandleCommand` → `WorldMoveWallHandleCommand` (需新建)
- [ ] `VmInsertWallHandleCommand` → `WorldInsertWallHandleCommand` (需新建)
- [ ] `VmDeleteWallHandleCommand` → `WorldDeleteWallHandleCommand` (需新建)

## 架构变更

### 当前架构（旧）
```
用户操作 → VmXxxCommand → MainWindowViewModel 修改
                      ↓
              HierarchyItemViewModel 更新
                      ↓
              RefreshMapRenderableItems()
                      ↓
              渲染层读取 ViewModel
```

### 目标架构（新）
```
用户操作 → WorldXxxCommand → World/GameObject 修改（权威）
                          ↓
                   CommandExecuted 事件
                          ↓
              MainWindowViewModel 监听并同步
                          ↓
              HierarchyItemViewModel 自动更新（只读视图）
                          ↓
              渲染层读取 ViewModel
```

## 实施步骤

### Phase 1: 创建缺失的 WorldCommand
1. 创建 `WorldRenameCommand`
2. 创建 `WorldSetPropertyCommand`
3. 创建 `WorldReorderChildCommand`
4. 创建 `WorldReparentCommand`
5. 创建墙体相关命令

### Phase 2: MainWindowViewModel 监听 World 变化
1. 订阅 `CommandBus.CommandExecuted` 事件
2. 根据命令类型同步 HierarchyItemViewModel
3. 自动调用 `RefreshMapRenderableItems()`

### Phase 3: 替换所有调用点
1. 查找所有 `_commandBus.Execute(new VmXxxCommand(...))` 调用
2. 替换为对应的 `WorldXxxCommand`
3. 更新参数（从 ViewModel ID 转为 GameObject Guid）

### Phase 4: 删除旧命令
1. 删除 `ViewModelCommands.cs`
2. 删除 `HierarchyOrderCommands.cs`
3. 删除 `WallHandleCommands.cs`
4. 删除 `LegacyCommandAdapter`
5. 删除 `CommandBus.Execute(ICommand)` 重载

### Phase 5: 更新序列化器
1. 在 `CommandSerializer.Serialize()` 中添加所有新命令的序列化逻辑
2. 在 `CommandSerializer.Deserialize()` 中添加反序列化逻辑
3. 在 `CommandSerializer.SerializeInverse()` 中添加逆命令序列化

## 风险和注意事项

1. **破坏性重构**：这会影响几乎所有的编辑操作
2. **测试覆盖**：每个命令迁移后都需要手动测试
3. **Undo/Redo**：需要确保撤销栈正常工作
4. **性能**：World → ViewModel 同步可能有性能开销
5. **联机同步**：新命令必须正确序列化/反序列化

## 当前状态

- 已完成：4 个基础命令（Add, Delete, Move, Rename）
- 待完成：9 个命令 + 架构同步机制
- 预计工作量：2-3 天（包含测试）

## 决策点

**是否现在开始重构？**
- 优点：彻底解决序列化问题，为联机功能铺路
- 缺点：大型重构，可能引入新 bug，延迟 Token UI 调试

**建议**：
1. 先完成 Token UI 调试（当前任务）
2. 提交当前分支到 GitHub
3. 新建 `feature/world-command-refactor` 分支
4. 逐步迁移命令，每迁移一个就测试一次
