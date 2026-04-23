# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ADOFAIMacro is a Unity mod for the rhythm game "A Dance of Fire and Ice" that provides automated input (macro) functionality. The mod uses Harmony patches to intercept game events and simulate key presses with precise timing.

## Build Commands

### C# Project (ADOFAIMacro-Dev.csproj)
```bash
# Build Debug
msbuild ADOFAIMacro-Dev.csproj /p:Configuration=Debug

# Build Release
msbuild ADOFAIMacro-Dev.csproj /p:Configuration=Release

# Clean and rebuild
msbuild ADOFAIMacro-Dev.csproj /t:Clean /p:Configuration=Release
msbuild ADOFAIMacro-Dev.csproj /t:Build /p:Configuration=Release
```

### C++ Projects
```bash
# Build InputSystem.dll
msbuild InputSystem\InputSystem.vcxproj /p:Configuration=Release /p:Platform=x64

# Build TechniqueSimulator.dll
msbuild TechniqueSimulator\TechniqueSimulator.vcxproj /p:Configuration=Release /p:Platform=x64
```

The C# project automatically copies native DLLs to output directories after build.

## Architecture

### Core Components

**Main.cs** - Mod entry point using UnityModManager. Handles mod loading/unloading, Harmony patching, and initialization of native DLLs.

**Patches.cs** - Harmony patches that intercept game methods:
- `scrController.PlayerControl_Update` → Main macro update loop (`Macro.Update()`)
- `scrController.Awake_Rewind` / `scrController.Restart` → Reset macro state
- `scrConductor.Update` → High-precision time synchronization (transpiler patch for `HighPrecisionAsync` mode)
- `SkyHookManager.HookCallback` → Key filtering for async input (SkyHook mode)
- `scrController.CountValidKeysPressed` → Blacklist/whitelist key filtering (sync path)

**Macro/Macro.cs** - Core macro logic (producer-consumer pattern):
- **主线程** (Unity每帧): 解析谱面 → 填充 `HitEvent[]` → 更新 `TimeAnchor` 双缓冲
- **工作线程**: 读取 `TimeAnchor` → 等待触发时间 → 调用 `SendKey()` / `controller.Hit()`
- `HitEvent` 结构体: `TriggerTime` + `KeyCode` + `ReleaseOnly` + `IsHoldRelated`
- `TimeAnchor` 双缓冲: `songPosRef`, `dspTimeRef`, `pitch`, `hitEvents` 等原子交换
- 对象池: `_hitEventPool[65536]` + 复用列表减少 GC
- 支持直接Hit模式和按键模拟模式

**Macro/InputSystem.cs** - P/Invoke wrapper for InputSystem.dll:
- 动态加载 `InputSystem.dll` + 函数指针映射
- API: `PushKeyEvent(key, isDown, delayMs)`, `SendKeyDirect()`, `SendKeyCombination()`
- 输入模式: `Auto`(0), `NtUserInjectKeyboard`(1), `NtUserSendInput`(2), `SendInput`(3)
- `GetAvailableModes()` 查询当前系统支持的模式

**Macro/TechniqueSimulator.cs** - P/Invoke wrapper for TechniqueSimulator.dll:
- 加载 `TechniqueSimulator.dll` 实现高性能左右手交替算法
- 配置结构 `NativeTechniqueConfig` 包含左右手按键、顺序、按压时长、BPM阈值、分段覆盖
- `BuildHitEvents()`: 分配Native内存 → `SetTechniqueConfig()` → 调用 `BuildTechniqueHitEvents()` → 批量复制返回
- DEBUG 构建自动回退到 C# 实现 (`BuildCSHarpTechniqueHitEvents()`)

**Macro/AsyncInputManager.cs** - SkyHook 异步输入管理 (热路径优化):
- `Start()`: `timeBeginPeriod(1)` + `GCSettings.LatencyMode = SustainedLowLatency` + `InputSystem.StartProcessing()`
- `DirectPushKey()`: **工作线程直接调用** `InputSystem.PushKeyEvent(delayMs=0)`，无队列延迟
- 不再使用内部环形缓冲区热路径（已移除消费者线程）
- `ClearQueue()`: 关卡重置时清理 C++ 层残留事件

**Macro/DSPTimeSimulater.cs** - 高精度音频时间补偿:
- 问题: Unity `AudioSettings.dspTime` 存在漂移 (pitch change / seek 会导致跳变)
- 方案: 每帧记录 `(dspTimeRef, songPosRef, qpcSnapshot)`，工作线程用 `QueryPerformanceCounter` 计算 elapsed
- 公式: `audioNow = songPosRef + (dspSnapshot + elapsed - dspTimeRef) * pitch`

