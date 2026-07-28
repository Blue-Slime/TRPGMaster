# TRPGMaster - 跑团大师 2.0

**版本**: 2.0  
**日期**: 2026-07-20  
**架构**: 前端计算 + 自研IM + 完整地图编辑器

---

## 🎯 项目简介

TRPGMaster 是一个专为桌面角色扮演游戏（TRPG）设计的多人在线协作平台，提供：
- 实时消息通讯（自研IM框架）
- 完整的地图编辑器（墙壁、视野锥、门、光源）
- 客户端/服务器架构
- 跨平台支持（基于 Avalonia UI）

---

## 🏗️ 项目结构

```
G:\跑团大师\02-新项目\
├── MasterClient/         # 客户端应用（玩家端）
├── MasterServer/         # WebSocket 服务器
├── MasterServerUI/       # 服务器管理界面
├── MasterStarter/        # 启动器（推荐入口）
├── TRPGMaster.Assets/    # 素材管理模块
├── MasterIM/             # 自研 IM 通讯框架
│   ├── Master.IM.Models/     # 数据模型
│   ├── Master.IM.SDK/        # 客户端 SDK
│   └── Master.IM.Server/     # 服务端实现
├── MapEditor/            # 地图编辑器（完整功能）
│   ├── MapEditor.Core/       # 核心逻辑
│   ├── MapEditor.Avalonia/   # UI 界面
│   ├── MapEditor.Render/     # OpenGL 渲染引擎
│   └── MapEditor.Agent/      # AI 代理
└── TRPGMaster.sln        # 解决方案文件
```

---

## 🚀 快速开始

### 环境要求
- **.NET 9.0 SDK**
- **Windows / Linux / macOS**
- **Git**（可选）

### 构建项目

```bash
cd "G:\跑团大师\02-新项目"
dotnet build TRPGMaster.sln
```

### 运行应用

#### 方式 1：使用启动器（推荐）
```bash
dotnet run --project MasterStarter/MasterStarter.csproj
```

#### 方式 2：分别启动服务器和客户端

**启动服务器**:
```bash
dotnet run --project MasterServer/MasterServer.csproj
```

**启动客户端**:
```bash
dotnet run --project MasterClient/MasterClient.csproj
```

#### 方式 3：使用 VSCode 调试

1. 打开项目文件夹
2. 按 `F5` 选择 "启动 MasterStarter（启动器）"
3. 或者选择 "启动服务器+客户端" 同时启动两者

---

## 🔧 技术栈

### 前端
- **Avalonia 11.3.18** — 跨平台 UI 框架
- **CommunityToolkit.Mvvm** — MVVM 工具包
- **Silk.NET** — OpenGL 渲染（地图编辑器）

### 后端
- **ASP.NET Core 9.0** — Web 框架
- **WebSocket** — 实时通讯
- **SQLite** — 消息和对象存储

### 开发工具
- **.NET 9.0**
- **C# 12**
- **Visual Studio Code** / **Visual Studio 2022**

---

## 📦 核心功能

### 1. IM 通讯系统
- ✅ WebSocket 实时消息推送
- ✅ 无限期消息存储
- ✅ 超时空编辑（修改历史消息）
- ✅ 断线重连
- ✅ 对象同步机制

### 2. 地图编辑器
- ✅ **墙壁系统** — 控制点拖拽、门/窗户
- ✅ **视野锥系统** — Angular Sweep 算法
- ✅ **楼层管理** — 多层地图
- ✅ **命令系统** — Undo/Redo（100步历史）
- ✅ **层级树编辑器** — 场景对象管理
- ✅ **Inspector 属性面板** — 实时编辑对象属性
- 🚧 迷雾系统（开发中）
- 🚧 光源系统（开发中）

### 3. 客户端功能
- ✅ 实时聊天
- ✅ 地图显示和编辑
- ✅ 对象同步
- 🚧 骰子引擎
- 🚧 角色卡管理

### 4. 服务器功能
- ✅ WebSocket 服务
- ✅ 消息存储（按月分库）
- ✅ 对象存储
- ✅ 实时推送

---

## 🎮 使用说明

### 地图编辑器

从客户端主界面点击 **"打开地图编辑器"** 按钮，即可启动独立的地图编辑器窗口。

**快捷键**:
- `Ctrl + Z` — 撤销
- `Ctrl + Y` — 重做
- `Delete` — 删除选中对象
- `Ctrl + C/V` — 复制/粘贴

**工具**:
- 选择工具 — 选择和移动对象
- 绘制工具 — 绘制墙壁
- 擦除工具 — 删除对象

---

## 📖 开发文档

### 架构设计
参考旧版需求书：`G:\跑团大师\01-需求文档\需求书\`
- `TRPGMaster_总需求书_v5.0.md`
- `TRPGMaster_客户端需求书_v5.0.md`
- `TRPGMaster_服务端需求书_v5.0.md`
- `TRPGMaster_消息系统需求书_v5.0.md`

### 核心原则
1. **本地优先** — 所有计算在客户端执行
2. **实时同步** — WebSocket 实时推送更新
3. **可扩展性** — 模块化架构，易于添加新功能

---

## 🐛 已知问题

### 当前状态
- ✅ 所有项目构建成功
- ✅ Avalonia 版本统一（11.3.18）
- ✅ 项目引用路径正确
- ✅ VSCode 调试配置完成

### 待完善功能
- [ ] 骰子引擎实现
- [ ] 角色卡管理
- [ ] 迷雾系统
- [ ] 光源系统
- [ ] 第三方语音插件集成

---

## 🔄 版本历史

### v2.0 (2026-07-20)
- ✅ 复用旧版 UI 项目
- ✅ 重新组织项目结构
- ✅ 统一 Avalonia 版本到 11.3.18
- ✅ 修复 XAML 兼容性问题
- ✅ 创建 VSCode 调试配置
- ✅ 完整构建成功

---

## 📞 相关资源

### 归档项目
旧版项目已归档至：`G:\跑团大师\04-旧项目\跑团大师2026年6月\`

包含：
- 完整可运行的 2026年6月 版本
- 所有开发文档
- 历史开发记录

### 文档位置
- 需求文档：`G:\跑团大师\01-需求文档\`
- 技术设计：`G:\跑团大师\03-技术设计\`
- 测试文档：`G:\跑团大师\06-测试文档\`

---

## 🎯 下一步计划

1. **运行和测试** — 启动 MasterStarter，验证基础功能
2. **实现骰子引擎** — 本地骰子计算
3. **完善地图编辑器** — 迷雾和光源系统
4. **优化 IM 性能** — 大量消息场景优化
5. **添加单元测试** — 确保代码质量

---

**项目状态**: ✅ 可构建、可运行  
**最后更新**: 2026-07-20  
**维护者**: TRPGMaster 开发团队
