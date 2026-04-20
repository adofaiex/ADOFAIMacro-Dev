# ADOFAIMacro

[![C# 10.0](https://img.shields.io/badge/C%23-10.0-239120?logo=csharp&logoColor=white)](https://dotnet.microsoft.com/zh-cn/languages/csharp)
[![.NET Framework 4.8.1](https://img.shields.io/badge/.NET%20Framework-4.8.1-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net481)
[![Visual Studio 2026](https://img.shields.io/badge/Visual%20Studio-2026-5C2D91?logo=visualstudio&logoColor=white)](https://visualstudio.microsoft.com/zh-hans/)
[![License](https://img.shields.io/github/license/2228293026/ADOFAIMacro?color=blue)](https://github.com/2228293026/ADOFAIMacro/blob/master/LICENSE.txt)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](https://github.com/2228293026/ADOFAIMacro/blob/master/AsyncInputOptimize-LICENSE.txt)
[![Downloads](https://img.shields.io/github/downloads/2228293026/ADOFAIMacro/total)](https://github.com/2228293026/ADOFAIMacro/releases)

[中文说明](README.md)

`ADOFAIMacro` is a UnityModManager (UMM) mod for **A Dance of Fire and Ice (ADOFAI)**. It is designed to provide stable, tunable, and filterable automated input workflows, from direct in-game hit triggering to system-level key simulation.

---

## Table of Contents

- [1. Feature Overview](#1-feature-overview)
- [2. How Input Modes Work](#2-how-input-modes-work)
- [3. Installation and Update](#3-installation-and-update)
- [4. Build Guide (Developers)](#4-build-guide-developers)
- [5. Settings Reference](#5-settings-reference)
- [6. Runtime Tuning & Hotkeys](#6-runtime-tuning--hotkeys)
- [7. Recommended Presets](#7-recommended-presets)
- [8. Troubleshooting](#8-troubleshooting)
- [9. Project Structure](#9-project-structure)
- [10. License](#10-license)

---

## 1. Feature Overview

ADOFAIMacro includes the following core capabilities:

- **Automatic macro triggering** based on chart/floor timing.
- **Dual trigger paths**:
  - Direct game logic call via `controller.Hit(false)`.
  - Keyboard simulation through system APIs (SendInput / SkyHook-related path).
- **SkyHook async input mode** for high-frequency scenarios with better stability under stress.
- **Timing offset tuning** with in-game hotkey adjustment support.
- **Death Key support** with configurable key and delay (SkyHook mode required).
- **Key filtering system** with blacklist/whitelist logic for sync and async key sources.
- **Multi-language UI system**: JSON-based key-value translations, currently supporting Chinese/English with easy extensibility.
- **Protected translations**: Certain UI texts (e.g., macro enabled notification) use hardcoded translations, immune to JSON modifications.

---

---

## 2. How Input Modes Work

### 2.1 Direct Hit mode (`SimulateKeyPress = false`)

- Triggers direct game hit logic.
- Pros: short path, predictable latency.
- Best for: users who only need macro timing and do not need OS-level key injection.

### 2.2 Simulated Key mode (`SimulateKeyPress = true`)

- Converts macro events into system keyboard input.
- Available paths:
  - **SendInput** (compatibility-first)
  - **SkyHook** (lower-level path, often preferred for advanced/high-frequency cases)

### 2.3 SkyHook + `InputMode`

When `SkyHookMode = true`, you can select the lower-layer mode:

- `Auto`: automatically pick the lowest available implementation.
- `NtUserInjectKeyboard`: deeper injection route.
- `NtUserSendInput`: lower than standard SendInput.
- `SendInput`: standard Win32 method (best compatibility).

> Start from `Auto`, then switch mode only if you encounter instability or conflicts.

---

## 3. Installation and Update

### 3.1 Requirements

- **UnityModManager** installed and working.
- ADOFAI configured to load UMM mods.

### 3.2 Installation Steps

1. Build the project and obtain `ADOFAIMacro.dll` (plus required dependencies).
2. Create or locate `Mods/ADOFAIMacro` in your UMM mods directory.
3. Copy the following files/folders into that directory:
   - `ADOFAIMacro.dll`
   - `Newtonsoft.Json.dll` (if not in game's Managed folder, copy from NuGet package)
   - `Localization/` folder (contains `zh-CN.json` and `en-US.json`)
   - Native DLLs (`InputSystem.dll`, `TechniqueSimulator.dll`) if present
4. Launch the game and enable `ADOFAIMacro` from UMM.

### 3.3 Update Tips

- Back up your old config before replacing files.
- After updating, verify key options on first launch:
  - `MacroKeys`
  - `TimeOffset`
  - `SkyHookMode` / `InputMode`
  - `EnableKeyFilter` and filter lists

> This repository includes `InputSystem.dll`, loaded at runtime by `InputSystem.Initialize()`.(https://github.com/2228293026/InputSystem)

---

## 4. Build Guide (Developers)

This is a **.NET Framework** C# project (`ADOFAIMacro-Dev.csproj`) that references several DLLs from your local ADOFAI installation.

### 4.1 Common Dependencies

- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `SkyHook.Unity.dll`
- `Newtonsoft.Json.dll` (required for localisation system, get from game Managed folder or NuGet)

> Note: The localisation system uses `Newtonsoft.Json` instead of Unity's `JsonUtility` to support `Dictionary<string, string>` deserialization.

### 4.2 Local Build Flow

1. Update `HintPath` values in `ADOFAIMacro-Dev.csproj` to match your local ADOFAI path.
2. Restore NuGet packages if needed (`packages.config` style).
3. Build `Release` with Visual Studio or MSBuild.
4. Copy the following outputs into `Mods/ADOFAIMacro` for live verification:
   - `ADOFAIMacro.dll`
   - `Newtonsoft.Json.dll` (should copy automatically if referenced correctly)
   - `Localization/` folder (with `zh-CN.json` and `en-US.json`)
   - Native DLLs (`InputSystem.dll`, `TechniqueSimulator.dll`) if they exist in output

---

## 5. Settings Reference

All settings below are available in the UMM panel.

| Setting | Type / Example | Description |
|---|---|---|
| `Macro` | `true / false` | Master switch for macro logic. |
| `MacroKeys` | `D,F,J,K` | Comma-separated key sequence for macro output. |
| `SimulateKeyPress` | `true / false` | Use system key simulation instead of direct hit calls. |
| `SkyHookMode` | `true / false` | Enable SkyHook path for simulated input. |
| `InputMode` | `Auto / NtInject / NtSendInput / SendInput` | Input backend selection when SkyHook mode is enabled. |
| `TimeOffset` | `-100 ~ 100` ms | Trigger timing offset in milliseconds. |
| `EnableKeyAdjust` | `true / false` | Enable runtime offset/step adjustment with `Ctrl + Arrow`. |
| `AdjustStep` | `0.1 ~ 10` | Step size for each runtime adjustment. |
| `EnableArrowTimeAdjust` | `true / false` | Enable Left/Right key adjustment for timing offset. |
| `HighPrecisionAsync` | `true / false` | Experimental high-precision async behavior. |
| `EnableDeathKey` | `true / false` | Auto-press a key on death (SkyHook mode required). |
| `DeathKeyDelay` | `0.1 ~ 30` sec | Delay before death key is fired. |
| `DeathKeyInput` | `R`, `SPACE`, `0x52` | Death key by key name or virtual key code. |
| `EnableKeyFilter` | `true / false` | Enable key filtering system. |
| `FilterMode` | `0 / 1` | `0` = blacklist, `1` = whitelist. |
| `FilteredKeys` | `F1,F2` | Sync input filter list. |
| `FilteredAsyncKeys` | `J,K,L` | Async input filter list (typically used with SkyHook). |

### 5.1 Key String Format

- Supports key names: `A-Z`, `0-9`, `F1-F12`, `SPACE`, `ENTER`, `ESC`, `CTRL`, `ALT`, arrows, etc.
- Supports hex virtual key code format (for example: `0x41`).
- Separate multiple keys with commas (for example: `J,K,L`).

---

## 6. Runtime Tuning & Hotkeys

Depending on your toggles, you can tune behavior during gameplay:

- **Ctrl + Left/Right**: adjust offset/step behavior based on `AdjustStep`.
- **Left/Right**: directly nudge timing offset (`EnableArrowTimeAdjust` required).

For reliability, tune on short levels first, then apply the profile to longer/harder charts.

---

## 7. Recommended Presets

### 7.1 Stable Starter Preset

- `Macro = true`
- `SimulateKeyPress = false`
- `TimeOffset = 0` (then fine-tune gradually)
- `EnableKeyFilter = false` (verify baseline first)

### 7.2 Compatibility-Oriented Preset

- `SimulateKeyPress = true`
- `SkyHookMode = false`
- Use `SendInput`
- Enable key filtering only if you detect conflicts

### 7.3 High-Frequency / Advanced Preset

- `SimulateKeyPress = true`
- `SkyHookMode = true`
- Start with `InputMode = Auto`, then manually test alternatives if needed
- Fine-tune `TimeOffset` and `AdjustStep` incrementally

---

## 8. Troubleshooting

### Q1: Macro is enabled but nothing happens

Check in order:

1. `ADOFAIMacro` is enabled in UMM.
2. `Macro` toggle is on.
3. `MacroKeys` format is valid (comma-separated).
4. If simulation is enabled, test with different `SkyHookMode` / `InputMode` combinations.

### Q2: Inconsistent triggers or dropped presses

- Tune `TimeOffset` in small steps (e.g., 1ms increments).
- Try enabling `SkyHookMode` for high-frequency charts.
- Enable key filtering to isolate conflicting input sources.

### Q3: Death key is not firing

- Confirm `SkyHookMode` is enabled.
- Confirm `EnableDeathKey` is on.
- Validate `DeathKeyInput` key name/code.
- Increase `DeathKeyDelay` and retest.

### Q4: Key filtering seems ineffective

- Confirm `EnableKeyFilter = true`.
- Verify `FilterMode` matches your intention (blacklist vs whitelist).
- In SkyHook scenarios, ensure `FilteredAsyncKeys` is also configured.

---

## 9. Project Structure

```text
ADOFAIMacro/
├─ Main.cs                    # Entry and mod lifecycle
├─ Settings.cs                # Settings and UMM UI
├─ UIUtils.cs                 # UI helper components/styles
├─ ShowText.cs                # Text/overlay helper
├─ Patches.cs                 # Harmony patch definitions
├─ Localization/
│  ├─ LocalizationManager.cs # Localization manager (JSON loading / protection)
│  ├─ zh-CN.json             # Simplified Chinese translations
│  └─ en-US.json             # English translations
├─ Macro/
│  ├─ Macro.cs               # Core macro triggering logic
│  ├─ InputSystem.cs         # Input system wrapper (P/Invoke to InputSystem.dll)
│  ├─ AsyncInputManager.cs   # Async input management
│  ├─ DSPTimeSimulater.cs    # Timing simulation helper
│  ├─ SkyHookSystem.cs       # SkyHook-specific handling
│  └─ TechniqueSimulator.cs  # Technique simulator wrapper (P/Invoke to TechniqueSimulator.dll)
└─ Platform/
   ├─ Windows.cs             # Windows platform (high-res timer)
   ├─ Linux.cs               # Linux platform
   └─ BaseSelect.cs          # Platform selection layer
```

---

## 10. License

- Main project license: `LICENSE.txt`
- Async input optimization license: `AsyncInputOptimize-LICENSE.txt`

---

If you report an issue, include:

- Game version
- ADOFAIMacro version
- Relevant settings (screenshot preferred)
- Whether SkyHook is enabled and current `InputMode`
- Reproduction steps and logs