**Macro/LevelTechniqueManager.cs** - 关卡特定手法配置管理器:
- 配置文件: `关卡路径.adofaimacro.json` (与关卡同目录)
- `CheckAndLoadLevelConfig()`: 关卡切换时自动加载 (Patches 中 `Awake_Rewind`/`Restart`/`LoadAndPlayLevel` 触发)
- UI 集成: Settings 中"手法模拟"卡底部提供 **加载/保存/删除** 按钮
- `ApplyConfigToSettings()`: 覆盖当前 TechniqueProfile 的按键/顺序/时长/分段
- `SaveConfigForCurrentLevel()`: 保存当前 Settings 配置到关卡文件

**Macro/KeyMap.cs** - 统一按键名→虚拟键码映射:
- `Dictionary<string, byte>` 包含字母、数字、功能键、方向键、小键盘、多媒体键等
- 供 `Macro.ParseTechKeyList()` 和 `TechniqueSimulator.ParseKeys()` 复用
- 避免重复定义和 inconsistency

**Settings.cs** - 设置定义与 Material 3 UI:
- **选项卡**: 语言 / 宏开关 / 按键设置 / 按键过滤 / 延迟设置 / 其他选项 / 手法模拟 / 更新日志 / 作者
- **手法模拟**: TechniqueProfile 列表 + 分段编辑 + 自动加载关卡配置
- **按键过滤**: 独立缓存 `bool[300]` (sync) 和 `bool[256]` (async) 实现 O(1) 过滤
- **本地化**: 所有 UI 文本通过 `LocalizationManager.Get("key")` 获取

**Localization/LocalizationManager.cs** - 多语言系统:
- JSON 文件: `Localization/zh-CN.json`, `Localization/en-US.json`
- `Get(key, args)` 查询顺序: ①受保护硬编码 → ②当前语言字典 → ③fallback 字典 → ④返回key本身
- 受保护键: `macro.enabled_text` (强制中/英硬编码，防篡改)

**Platform/BaseSelect.cs**, `Windows.cs`, `Linux.cs** - 平台抽象:
- 提供高分辨率计时器: `GetFileTime()` → Windows 用 `GetSystemTimePreciseAsFileTime`, Linux 用 `clock_gettime(CLOCK_MONOTONIC_RAW)`

---

### Threading Model

```
┌─────────────────────────────────────────────────────────────┐
│                    Unity Main Thread                        │
│  • PlayerControl_Update (每帧)                              │
│  • Macro.Update()                                          │
│    ├─ Parse level floors → HitEvent[]                     │
│    ├─ Update TimeAnchor (dual-buffer swap)                │
│    └─ Volatile.Write(_currentAnchor, anchor)              │
│  • Hotkey handling (Ctrl+Arrows, etc.)                     │
└─────────────────────────────────────────────────────────────┘
                          │
                          │ atomic swap
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                Worker Thread (High Priority)               │
│  • WorkerLoop()                                            │
│    ├─ Volatile.Read(_currentAnchor)                       │
│    ├─ Compare triggerTime vs audioNow                    │
│    ├─ Sleep(1)/Yield/SpinWait based on waitSec           │
│    ├─ SendKey() / WorkerPressKey()                        │
│    └─ Volatile.Write(_workerLastTriggeredFloor, i)       │
└─────────────────────────────────────────────────────────────┘
```

**无锁策略**:
- `TimeAnchor` 双缓冲: 主线程写 B，工作线程读 A，指针原子交换
- `validFlag` (int): `Volatile.Read/Write` 确保可见性
- `_workerNeedsHit`: `Interlocked.Add` 通知主线程 `controller.Hit()`
- `_resetVersion`: 版本号递增实现 reset 通知，无需 lock

---

### Time Synchronization Deep Dive

**问题**: Unity `AudioSettings.dspTime` 不准确 (seek、pitch change 时跳变)  
**方案**: 混合时间源 + 每帧校准

```
Frame N:
  dspSnapshot = DSPTimeSimulater.GetDSPTime()  // high-res timer
  qpcSnapshot = QueryPerformanceCounter()
  songPosRef = conductor.songposition_minusi
  dspTimeRef = AudioSettings.dspTime  // Unity (unreliable)
  pitch = conductor.song.pitch

