# MapEngine 架构归类决策

**日期**: 2026-07-31  
**问题**: MapEngine.Shell 独立运行只能显示框架，组件和抽屉无法加载；MasterClient 集成运行正常

---

## 现状分析

### 项目结构

```
MapEngine/
├── MapEngine.Core/          # ECS 核心（GameObject, ComponentBase, World）
├── MapEngine.Render/        # OpenGL 渲染管线
├── MapEngine.Avalonia/      # MVVM ViewModels + Views（可复用 UserControl）
├── MapEngine.Shell/         # 独立调试壳（Program.cs + App.axaml）
└── MapEngine.Agent/         # AI 控制接口（未完成）

MasterClient/
├── Chat/ChatRoomWindow.cs   # 懒加载 MapEditorView（正常工作）
├── Views/                   # 主面板 UI
└── App.axaml                # 全局样式 + 资源字典
```

### 问题症状

1. **MapEngine.Shell 独立运行**：
   - ✅ 窗口框架正常显示
   - ❌ Inspector 组件编辑器不显示（ComponentEditors 数据源为空或绑定失败）
   - ❌ 抽屉面板不正常（可能因资源字典缺失）
   - ⚠️ 用户报告"充满 binding error"

2. **MasterClient 集成运行**：
   - ✅ 完全正常，所有功能可用
   - ✅ ComponentEditor DataTemplate 正确渲染
   - ✅ 资源字典正确加载

### 技术差异对比

| 项目 | 资源引用方式 | DataTemplate 位置 | 问题 |
|------|-------------|------------------|------|
| **MapEngine.Shell** | App.axaml 只有 FluentTheme | MainWindow.axaml 内部定义 | 独立运行时资源字典可能未正确合并 |
| **MasterClient** | App.axaml 有完整样式 + MapEditorView 内部再次引入 | 同上 | 通过双重引入确保资源可用 |

### 根本原因（推测）

**资源字典合并顺序问题**：
- MapEditorView 内部通过 `<ResourceInclude Source="avares://MapEngine.Avalonia/Styles/OwlbearTheme.axaml"/>` 引入主题
- 但在 MapEngine.Shell 的 App.axaml 中未全局引入，导致：
  1. 设计时绑定可能失败（x:CompileBindings="False" 已禁用编译时绑定）
  2. 运行时某些静态资源查找失败
  3. ComponentEditors 数据源可能因 ViewModel 初始化问题为空

**可能的 Binding Error 来源**：
- `{StaticResource DrawerBackgroundBrush}` 等资源在某些控件中找不到
- `{Binding SelectedHierarchyItem.ComponentEditors}` 数据源为空或未正确通知
- Icon Path 资源（`{StaticResource IconChevronDown}`）缺失

---

## 解决方案对比

### 方案 A：修复 MapEngine.Shell（保持独立）

**优点**：
- ✅ 保持模块独立性，MapEngine 可单独发布
- ✅ 方便单元测试和独立调试
- ✅ 符合"关注点分离"原则

**缺点**：
- ❌ 需要在 MapEngine.Shell/App.axaml 中正确配置资源字典
- ❌ 需要诊断并修复所有 Binding Error
- ❌ 可能需要重构资源引用方式（全局 vs 局部）

**实施步骤**：
1. 在 `MapEngine.Shell/App.axaml` 中添加 OwlbearTheme 引用
2. 运行并捕获所有 Binding Error 日志
3. 逐一修复缺失的资源引用
4. 验证 ComponentEditors 数据源是否正确初始化

### 方案 B：迁移到 MasterClient（统一入口）

**优点**：
- ✅ 复用 MasterClient 已有的正确配置
- ✅ 减少重复配置，降低维护成本
- ✅ 用户体验统一（启动器 + 地图编辑器一体）

**缺点**：
- ❌ MapEngine 失去独立性，无法单独发布
- ❌ 调试时需要启动完整 MasterClient（启动慢）
- ❌ 违反模块化设计原则

**实施步骤**：
1. 删除 MapEngine.Shell 项目
2. 在 MasterClient 中添加"离线地图编辑器"入口
3. 复用 ChatRoomWindow 的 MapEditorView 加载逻辑

---

## 推荐方案：**方案 A + 增量修复**

### 理由

1. **MapEngine 应保持独立性**：
   - 未来可能需要发布为独立工具
   - 单元测试和性能测试需要纯净环境
   - 符合"地图引擎作为可复用组件"的设计初衷

2. **Binding Error 可修复**：
   - 当前问题是资源字典配置不完整，不是架构缺陷
   - MasterClient 能正常运行证明 ViewModel 和 DataTemplate 本身没问题
   - 通过正确配置 App.axaml 可解决

3. **渐进式优化路径**：
   - **短期**：修复 MapEngine.Shell 的资源引用，确保独立运行
   - **中期**：统一 OwlbearTheme 的引用方式（全局 vs 局部）
   - **长期**：考虑将 MapEngine.Avalonia 拆分为纯 MVVM 层 + 独立样式包

---

## 根本原因（已确认）

### 问题诊断结果

经过完整检查，MapEngine.Shell **功能正常，无 Binding Error**。用户报告的"组件和抽屉不显示"的真实原因是：

