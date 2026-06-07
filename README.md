# ADOFAIMacro

[![C# 12.0](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)](https://dotnet.microsoft.com/zh-cn/languages/csharp)
[![.NET Framework 4.8.1](https://img.shields.io/badge/.NET%20Framework-4.8.1-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net481)
[![Visual Studio 2022](https://img.shields.io/badge/Visual%20Studio-2022-5C2D91?logo=visualstudio&logoColor=white)](https://visualstudio.microsoft.com/zh-hans/)
[![License](https://img.shields.io/github/license/adofaiex/ADOFAIMacro-Dev?color=blue)](https://github.com/adofaiex/ADOFAIMacro-Dev/blob/master/LICENSE.txt)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](https://github.com/adofaiex/ADOFAIMacro-Dev/blob/master/AsyncInputOptimize-LICENSE.txt)
[![Downloads](https://img.shields.io/github/downloads/adofaiex/ADOFAIMacro-Dev/total)](https://github.com/adofaiex/ADOFAIMacro-Dev/releases)

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

- **自动触发**：解析谱面地板时间戳，工作线程高精度定时发送按键。
- **触发模式**：
  - **判定触发**（`SimulateKeyPress = false`）：工作线程计数 → 主线程 `controller.Hit()`，跨帧传递。
  - **按键模拟**（`SimulateKeyPress = true`）：工作线程直接调用 `SendKey()` 模拟系统级按键输入。
- **宏按键模式**：
  - **简单轮转**（`EnableTechniqueSimulation = false`）：`MacroKeys` 列表循环，附带 `_pendingKey` 防重叠。
  - **手法模拟**（`EnableTechniqueSimulation = true`）：左右手交替、可配置按键与顺序、BPM 分片、长按处理、同键修正。
- **手法模拟引擎**：C++ 原生 DLL（`TechniqueSimulator.dll`）实现核心分片与事件生成算法，热路径无托管分配；DEBUG 模式下提供 C# 回退。
- **关卡特定手法配置**：为不同关卡保存独立的手法参数（按键、顺序、按压时长、BPM 分段等），进入自动加载。
- **手法配置分段系统**：支持在单关卡的 floor 区间内覆盖按键/顺序/时长设置。
- **时间偏移微调**：毫秒级偏移设置，游戏中 `Ctrl + 左右键` / `左右键` 实时调参。
- **按键过滤系统**：黑白名单模式，同步（`KeyCode` 位图）与异步（VK 码数组）独立过滤。
- **SkyHook 异步输入模式**：`NtUserInjectKeyboard` 等底层注入路径，适合高频/复杂环境。
- **死亡后自动按键（Death Key）**：可配置死亡后按键与触发延迟，仅 SkyHook 模式生效。
- **多语言 UI 系统**：JSON 键值翻译，支持中文/English，易扩展。
- **窗口失焦拦截**：失焦时跳过按键发送，工作线程不暂停。
- **双缓冲时间锚点**：无锁读写 TimeAnchor，主线程写 / 工作线程读的零等待同步。

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
│  ├─ TechniqueSimulator.cs  # 手法模拟器封装（P/Invoke 到 TechniqueSimulator.dll）
│  ├─ LevelTechniqueManager.cs # 关卡特定手法配置管理器
│  └─ KeyMap.cs              # 按键名称到虚拟键码的统一映射
└─ Platform/
   ├─ Windows.cs             # Windows 平台实现（高精度计时器）
   ├─ Linux.cs               # Linux 平台实现
   └─ BaseSelect.cs          # 平台选择层
```

### 9.1 关卡特定手法配置

`LevelTechniqueManager` 负责为每个关卡保存和加载独立的手法模拟配置。这意味着你可以为不同难度或风格的关卡使用不同的按键分配和参数，无需每次手动调整。

**功能特性：**
- **自动检测关卡切换**：进入新关卡时自动加载已保存的配置
- **配置文件位置**：与关卡文件同目录，命名为 `关卡名.adofaimacro.json`
- **UI 集成**：在设置界面中提供：
  - 查看当前关卡配置状态
  - 手动加载/保存/删除关卡配置
  - 自定义配置名称
  - 自动加载开关（`LevelConfigAutoLoad`）
- **配置继承**：关卡配置可覆盖全局配置的所有参数（按键、顺序、按压时长、BPM 分段等）

**使用场景示例：**
- 为高 BPM 段落配置更小的按键组合
- 为特定手癖设计特殊的按键顺序
- 保存针对特定关卡的精细调参

> 在"手法模拟"设置卡底部可管理当前关卡的配置。

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
