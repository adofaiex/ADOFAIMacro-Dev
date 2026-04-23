# ADOFAIMacro

[![C# 12.0](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)](https://dotnet.microsoft.com/zh-cn/languages/csharp)
[![.NET Framework 4.8.1](https://img.shields.io/badge/.NET%20Framework-4.8.1-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net481)
[![Visual Studio 2026](https://img.shields.io/badge/Visual%20Studio-2026-5C2D91?logo=visualstudio&logoColor=white)](https://visualstudio.microsoft.com/zh-hans/)
[![License](https://img.shields.io/github/license/2228293026/ADOFAIMacro-Dev?color=blue)](https://github.com/2228293026/ADOFAIMacro-Dev/blob/master/LICENSE.txt)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](https://github.com/2228293026/ADOFAIMacro-Dev/blob/master/AsyncInputOptimize-LICENSE.txt)
[![Downloads](https://img.shields.io/github/downloads/2228293026/ADOFAIMacro-Dev/total)](https://github.com/2228293026/ADOFAIMacro-Dev/releases)

[English README](README.en.md)

`ADOFAIMacro` 是一个用于 **A Dance of Fire and Ice (ADOFAI)** 的 UnityModManager（UMM）模组，核心目标是提供更稳定、可调、可过滤的自动输入能力，覆盖从”直接判定触发”到”系统级按键模拟”的多种使用场景。

---

## 目录

- [1. 功能简介](#1-功能简介)
- [2. 工作模式说明](#2-工作模式说明)
- [3. 安装与更新](#3-安装与更新)
- [4. 构建说明（开发者）](#4-构建说明开发者)
- [5. 设置项详细说明](#5-设置项详细说明)
- [6. 运行时快捷键与调参](#6-运行时快捷键与调参)
- [7. 推荐配置](#7-推荐配置)
- [8. 常见问题与排查](#8-常见问题与排查)
- [9. 项目结构概览](#9-项目结构概览)
- [10. 许可证](#10-许可证)

---

## 1. 功能简介

ADOFAIMacro 提供以下核心能力：

- **自动宏触发**：根据谱面时间点自动触发输入。
- **双触发路径**：
  - 直接调用游戏逻辑层 `controller.Hit(false)`。
  - 通过系统按键模拟触发（SendInput / SkyHook 相关路径）。
- **SkyHook 异步输入模式**：适合高频输入场景，降低输入抖动带来的不稳定。
- **时间偏移微调**：支持毫秒级偏移设置，并可在游戏中快速调参。
- **死亡后自动按键（Death Key）**：可配置死亡后按键与触发延迟（依赖 SkyHook 模式）。
- **按键过滤系统**：支持黑白名单与同步/异步按键列表，减少冲突输入。
- **多语言 UI 系统**：基于 JSON 的键值翻译，支持中文/English，可轻松扩展其他语言。
- **保护翻译机制**：特定 UI 文本（如宏开启提示）使用硬编码翻译，防止玩家通过修改 JSON 文件篡改。

---

---

## 2. 工作模式说明

### 2.1 直接 Hit 模式（`SimulateKeyPress = false`）

- 宏触发时直接命中游戏逻辑。
- 优点：链路短、延迟可控。
- 适合：纯宏判定场景、不依赖系统按键注入的用户。

### 2.2 按键模拟模式（`SimulateKeyPress = true`）

- 将宏触发转换为系统层按键输入。
- 可选：
  - **SendInput 路径**（兼容性优先）
  - **SkyHook 路径**（更偏底层、适合复杂/高频环境）

### 2.3 SkyHook + InputMode

当 `SkyHookMode = true` 时，可进一步选择输入模式：

- `Auto`：自动选择可用的更低层实现。
- `NtUserInjectKeyboard`：更低层注入路径。
- `NtUserSendInput`：介于底层注入与标准 SendInput 之间的路径。
- `SendInput`：标准 Win32 方式，兼容性最好。

> 建议从 `Auto` 开始，遇到冲突或异常时再逐项切换测试。

---

## 3. 安装与更新

### 3.1 前置条件

- 已安装并可正常运行 **UnityModManager**。
- ADOFAI 可通过 UMM 加载模组。

### 3.2 安装步骤

1. 编译项目得到 `ADOFAIMacro.dll`（及相关依赖）。
2. 在 UMM 模组目录创建或定位 `Mods/ADOFAIMacro`。
3. 将以下文件/文件夹复制到该目录：
   - `ADOFAIMacro.dll`
   - `Newtonsoft.Json.dll`（如果不在游戏目录中，请从 NuGet 包或游戏 Managed 目录复制）
   - `Localization/` 文件夹（包含 `zh-CN.json` 和 `en-US.json`）
   - 原生的 DLL（如 `InputSystem.dll`, `TechniqueSimulator.dll`，如果存在）
4. 启动游戏，在 UMM 面板中启用 `ADOFAIMacro`。

### 3.3 更新建议

- 更新前备份旧版配置。
- 覆盖新文件后，首次进游戏建议检查：
  - `MacroKeys`
  - `TimeOffset`
  - `SkyHookMode` / `InputMode`
  - `EnableKeyFilter` 配置是否符合当前习惯。

> 仓库包含 `InputSystem.dll`，运行时由 `InputSystem.Initialize()` 尝试加载。（https://github.com/2228293026/InputSystem）

---

## 4. 构建说明（开发者）

本项目为 **.NET Framework** C# 项目（`ADOFAIMacro-Dev.csproj`），依赖 ADOFAI 本体目录中的托管 DLL。

### 4.1 关键依赖

常见引用包括（以本地环境为准）：

- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `SkyHook.Unity.dll`
- `Newtonsoft.Json.dll`（本地化系统所需，请从游戏目录或 NuGet 获取）

> 注意：本地化系统使用 `Newtonsoft.Json` 而非 Unity 的 `JsonUtility`，以支持 `Dictionary<string, string>` 反序列化。

### 4.2 开发工具下载（编程语言 / 编译器）

- 编程语言：C#（Microsoft Learn）
  - https://learn.microsoft.com/dotnet/csharp/
- .NET Framework 开发包下载（用于目标框架构建）
  - https://dotnet.microsoft.com/download/dotnet-framework
- 编译器 / IDE：Visual Studio（建议安装 **.NET 桌面开发** 工作负载）
  - https://visualstudio.microsoft.com/zh-hans/downloads/

### 4.3 本地构建流程

1. 检查 `ADOFAIMacro-Dev.csproj` 的 `HintPath`，指向你本机 ADOFAI 安装目录。
2. 如需，先执行 NuGet 还原（`packages.config` 方式）。
3. 使用 Visual Studio 或 MSBuild 构建 `Release`。
4. 将以下产物复制到 UMM 模组目录（`Mods/ADOFAIMacro/`）进行联调：
   - `ADOFAIMacro.dll`
   - `Newtonsoft.Json.dll`
   - `Localization/` 文件夹（包含 `zh-CN.json` 和 `en-US.json`）
   - 以及任何需要的原生 DLL（`InputSystem.dll`, `TechniqueSimulator.dll` 等）

---

## 5. 设置项详细说明

以下设置均可在 UMM 面板中调整：

| 设置项 | 类型 / 示例 | 说明 |
|---|---|---|
| `Macro` | `true / false` | 宏总开关。关闭后不执行宏逻辑。 |
| `MacroKeys` | `D,F,J,K` | 宏按键序列，使用英文逗号分隔。 |
| `SimulateKeyPress` | `true / false` | 是否用系统按键模拟替代直接 Hit。 |
| `SkyHookMode` | `true / false` | 按键模拟时是否使用 SkyHook 路径。 |
| `InputMode` | `Auto / NtInject / NtSendInput / SendInput` | SkyHook 模式下的底层输入方式。 |
| `TimeOffset` | `-100 ~ 100` (ms) | 宏触发时间偏移（毫秒）。 |
| `EnableKeyAdjust` | `true / false` | 允许在游戏中使用 `Ctrl + 方向键` 调整。 |
| `AdjustStep` | `0.1 ~ 10` | 每次热键调整时的步长。 |
| `EnableArrowTimeAdjust` | `true / false` | 允许用左右键快速调整延迟。 |
| `HighPrecisionAsync` | `true / false` | 实验性高精度异步开关。 |
| `EnableDeathKey` | `true / false` | 死亡后自动按键（需 SkyHook 模式）。 |
| `DeathKeyDelay` | `0.1 ~ 30` | 死亡后按键触发延迟（秒）。 |
| `DeathKeyInput` | `R` / `SPACE` / `0x52` | 死亡后按键，支持名称与虚拟键码。 |
| `EnableKeyFilter` | `true / false` | 启用按键过滤系统。 |
| `FilterMode` | `0/1` | 0=黑名单（阻止列表内按键）；1=白名单（仅允许列表内按键）。 |
| `FilteredKeys` | `F1,F2` | 同步输入过滤列表。 |
| `FilteredAsyncKeys` | `J,K,L` | 异步输入过滤列表（通常用于 SkyHook）。 |

### 5.1 按键字符串格式

- 支持：`A-Z`、`0-9`、`F1-F12`、`SPACE`、`ENTER`、`ESC`、`CTRL`、`ALT`、方向键等。
- 也支持十六进制虚拟键码：如 `0x41`。
- 多个键使用英文逗号分隔，例如：`J,K,L`。

---

## 6. 运行时快捷键与调参

根据设置开关，游戏中可进行快速调参：

- **Ctrl + 左/右方向键**：按 `AdjustStep` 调整偏移。
- **左右方向键**：直接微调延迟（受 `EnableArrowTimeAdjust` 控制）。

建议先在短图测试，确认稳定后再用于长图或高密谱。

---

## 7. 推荐配置

### 7.1 追求稳定（入门）

- `Macro = true`
- `SimulateKeyPress = false`
- `TimeOffset = 0`（再逐步微调）
- `EnableKeyFilter = false`（先确认基础可用）

### 7.2 追求兼容（多软件并行）

- `SimulateKeyPress = true`
- `SkyHookMode = false`
- 使用 `SendInput` 路径
- 必要时开启 `EnableKeyFilter` 做冲突隔离

### 7.3 高频场景（进阶）

- `SimulateKeyPress = true`
- `SkyHookMode = true`
- `InputMode = Auto` 起步，不稳再手动切换
- 逐步微调 `TimeOffset` 与 `AdjustStep`

---

## 8. 常见问题与排查

### Q1：宏开了但没反应

请按顺序检查：

1. UMM 中 `ADOFAIMacro` 是否已启用。
2. `Macro` 是否打开。
3. `MacroKeys` 格式是否正确（英文逗号分隔）。
4. 若使用模拟输入，尝试切换 `SkyHookMode` 与 `InputMode`。

### Q2：有时触发、有时漏键

- 先调整 `TimeOffset`（例如每次 1ms 微调）。
- 高频场景尝试开启 `SkyHookMode`。
- 开启 `EnableKeyFilter`，屏蔽冲突来源。

### Q3：死亡后按键不生效

- 确认 `SkyHookMode` 已开启。
- 检查 `EnableDeathKey` 是否启用。
- 检查 `DeathKeyInput` 是否为有效按键名或键码。
- 尝试增加 `DeathKeyDelay`。

### Q4：按键过滤看起来“没有作用”

- 确认 `EnableKeyFilter = true`。
- 确认 `FilterMode` 与你的目标一致（黑名单/白名单）。
- SkyHook 场景下，别忘了设置 `FilteredAsyncKeys`。

---

## 9. 项目结构概览

```text
ADOFAIMacro/
├─ Main.cs                    # 入口与 Mod 生命周期
├─ Settings.cs                # UI 与设置定义
├─ UIUtils.cs                 # UMM 面板绘制辅助
├─ ShowText.cs                # 文本提示相关
├─ Patches.cs                 # Harmony 补丁逻辑
├─ Localization/
│  ├─ LocalizationManager.cs # 本地化管理器（JSON 加载/保护机制）
│  ├─ zh-CN.json             # 简体中文翻译
│  └─ en-US.json             # 英文翻译
├─ Macro/
│  ├─ Macro.cs               # 核心宏触发逻辑
│  ├─ InputSystem.cs         # 输入系统封装（P/Invoke 到 InputSystem.dll）
│  ├─ AsyncInputManager.cs   # 异步输入管理
│  ├─ DSPTimeSimulater.cs    # 时间模拟辅助
│  ├─ SkyHookSystem.cs       # SkyHook 相关处理
│  └─ TechniqueSimulator.cs  # 手法模拟器封装（P/Invoke 到 TechniqueSimulator.dll）
└─ Platform/
   ├─ Windows.cs             # Windows 平台实现（高精度计时器）
   ├─ Linux.cs               # Linux 平台实现
   └─ BaseSelect.cs          # 平台选择层
```

---

## 10. 许可证

- 项目许可证：`LICENSE.txt`
- 异步输入优化许可：`AsyncInputOptimize-LICENSE.txt`

---

如果你在使用中遇到特定机型/系统版本相关问题，建议提交 issue 时附上：

- 游戏版本
- ADOFAIMacro 版本
- 关键配置（可截图）
- 是否使用 SkyHook / 当前 InputMode
- 复现步骤与日志信息