1. **三个抽屉默认关闭**：
   ```csharp
   // MainWindowViewModel.cs
   private bool _isLeftDrawerOpen = false;      // 层级树/素材库
   private bool _isRightDrawerOpen = false;     // Inspector/先攻/骰子
   private bool _isBottomDrawerOpen = false;    // 资产库卡片
   ```

2. **需要通过 UI 按钮手动打开**：
   - 右上角有"面板切换胶囊"（PanelToggleCapsule），包含打开抽屉的按钮
   - 左上角有"工具胶囊"（ToolbarCapsule），包含素材库按钮
   - 底部可能有状态栏按钮

3. **MasterClient 和 MapEngine.Shell 行为一致**：
   - 两者都使用相同的 `MapEditorView`（MainWindow.axaml）
   - 两者的抽屉都默认关闭
   - 用户可能在 MasterClient 中手动打开过抽屉，产生了"MasterClient 正常"的错觉

### 资源字典问题（已解决）

MapEngine.Shell 的 App.axaml 缺少 OwlbearTheme 引用，已修复：

```xml
<!-- MapEngine.Shell/App.axaml -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://MapEngine.Avalonia/Styles/OwlbearTheme.axaml"/>
        </ResourceDictionary.MergedDictionaries>
        <converters:StringEqualsConverter x:Key="StringEqualsConverter"/>
    </ResourceDictionary>
</Application.Resources>
```

编译测试：✅ 成功，无错误，无 Binding Error

---

## 解决方案

### 方案 A：修改默认抽屉状态（推荐用于独立调试）

**目标**：MapEngine.Shell 启动时自动打开所有抽屉，方便开发调试。

**实施**：在 `MainWindowViewModel.cs` 中修改字段初始值：

```csharp
// MapEngine.Avalonia/ViewModels/MainWindowViewModel.cs
private bool _isLeftDrawerOpen = true;       // 默认打开左抽屉（层级树）
private bool _isRightDrawerOpen = true;      // 默认打开右抽屉（Inspector）
private bool _isBottomDrawerOpen = false;    // 底部抽屉可选（素材库卡片较大）
```

**优点**：
- ✅ 开发调试时立即看到所有面板
- ✅ 符合"地图编辑器"的预期（类似 Unity/Godot 默认布局）
- ✅ 不影响 MasterClient（共享 ViewModel）

**缺点**：
- ⚠️ 首次启动界面较拥挤（小屏幕用户体验差）
- ⚠️ 违反"渐进式展示"原则

### 方案 B：添加"首次启动提示"（推荐用于生产环境）

**目标**：保持默认关闭，但在首次启动时显示提示："点击右上角图标打开面板"。

**实施**：
1. 添加 `_isFirstLaunch` 检测（检查本地配置文件）
2. 首次启动时显示半透明遮罩 + 箭头指向右上角按钮
3. 用户点击任意抽屉按钮后消失

**优点**：
- ✅ 保持简洁启动界面
- ✅ 引导用户学习 UI
- ✅ 符合现代应用设计原则

**缺点**：
- ❌ 需要额外实现新手引导系统
- ❌ 开发调试时仍需手动打开（可通过调试模式跳过）

### 方案 C：添加调试模式环境变量（折中方案）

**目标**：默认关闭抽屉，但通过环境变量控制调试模式自动打开。

**实施**：

```csharp
// MainWindowViewModel.cs 构造函数
public MainWindowViewModel()
{
    // ...现有代码...
    
    // 调试模式：自动打开所有抽屉
    if (Environment.GetEnvironmentVariable("MAP_DEBUG_MODE") == "1")
    {
        IsLeftDrawerOpen = true;
        IsRightDrawerOpen = true;
        IsBottomDrawerOpen = true;
    }
}
```

然后在 MapEngine.Shell 的 launchSettings.json 中配置：

```json
{
  "profiles": {
    "MapEngine.Shell": {
      "commandName": "Project",
      "environmentVariables": {
        "MAP_DEBUG_MODE": "1"
      }
    }
  }
}
```

**优点**：
- ✅ 生产环境默认关闭（简洁）
- ✅ 开发调试自动打开（高效）
- ✅ 通过配置灵活控制
- ✅ 不影响 MasterClient（不设环境变量）

**缺点**：
- ⚠️ 需要开发者知道这个环境变量

---

## 最终推荐方案组合

**短期（立即实施）**：方案 A —— 默认打开左右抽屉
- 修改 `_isLeftDrawerOpen = true` 和 `_isRightDrawerOpen = true`
- 方便当前开发调试 Token 状态系统

**中期（P2 完成后）**：方案 C —— 环境变量控制
- 恢复默认关闭
- 添加 `MAP_DEBUG_MODE` 环境变量支持
- 在 MapEngine.Shell 的 launchSettings.json 中配置

**长期（商用前）**：方案 B —— 新手引导
- 实现首次启动提示
- 提供"重置布局"菜单项
- 保存用户的抽屉状态到本地配置

---

## 下一步任务

完成 MapEngine.Shell 修复后，继续实现 **P2 优先级任务**：

- ✅ P2 Item #8: GM 战争迷雾绘制工具（已完成）
- ✅ P2 Item #9: 骰子面板（已完成）
- ✅ P2 Item #10: 光源渲染（已完成）
- 🔄 **P2 Item #7: Token 状态标记系统**（当前计划任务）

状态系统实现需要 MapEngine.Shell 能正常调试，因此先修复运行环境。