Frame N+1 (Worker thread):
  qpcNow = QueryPerformanceCounter()
  elapsed = (qpcNow - qpcSnapshot) * perfFreqInv
  audioNow = songPosRef + (dspSnapshot + elapsed - dspTimeRef) * pitch
  // audioNow 是当前音频播放位置 (秒)
```

**高精度模式 (`HighPrecisionTime = true`)**: Transpiler 替换 `scrConductor.Update` 中的 `DateTime.Now.Ticks` 和 `dspTime` 获取逻辑，使用 `GetSystemTimePreciseAsFileTime` 减少调度延迟。

---

### Native DLL Responsibilities

**InputSystem.dll** (C++ 项目):
- 8192 容量 RingBuffer (可选，热路径已绕过)
- 多种注入模式: `NtUserInjectKeyboard`, `NtUserSendInput`, `SendInput`
- 直接内核层/驱动层注入，绕过 Windows message queue
- `StartProcessing()` / `StopProcessing()` 管理后台消费者线程 (未使用)
- `ClearQueue()` 清空残留事件

**TechniqueSimulator.dll** (C++ 项目):
- 核心算法: 将谱面事件序列转换为交替左右手模式
- BPM 自适应子分片 (default: 60 / (bpm * 2^mult) / 2)
- 支持分段配置 (`TechniqueSegment`): 按地板范围覆盖按键、顺序、按压时长
- 输出 `NativeHitEvent[]` 供 C# 层直接使用

---

### Key Filtering System

双独立的位图缓存 (rebuild on setting change):

```csharp
// Sync keys (KeyCode) -> bool[300]
_keyFilterMap[(int)keyCode] = true  // 在列表中
// 黑名单: !inList; 白名单: inList

// Async keys (VK code) -> bool[256]
_asyncKeyFilterMap[vkCode] = true
```

**补丁位置**: 
- `CountValidKeysPressed` Prefix+Postfix: 拦截游戏内计数逻辑
- `SkyHookManager.HookCallback` Prefix: 拦截 SkyHook 异步输入

---

### Important Notes

- **前置**: UnityModManager 必须已安装
- **DLL 位置**: `InputSystem.dll`, `TechniqueSimulator.dll` 需与 `ADOFAIMacro.dll` 同目录
- **游戏路径**: `.csproj` 中 Steam 路径硬编码，如不同需调整
- **DEBUG 构建**: TechniqueSimulator 使用 C# 回退版本；Release 需要原生 DLL
- **兼容性**: 检测到 BaseMacro 时自动退出游戏防冲突
- **关卡配置**: `LevelConfigAutoLoad` 默认开启，自动应用 `*.adofaimacro.json`

## Important Notes

- The mod requires UnityModManager to be installed in the game
- Native DLLs must be placed in the mod directory alongside ADOFAIMacro.dll
- The game path is hardcoded in the .csproj file - adjust if your Steam installation differs
- DEBUG builds include a C# fallback for technique simulation; Release builds require the native DLL
- Key filtering uses separate caches for sync (KeyCode) and async (VK code) keys
- The mod detects and refuses to load alongside incompatible mods (BaseMacro)

## File Structure

```
ADOFAIMacro/
├── Main.cs                    # Mod entry point
├── Patches.cs                 # Harmony patches
├── Settings.cs                # Settings & UI
├── ShowText.cs                # On-screen macro indicator
├── UIUtils.cs                 # Material 3 UI helpers
├── Localization/
│   ├── LocalizationManager.cs # JSON-based translation system
│   ├── zh-CN.json            # Simplified Chinese translations
│   └── en-US.json            # English translations
├── Macro/
│   ├── Macro.cs               # Core macro logic
│   ├── InputSystem.cs         # InputSystem.dll wrapper
│   ├── TechniqueSimulator.cs  # TechniqueSimulator.dll wrapper
│   ├── AsyncInputManager.cs   # Async input queue manager
│   ├── DSPTimeSimulater.cs    # High-precision time
│   ├── SkyHookSystem.cs       # SkyHook compatibility layer
│   └── KeyMap.cs              # Key name to VK code mapping
├── Platform/
│   ├── BaseSelect.cs          # Platform abstraction
│   ├── Windows.cs             # Windows high-res timer
│   └── Linux.cs               # Linux high-res timer
├── InputSystem/               # C++ InputSystem project
├── TechniqueSimulator/        # C++ TechniqueSimulator project
└── Info.json                 # Mod metadata
```
