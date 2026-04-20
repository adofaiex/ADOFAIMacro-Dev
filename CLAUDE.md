# CLAUDE.md

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
- `scrController.PlayerControl_Update` - Main macro update loop
- `scrController.Awake_Rewind` / `scrController.Restart` - Reset macro state
- `scrConductor.Update` - High-precision time synchronization (transpiler patch)
- `SkyHookManager.HookCallback` - Key filtering for async input
- `scrController.CountValidKeysPressed` - Blacklist/whitelist key filtering

**Macro/Macro.cs** - Core macro logic:
- Time-based event system using `HitEvent` struct
- Dual-buffered `TimeAnchor` for thread-safe time synchronization
- Background worker thread for precise timing
- Technique simulation for alternating left/right hand patterns

**Macro/InputSystem.cs** - P/Invoke wrapper for InputSystem.dll:
- Loads native DLL and marshals function pointers
- Provides `PushKeyEvent`, `SendKeyDirect`, `SendKeyCombination` APIs
- Supports multiple input modes: Auto, NtInject, NtSendInput, SendInput

**Macro/TechniqueSimulator.cs** - P/Invoke wrapper for TechniqueSimulator.dll:
- Loads native DLL for technique simulation
- Marshals complex config structures with segment overrides
- Falls back to C# implementation in DEBUG builds

**Macro/AsyncInputManager.cs** - Manages async input queue:
- Sets `timeBeginPeriod(1)` for 1ms sleep precision
- Switches GC to `SustainedLowLatency` mode
- Direct call path to InputSystem (no intermediate queue)

**Macro/DSPTimeSimulater.cs** - High-precision audio time simulation:
- Compensates for Unity's `AudioSettings.dspTime` drift
- Uses platform-specific high-resolution timers

**Settings.cs** - Mod settings with Material 3 UI:
- Multi-language UI via `LocalizationManager.Get()`
- Technique profiles with segment-based overrides
- Key filtering (blacklist/whitelist modes)
- All UI strings use key-based localization (no hardcoded text except icon emojis)

**Localization/LocalizationManager.cs** - Localization system:
- Loads JSON translation files (`zh-CN.json`, `en-US.json`)
- Uses `Newtonsoft.Json` for `Dictionary<string, string>` deserialization
- Provides fallback translations if key missing or file corrupt
- Supports protected translations (hardcoded per-language values that ignore JSON modifications)

---

### Threading Model

The macro uses a producer-consumer pattern:
- **Main thread** (Unity): Updates `TimeAnchor` with current game state every frame
- **Worker thread**: Reads `TimeAnchor`, waits for event times, triggers key presses
- Communication via volatile variables and atomic operations (no locks on hot path)

### Time Synchronization

The system uses multiple time sources:
- `AudioSettings.dspTime` - Unity audio time (corrected by DSPTimeSimulater)
- `QueryPerformanceCounter` - High-resolution CPU timer
- `GetSystemTimePreciseAsFileTime` - Platform-specific wall clock

The `TimeAnchor` struct captures a snapshot of timing state that the worker thread can read without synchronization.

### Native DLLs

**InputSystem.dll** - Low-level input simulation:
- Ring buffer for queued events (8192 capacity)
- Multiple injection modes (NtInject, NtSendInput, SendInput)
- Direct key injection without going through Windows message queue

**TechniqueSimulator.dll** - Technique pattern generation:
- Converts level data into alternating hand patterns
- Supports BPM-based time subdivision
- Per-segment key/press-time/order overrides

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
