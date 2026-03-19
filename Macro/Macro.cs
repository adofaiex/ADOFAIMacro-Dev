using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

#nullable enable

namespace ADOFAIMacro.Macro
{
#pragma warning disable CS0420
    #region TimeBasedMacro

    internal static class Macro
    {
        // ─────────────────────────────────────────────
        //  预处理事件
        // ─────────────────────────────────────────────
        internal readonly struct HitEvent(double triggerTime, byte keyCode, bool releaseOnly,
                                          bool isHoldRelated = false, byte releaseKeyCode = 0)
        {
            public readonly double TriggerTime = triggerTime;
            public readonly byte KeyCode = keyCode;
            public readonly bool ReleaseOnly = releaseOnly;
            public readonly bool IsHoldRelated = isHoldRelated;
            public readonly byte ReleaseKeyCode = releaseKeyCode;
        }

        private readonly struct PieceInfo(int ec, int h, double pl, double st, double et, int es, int mult = 0)
        {
            public readonly int EvCount = ec;
            public readonly int Hand = h;
            public readonly double PieceLen = pl;
            public readonly double StartTime = st;
            public readonly double EndTime = et;
            public readonly int EvStart = es;
            public readonly int Multiplier = mult;
        }

        // ─────────────────────────────────────────────
        //  主线程专属数据
        // ─────────────────────────────────────────────
        private static scrLevelMaker? levelMaker;
        private static scrConductor? conductor;
        private static scrFloor[]? cachedFloors;
        private static bool initialized = false;
        private static string lastKeysSetting = "";
        private static readonly List<byte> keyCodes = [with(4)];
        private static int _keyCodesVersion = 0;

        // ─────────────────────────────────────────────
        //  只读共享数据（初始化后不变）
        // ─────────────────────────────────────────────
        private static HitEvent[]? _hitEvents;
        private static int _hitEventCount;
        private static int floorCount;

        // ─────────────────────────────────────────────
        //  时间锚点（双缓冲）
        // ─────────────────────────────────────────────
        private sealed class TimeAnchor
        {
            public double songPosRef;
            public double dspTimeRef;
            public double dspSnapshot;
            public long qpcSnapshot;
            public double pitch;
            public double timeOffset;
            public bool simulateKeyPress;

            public HitEvent[]? hitEvents;
            public int hitEventCount;

            public byte[] keyCodesSnapshot;
            public int keyCodesVersion;

            public int validFlag;
            public int staticVersion;

#pragma warning disable IDE1006
            public bool valid
#pragma warning restore IDE1006
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Volatile.Read(ref validFlag) == 1;
            }

            public TimeAnchor() { keyCodesSnapshot = []; }
        }

        private static readonly TimeAnchor _anchorA = new();
        private static readonly TimeAnchor _anchorB = new();
        private static volatile TimeAnchor _currentAnchor = _anchorA;

        private static double _songPosRef;
        private static double _dspTimeRef;
        private static float _lastPitch;
        private static int _staticAnchorVersion = 0;

        private static readonly double perfFreqInv;

        private static volatile int _workerLastTriggeredFloor = -1;
        private static volatile int _workerNeedsHit = 0;
        private static volatile int _resetVersion = 0;

        // ─────────────────────────────────────────────
        //  工作线程控制
        // ─────────────────────────────────────────────
        private static volatile Thread? _workerThread;
        private static volatile bool _workerRunning = false;
        private static readonly SemaphoreSlim _startSignal = new(0, 1);
        private static volatile bool _workerStarted = false;

        private static byte _pendingKey;
        private static bool _isKeyDown;
        private static byte _holdKey;
        private static bool _isHoldDown;

        private static volatile bool _cachedSkyHookMode = false;
        private static volatile bool _cachedHighPrecision = false;
        private static volatile bool skyHookInitialized = false;

        // ─────────────────────────────────────────────
        //  Win32
        // ─────────────────────────────────────────────
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0;
        private const uint KEYEVENTF_KEYUP = 2;

        // ─────────────────────────────────────────────
        //  手法模拟全局数据
        // ─────────────────────────────────────────────
        private static byte[] _techLeftKeys = [];
        private static byte[] _techRightKeys = [];
        private static readonly int[][][] _techKeyOrders = [[], []];
        private static readonly double[][] _techPressDur = [[], []];
        private static List<Settings.TechniqueSegment>? _currentSegments;

        [ThreadStatic]
        private static SkyHookSystem.INPUT _cachedInput;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, IntPtr pInputs, int cbSize);
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long lpFrequency);
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private static readonly long perfFrequency;
        private static readonly bool usePerfCounter;
        private static readonly byte[] scanCodeCache = new byte[256];

        // ─────────────────────────────────────────────
        //  按键名称 → VK 映射（internal，供 TechniqueSimulator 复用）
        // ─────────────────────────────────────────────
        internal static readonly Dictionary<string, byte> KeyNameToCode = new()
        {
            ["A"] = 0x41,
            ["B"] = 0x42,
            ["C"] = 0x43,
            ["D"] = 0x44,
            ["E"] = 0x45,
            ["F"] = 0x46,
            ["G"] = 0x47,
            ["H"] = 0x48,
            ["I"] = 0x49,
            ["J"] = 0x4A,
            ["K"] = 0x4B,
            ["L"] = 0x4C,
            ["M"] = 0x4D,
            ["N"] = 0x4E,
            ["O"] = 0x4F,
            ["P"] = 0x50,
            ["Q"] = 0x51,
            ["R"] = 0x52,
            ["S"] = 0x53,
            ["T"] = 0x54,
            ["U"] = 0x55,
            ["V"] = 0x56,
            ["W"] = 0x57,
            ["X"] = 0x58,
            ["Y"] = 0x59,
            ["Z"] = 0x5A,
            ["0"] = 0x30,
            ["1"] = 0x31,
            ["2"] = 0x32,
            ["3"] = 0x33,
            ["4"] = 0x34,
            ["5"] = 0x35,
            ["6"] = 0x36,
            ["7"] = 0x37,
            ["8"] = 0x38,
            ["9"] = 0x39,
            ["`"] = 0xC0,
            ["-"] = 0xBD,
            ["="] = 0xBB,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            ["\\"] = 0xDC,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            [" "] = 0x20,
            ["F1"] = 0x70,
            ["F2"] = 0x71,
            ["F3"] = 0x72,
            ["F4"] = 0x73,
            ["F5"] = 0x74,
            ["F6"] = 0x75,
            ["F7"] = 0x76,
            ["F8"] = 0x77,
            ["F9"] = 0x78,
            ["F10"] = 0x79,
            ["F11"] = 0x7A,
            ["F12"] = 0x7B,
            ["CTRL"] = 0x11,
            ["LCTRL"] = 0xA2,
            ["RCTRL"] = 0xA3,
            ["SHIFT"] = 0x10,
            ["LSHIFT"] = 0xA0,
            ["RSHIFT"] = 0xA1,
            ["ALT"] = 0x12,
            ["LALT"] = 0xA4,
            ["RALT"] = 0xA5,
            ["WIN"] = 0x5B,
            ["LWIN"] = 0x5B,
            ["RWIN"] = 0x5C,
            ["MENU"] = 0x5D,
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["HOME"] = 0x24,
            ["END"] = 0x23,
            ["PAGEUP"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["INSERT"] = 0x2D,
            ["DELETE"] = 0x2E,
            ["BACKSPACE"] = 0x08,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["SPACE"] = 0x20,
            ["SPACEBAR"] = 0x20,
            ["NUMPAD0"] = 0x60,
            ["NUMPAD1"] = 0x61,
            ["NUMPAD2"] = 0x62,
            ["NUMPAD3"] = 0x63,
            ["NUMPAD4"] = 0x64,
            ["NUMPAD5"] = 0x65,
            ["NUMPAD6"] = 0x66,
            ["NUMPAD7"] = 0x67,
            ["NUMPAD8"] = 0x68,
            ["NUMPAD9"] = 0x69,
            ["NUMPADMULTIPLY"] = 0x6A,
            ["NUMPADADD"] = 0x6B,
            ["NUMPADSEPARATOR"] = 0x6C,
            ["NUMPADSUBTRACT"] = 0x6D,
            ["NUMPADDECIMAL"] = 0x6E,
            ["NUMPADDIVIDE"] = 0x6F,
            ["NUMPADENTER"] = 0x0D,
            ["NUMLOCK"] = 0x90,
            ["PRINTSCREEN"] = 0x2C,
            ["SCROLLLOCK"] = 0x91,
            ["PAUSE"] = 0x13,
            ["BREAK"] = 0x13,
            ["CAPSLOCK"] = 0x14,
            ["HELP"] = 0x2F,
            ["VOLUME_MUTE"] = 0xAD,
            ["VOLUME_DOWN"] = 0xAE,
            ["VOLUME_UP"] = 0xAF,
            ["MEDIA_NEXT_TRACK"] = 0xB0,
            ["MEDIA_PREV_TRACK"] = 0xB1,
            ["MEDIA_STOP"] = 0xB2,
            ["MEDIA_PLAY_PAUSE"] = 0xB3,
            ["BROWSER_HOME"] = 0xAC,
            ["BROWSER_SEARCH"] = 0xAA,
            ["BROWSER_FAVORITES"] = 0xAB,
            ["BROWSER_REFRESH"] = 0xA8,
            ["BROWSER_STOP"] = 0xA9,
            ["BROWSER_FORWARD"] = 0xA7,
            ["BROWSER_BACK"] = 0xA6,
            ["LAUNCH_MAIL"] = 0xB4,
            ["LAUNCH_MEDIA_SELECT"] = 0xB5,
            ["LAUNCH_APP1"] = 0xB6,
            ["LAUNCH_APP2"] = 0xB7,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ParseTechKeyList(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return [0x4A];
            var result = new List<byte>();
            foreach (var part in input!.Split([','], StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z') { result.Add((byte)name[0]); continue; }
                if (name.Length == 1 && name[0] >= '0' && name[0] <= '9') { result.Add((byte)name[0]); continue; }
                if (KeyNameToCode.TryGetValue(name, out byte code)) result.Add(code);
            }
            return result.Count == 0 ? [0x4A] : [.. result];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Macro()
        {
            usePerfCounter = QueryPerformanceFrequency(out perfFrequency);
            perfFreqInv = (usePerfCounter && perfFrequency > 0) ? 1.0 / perfFrequency : 1e-7;
            for (int i = 0; i < 256; i++) scanCodeCache[i] = (byte)MapVirtualKey((uint)i, 0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  主线程：每帧写锚点
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Update(scrController controller)
        {
            var settings = Main.Settings;

            if (!settings.Macro || controller?.paused != false ||
                ADOBase.sceneName == GCNS.sceneLevelSelect)
            {
                StopWorkerIfNeeded();
                return;
            }

            EnsureWorkerRunning();

            if (settings.SkyHookMode != skyHookInitialized) SwitchMode(settings.SkyHookMode);
            _cachedHighPrecision = settings.HighPrecisionTime;

            if (!initialized)
            {
                Initialize();
                if (!initialized) return;
            }
            else if (NeedReinitialize())
            {
                ResetState(controller);
                Initialize();
                if (!initialized) return;
            }

            if (Volatile.Read(ref _workerNeedsHit) != 0)
            {
                int hitCount = Interlocked.Exchange(ref _workerNeedsHit, 0);
                for (int h = 0; h < hitCount; h++) controller!.Hit(false);
            }

#if DEBUG
            int lastFloor = Volatile.Read(ref _workerLastTriggeredFloor);
#endif
            float pitch = conductor!.song.pitch;
            double dspSnap = DSPTimeSimulater.GetDSPTime();
            long qpcSnap = GetRawTicks();

            if (pitch != _lastPitch)
            {
                _dspTimeRef = dspSnap;
                _songPosRef = conductor!.songposition_minusi;
                _lastPitch = pitch;
            }

            var anchor = ReferenceEquals(_currentAnchor, _anchorA) ? _anchorB : _anchorA;

            anchor.songPosRef = _songPosRef;
            anchor.dspTimeRef = _dspTimeRef;
            anchor.dspSnapshot = dspSnap;
            anchor.qpcSnapshot = qpcSnap;
            anchor.pitch = pitch;
            anchor.timeOffset = settings.TimeOffset * 0.001;
            anchor.simulateKeyPress = settings.SimulateKeyPress;

            if (anchor.staticVersion != _staticAnchorVersion)
            {
                anchor.hitEvents = _hitEvents;
                anchor.hitEventCount = _hitEventCount;
                anchor.staticVersion = _staticAnchorVersion;
            }

            if (anchor.keyCodesVersion != _keyCodesVersion)
            {
                if (anchor.keyCodesSnapshot.Length != keyCodes.Count)
                    anchor.keyCodesSnapshot = new byte[keyCodes.Count];
                keyCodes.CopyTo(anchor.keyCodesSnapshot, 0);
                anchor.keyCodesVersion = _keyCodesVersion;
            }

            Volatile.Write(ref anchor.validFlag, 1);
            Volatile.Write(ref _currentAnchor, anchor);

            if (!_workerStarted) { _workerStarted = true; _startSignal.Release(); }

#if DEBUG
            Log($"[Macro-Main] 锚点已发布 pitch={pitch} lastFloor={lastFloor}");
#endif
        }

        // ═══════════════════════════════════════════════════════════════
        //  工作线程
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerLoop()
        {
            Log("[Macro-Worker] 工作线程启动");
            timeBeginPeriod(1);
            try
            {
                _startSignal.Wait();
                if (!_workerRunning) return;

                int localLastFloor = Volatile.Read(ref _workerLastTriggeredFloor);
                int localResetVer = Volatile.Read(ref _resetVersion);

                while (_workerRunning)
                {
                    var anchor = Volatile.Read(ref _currentAnchor);

                    if (!anchor.valid || anchor.hitEvents == null || anchor.hitEventCount == 0)
                    {
                        Thread.Sleep(1); continue;
                    }

                    int curResetVer = Volatile.Read(ref _resetVersion);
                    if (curResetVer != localResetVer)
                    {
                        localResetVer = curResetVer;
                        localLastFloor = Volatile.Read(ref _workerLastTriggeredFloor);
                        continue;
                    }

                    var events = anchor.hitEvents;
                    int evCount = anchor.hitEventCount;
                    bool simulateKey = anchor.simulateKeyPress;
                    double timeOffset = anchor.timeOffset;
                    double pitch = anchor.pitch;
                    double songPosRef = anchor.songPosRef;
                    double dspTimeRef = anchor.dspTimeRef;
                    double dspSnapshot = anchor.dspSnapshot;
                    long qpcSnapshot = anchor.qpcSnapshot;

                    int hitCount = 0;
                    bool triggered = false;

                    if (localLastFloor >= evCount - 1)
                    {
                        int ver = Volatile.Read(ref _resetVersion);
                        for (int s = 0; s < 50 && _workerRunning && Volatile.Read(ref _resetVersion) == ver; s++)
                            Thread.Sleep(1);
                        goto WriteBack;
                    }

                    for (int i = localLastFloor + 1; i < evCount;)
                    {
                        if (Volatile.Read(ref _resetVersion) != localResetVer) goto WriteBack;

                        long qpcNow = GetRawTicks();
                        double elapsed = (double)(qpcNow - qpcSnapshot) * perfFreqInv;
                        double audioNow = songPosRef + (dspSnapshot + elapsed - dspTimeRef) * pitch;
                        double triggerAt = events[i].TriggerTime + timeOffset;

                        if (triggerAt > audioNow)
                        {
                            if (pitch <= 0.0) { Thread.Sleep(1); break; }
                            double waitSec = (triggerAt - audioNow) / pitch;
                            if (waitSec > 0.005) { Thread.Sleep(1); break; }
                            else if (waitSec > 0.002) Thread.Yield();
                            continue;
                        }

                        ref readonly var ev = ref events[i];
                        bool enableTechnique = Main.Settings.EnableTechniqueSimulation;

                        if (!simulateKey)
                        {
                            hitCount++;
                            Log($"[Macro-Worker] 请求 Hit() EventIndex={i}");
                        }
                        else if (ev.ReleaseOnly)
                        {
                            if (enableTechnique)
                            {
                                byte keyToRelease = ev.IsHoldRelated
                                    ? (ev.ReleaseKeyCode != 0 ? ev.ReleaseKeyCode : _holdKey)
                                    : ev.ReleaseKeyCode;
                                SendKey(keyToRelease, false);
                                if (ev.IsHoldRelated) { _holdKey = 0; _isHoldDown = false; }
                                Log($"[Macro-Worker] 直接释放 key=0x{keyToRelease:X2} EventIndex={i} audioNow={audioNow:F6}");
                            }
                            else
                            {
                                if (ev.IsHoldRelated) WorkerReleaseHoldKey();
                                else WorkerReleaseKey(ev.ReleaseKeyCode);
                                Log($"[Macro-Worker] 松键(hold={ev.IsHoldRelated} key=0x{ev.ReleaseKeyCode:X2}) EventIndex={i}");
                            }
                        }
                        else if (ev.IsHoldRelated)
                        {
                            if (enableTechnique)
                            {
                                if (_isHoldDown) { SendKey(_holdKey, false); _holdKey = 0; _isHoldDown = false; }
                                SendKey(ev.KeyCode, true);
                                _holdKey = ev.KeyCode; _isHoldDown = true;
                                Log($"[Macro-Worker] 直接长按 0x{ev.KeyCode:X2} EventIndex={i} audioNow={audioNow:F6}");
                            }
                            else
                            {
                                WorkerHoldKey(ev.KeyCode);
                                Log($"[Macro-Worker] Hold 按下 0x{ev.KeyCode:X2} EventIndex={i}");
                            }
                        }
                        else
                        {
                            if (enableTechnique)
                            {
                                SendKey(ev.KeyCode, true);
                                Log($"[Macro-Worker] 直接按下 0x{ev.KeyCode:X2} EventIndex={i} audioNow={audioNow:F6}");
                            }
                            else
                            {
                                WorkerPressKey(ev.KeyCode);
                                Log($"[Macro-Worker] 按下 0x{ev.KeyCode:X2} EventIndex={i}");
                            }
                        }

                        localLastFloor = i++;
                        triggered = true;

                        var fresh = Volatile.Read(ref _currentAnchor);
                        if (!ReferenceEquals(fresh, anchor) && fresh.valid
                            && Volatile.Read(ref _resetVersion) == localResetVer)
                        {
                            anchor = fresh;
                            events = anchor.hitEvents!;
                            evCount = anchor.hitEventCount;
                            dspSnapshot = anchor.dspSnapshot;
                            qpcSnapshot = anchor.qpcSnapshot;
                        }
                    }

                    if (localLastFloor >= evCount - 1)
                    {
                        if (_isKeyDown) WorkerReleaseKey();
                        if (_isHoldDown) WorkerReleaseHoldKey();
                    }

                WriteBack:
                    if (triggered && Volatile.Read(ref _resetVersion) == localResetVer)
                        Volatile.Write(ref _workerLastTriggeredFloor, localLastFloor);
                    if (hitCount > 0)
                        Interlocked.Add(ref _workerNeedsHit, hitCount);
                }
            }
            finally
            {
                timeEndPeriod(1);
                if (Main.Settings.EnableTechniqueSimulation)
                { if (_isHoldDown) SendKey(_holdKey, false); }
                else
                { WorkerReleaseKey(); WorkerReleaseHoldKey(); }
                Log("[Macro-Worker] 工作线程退出");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  按键操作
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerPressKey(byte keyCode)
        {
            if (_isKeyDown && _pendingKey != keyCode) WorkerReleaseKey();
            if (_isHoldDown && _holdKey == keyCode) return;
            if (!_isKeyDown) { SendKey(keyCode, isDown: true); _pendingKey = keyCode; _isKeyDown = true; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerHoldKey(byte keyCode)
        {
            if (_isHoldDown) WorkerReleaseHoldKey();
            SendKey(keyCode, isDown: true);
            _holdKey = keyCode; _isHoldDown = true;
            Log($"[Macro-Worker] Hold 按下 0x{keyCode:X2}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerReleaseHoldKey()
        {
            if (!_isHoldDown) return;
            SendKey(_holdKey, isDown: false);
            _holdKey = 0; _isHoldDown = false;
            Log("[Macro-Worker] Hold 释放");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerReleaseKey(byte targetKey = 0)
        {
            if (!_isKeyDown) return;
            if (targetKey != 0 && _pendingKey != targetKey) return;
            SendKey(_pendingKey, isDown: false);
            _pendingKey = 0; _isKeyDown = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SendKey(byte keyCode, bool isDown)
        {
            if (_cachedSkyHookMode)
            {
                int r = AsyncInputManager.DirectPushKey(keyCode, isDown);
                if (r != 0) Log($"[Macro-Worker] PushKeyEvent 失败 result={r} key=0x{keyCode:X2}");
                Log($"[Macro-Worker] SkyHook direct key=0x{keyCode:X2} down={isDown}");
            }
            else
            {
                _cachedInput.type = INPUT_KEYBOARD;
                _cachedInput.u.ki.wVk = keyCode;
                _cachedInput.u.ki.wScan = scanCodeCache[keyCode];
                _cachedInput.u.ki.dwFlags = isDown ? KEYEVENTF_KEYDOWN : KEYEVENTF_KEYUP;
                fixed (SkyHookSystem.INPUT* ptr = &_cachedInput)
                    SendInput(1, (IntPtr)ptr, sizeof(SkyHookSystem.INPUT));
                Log($"[Macro-Worker] SendInput key=0x{keyCode:X2} down={isDown}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  初始化
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Initialize()
        {
            levelMaker = scrLevelMaker.instance;
            if (levelMaker?.listFloors == null || levelMaker.listFloors.Count == 0) return;

            cachedFloors = [.. levelMaker.listFloors];
            floorCount = cachedFloors.Length;
            conductor = scrConductor.instance;

            ParseKeyCodes();
            BuildHitEvents();

            initialized = true;
            _staticAnchorVersion++;

            _dspTimeRef = DSPTimeSimulater.GetDSPTime();
            long initQpc = GetRawTicks();
            _songPosRef = conductor!.songposition_minusi;
            _lastPitch = conductor.song.pitch;

            _anchorA.dspTimeRef = _anchorB.dspTimeRef = _dspTimeRef;
            _anchorA.songPosRef = _anchorB.songPosRef = _songPosRef;
            _anchorA.dspSnapshot = _anchorB.dspSnapshot = _dspTimeRef;
            _anchorA.qpcSnapshot = _anchorB.qpcSnapshot = initQpc;

            int syncFloor = SyncFloor(_songPosRef);
            Volatile.Write(ref _workerLastTriggeredFloor, syncFloor);

            Log("[Macro-Main] 初始化完成");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildHitEvents()
        {
            if (Main.Settings.SimulateKeyPress && Main.Settings.EnableTechniqueSimulation)
            {
                BuildTechniqueHitEvents();
                return;
            }

            var floors = cachedFloors!;
            int n = floors.Length;
            bool simulate = Main.Settings.SimulateKeyPress;

            byte[] keys = [.. keyCodes];
            int keyLen = keys.Length;
            int keyIdx = 0;

            var events = new List<HitEvent>(n);

            for (int i = 0; i < n - 1; i++)
            {
                var floor = floors[i];
                if (floor == null) continue;
                if ((floor.nextfloor != null && floor.nextfloor.auto) || floor.midSpin) continue;

                double t = floors[i + 1]?.entryTime ?? double.MaxValue;

                if (simulate && floor.holdLength > -1 && i + 1 < n)
                {
                    var nf = floors[i + 1];
                    if (nf != null && nf.holdLength == -1) { events.Add(new HitEvent(t, 0, releaseOnly: true)); continue; }
                }

                byte key = keys[keyIdx];
                if (++keyIdx >= keyLen) keyIdx = 0;
                events.Add(new HitEvent(t, key, releaseOnly: false));
            }

            _hitEvents = [.. events];
            _hitEventCount = _hitEvents.Length;
            Log($"[Macro-Main] BuildHitEvents 完成，共 {_hitEventCount} 个事件");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SyncFloor(double currentTime)
        {
            if (_hitEvents == null || _hitEvents.Length == 0) return -1;
            int left = 0, right = _hitEvents.Length - 1;
            while (left <= right)
            {
                int mid = (left + right) >> 1;
                double t = _hitEvents[mid].TriggerTime;
                if (t < currentTime) left = mid + 1;
                else if (t > currentTime) right = mid - 1;
                else return mid;
            }
            return left - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedReinitialize() => levelMaker?.listFloors.Count != floorCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParseKeyCodes()
        {
            string keysSetting = Main.Settings.MacroKeys ?? "J";
            if (keysSetting == lastKeysSetting && keyCodes.Count > 0) return;

            lastKeysSetting = keysSetting;
            keyCodes.Clear();

            foreach (string part in keysSetting.Split([','], StringSplitOptions.RemoveEmptyEntries))
            {
                string keyName = part.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(keyName)) continue;
                if (keyName.Length == 1)
                {
                    char c = keyName[0];
                    if (c is >= 'A' and <= 'Z') { keyCodes.Add((byte)c); continue; }
                    if (c is >= '0' and <= '9') { keyCodes.Add((byte)c); continue; }
                }
                if (KeyNameToCode.TryGetValue(keyName, out byte code)) keyCodes.Add(code);
            }
            if (keyCodes.Count == 0) keyCodes.Add(0x4A);
            _keyCodesVersion++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyHoldBehavior(scrController controller)
        {
            if (controller == null || !Main.Settings.Macro) return;
            bool simulate = Main.Settings.SimulateKeyPress;
            controller.requireHolding = simulate && Persistence.holdBehavior < HoldBehavior.NoHoldNeeded;
        }

        // ═══════════════════════════════════════════════════════════════
        //  手法模拟：解析全局配置
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParseTechniqueConfig()
        {
            var s = Main.Settings;
            _techLeftKeys = ParseTechKeyList(s.TechLeftHandKeys);
            _techRightKeys = ParseTechKeyList(s.TechRightHandKeys);
            _techKeyOrders[0] = ParseTechOrders(s.TechLeftHandOrders, _techLeftKeys.Length);
            _techKeyOrders[1] = ParseTechOrders(s.TechRightHandOrders, _techRightKeys.Length);
            _techPressDur[0] = ParseTechPressTimes(s.TechLeftHandPressTimes, _techLeftKeys.Length);
            _techPressDur[1] = ParseTechPressTimes(s.TechRightHandPressTimes, _techRightKeys.Length);

            var profiles = s.TechniqueProfiles;
            if (profiles != null && profiles.Count > 0 &&
                s.SelectedTechniqueProfileIndex >= 0 && s.SelectedTechniqueProfileIndex < profiles.Count)
                _currentSegments = profiles[s.SelectedTechniqueProfileIndex].techniqueSegments;
            else
                _currentSegments = new List<Settings.TechniqueSegment>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int[][] ParseTechOrders(string? input, int keyCount)
        {
            int slots = keyCount;
            var result = new int[slots][];
            for (int n = 0; n < slots; n++) { result[n] = new int[n + 1]; for (int i = 0; i <= n; i++) result[n][i] = i % keyCount; }
            if (string.IsNullOrWhiteSpace(input)) return result;

            string[] groups = input!.Split('|');
            for (int n = 0; n < slots; n++)
            {
                string group = n < groups.Length ? groups[n] : groups[groups.Length - 1];
                var indices = new List<int>();
                foreach (var p in group.Split([','], StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(p.Trim(), out int idx))
                        indices.Add(Math.Max(0, Math.Min(idx - 1, keyCount - 1)));
                if (indices.Count > 0) result[n] = [.. indices];
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[] ParseTechPressTimes(string? input, int keyCount)
        {
            var result = new double[keyCount];
            for (int i = 0; i < result.Length; i++) result[i] = 0.8;
            if (string.IsNullOrWhiteSpace(input)) return result;
            var parts = input!.Split([','], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(parts.Length, result.Length); i++)
                if (double.TryParse(parts[i].Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    result[i] = v;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetAdviceBpm(double limit)
        {
            double bpm = (double)(conductor!.bpm * ADOBase.controller.speed * conductor.song.pitch);
            while (bpm > limit) bpm /= 2.0;
            while (bpm <= limit / 2.0) bpm *= 2.0;
            return bpm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GetAdviceBpm() => GetAdviceBpm(Main.Settings.TechniqueBpmLimit);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetSegmentBpmLimit(int floorIdx)
        {
            if (_currentSegments != null)
                foreach (var seg in _currentSegments)
                    if (floorIdx >= seg.startFloor && floorIdx <= seg.endFloor)
                        return seg.bpmLimit;
            return Main.Settings.TechniqueBpmLimit;
        }

        // ─────────────────────────────────────────────
        //  手法模拟：分段有效配置（C# 调试路径用）
        // ─────────────────────────────────────────────
        private readonly struct EffectiveTechConfig
        {
            public readonly byte[] LeftKeys;
            public readonly byte[] RightKeys;
            public readonly int[][] LeftOrders;
            public readonly int[][] RightOrders;
            public readonly double[] LeftPressTimes;
            public readonly double[] RightPressTimes;

            public EffectiveTechConfig(byte[] lk, byte[] rk,
                int[][] lo, int[][] ro, double[] lp, double[] rp)
            {
                LeftKeys = lk; RightKeys = rk;
                LeftOrders = lo; RightOrders = ro;
                LeftPressTimes = lp; RightPressTimes = rp;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EffectiveTechConfig GetEffectiveConfig(int floorIdx)
        {
            if (_currentSegments != null)
            {
                foreach (var seg in _currentSegments)
                {
                    if (floorIdx < seg.startFloor || floorIdx > seg.endFloor) continue;
                    if (!seg.HasKeyOverride) break; // BPM only — keys fall through to global

                    byte[] lk = string.IsNullOrWhiteSpace(seg.leftHandKeys)
                        ? _techLeftKeys
                        : ParseTechKeyList(seg.leftHandKeys);
                    byte[] rk = string.IsNullOrWhiteSpace(seg.rightHandKeys)
                        ? _techRightKeys
                        : ParseTechKeyList(seg.rightHandKeys);

                    int[][] lo = string.IsNullOrWhiteSpace(seg.leftHandOrders)
                        ? _techKeyOrders[0]
                        : ParseTechOrders(seg.leftHandOrders, lk.Length);
                    int[][] ro = string.IsNullOrWhiteSpace(seg.rightHandOrders)
                        ? _techKeyOrders[1]
                        : ParseTechOrders(seg.rightHandOrders, rk.Length);

                    double[] lp = string.IsNullOrWhiteSpace(seg.leftHandPressTimes)
                        ? _techPressDur[0]
                        : ParseTechPressTimes(seg.leftHandPressTimes, lk.Length);
                    double[] rp = string.IsNullOrWhiteSpace(seg.rightHandPressTimes)
                        ? _techPressDur[1]
                        : ParseTechPressTimes(seg.rightHandPressTimes, rk.Length);

                    return new EffectiveTechConfig(lk, rk, lo, ro, lp, rp);
                }
            }
            return new EffectiveTechConfig(
                _techLeftKeys, _techRightKeys,
                _techKeyOrders[0], _techKeyOrders[1],
                _techPressDur[0], _techPressDur[1]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildTechniqueHitEvents()
        {
            ParseTechniqueConfig();

            var floors = cachedFloors!;
            bool sim = Main.Settings.SimulateKeyPress;

            var evTime = new List<double>(floors.Length);
            var evPress = new List<int>(floors.Length);
            var evFloor = new List<int>(floors.Length);

            for (int i = 0; i < floors.Length - 1; i++)
            {
                var fl = floors[i];
                if (fl == null) continue;
                if ((fl.nextfloor?.auto ?? false) || fl.midSpin) continue;

                var nf = floors[i + 1];
                double t = nf?.entryTime ?? double.MaxValue;

                if (sim && fl.holdLength > -1 && nf != null && nf.holdLength == -1)
                {
                    evTime.Add(t); evPress.Add(-1); evFloor.Add(i);
                    continue;
                }

                bool isHoldHead = sim && nf != null && nf.holdLength > -1;
                evTime.Add(t);
                evPress.Add(isHoldHead ? 2 : 1);
                evFloor.Add(i);
            }

            int total = evTime.Count;
            if (total == 0) { _hitEvents = []; _hitEventCount = 0; return; }

#if DEBUG
            bool useCppVersion = Main.Settings.UseCppTechniqueInDebug;
#else
            bool useCppVersion = true;
#endif
            if (useCppVersion)
            {
                try
                {
                    var currentProfile = Main.Settings.TechniqueProfiles[Main.Settings.SelectedTechniqueProfileIndex];
                    var segments = currentProfile.techniqueSegments.ToArray();

                    TechniqueSimulator.UpdateConfig(
                        _techLeftKeys, _techRightKeys,
                        _techKeyOrders[0], _techKeyOrders[1],
                        _techPressDur[0], _techPressDur[1],
                        Main.Settings.TechniqueBpmLimit,
                        Main.Settings.TechniqueHandPreference,
                        segments);

                    if (TechniqueSimulator.BuildHitEvents(
                            [.. evTime], [.. evPress], [.. evFloor],
                            total,
                            conductor!.bpm, ADOBase.controller.speed, conductor.song.pitch,
                            out var nativeEvents))
                    {
                        _hitEvents = nativeEvents;
                        _hitEventCount = nativeEvents!.Length;
                        Log($"[Macro-Main] C++ 手法模拟（原生分段）完成：{_hitEventCount} 事件");
                        return;
                    }
                }
                catch (Exception ex) { Log($"[Macro-Main] C++ 手法模拟异常: {ex.Message}，回退 C# 版本"); }
            }

#if DEBUG
            BuildCSHarpTechniqueHitEvents();
#endif
        }

#if DEBUG
        // ═══════════════════════════════════════════════════════════════
        //  C# 回退路径（DEBUG only）
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildCSHarpTechniqueHitEvents()
        {
            ParseTechniqueConfig();

            var  floors  = cachedFloors!;
            bool sim     = Main.Settings.SimulateKeyPress;

            var evTime  = new List<double>(floors.Length);
            var evPress = new List<int>(floors.Length);
            var evFloor = new List<int>(floors.Length);

            for (int i = 0; i < floors.Length - 1; i++)
            {
                var fl = floors[i];
                if (fl == null) continue;
                if ((fl.nextfloor?.auto ?? false) || fl.midSpin) continue;

                var    nf = floors[i + 1];
                double t  = nf?.entryTime ?? double.MaxValue;

                if (sim && fl.holdLength > -1 && nf != null && nf.holdLength == -1)
                {
                    evTime.Add(t); evPress.Add(-1); evFloor.Add(i);
                    continue;
                }

                bool isHoldHead = sim && nf != null && nf.holdLength > -1;
                evTime.Add(t);
                evPress.Add(isHoldHead ? 2 : 1);
                evFloor.Add(i);
            }

            int total = evTime.Count;
            if (total == 0) { _hitEvents = []; _hitEventCount = 0; return; }

            var pieces = new List<PieceInfo>(total);
            BuildPieces(evTime, evPress, evFloor, total, pieces);

            if (pieces.Count > 0)
            {
                var lp = pieces[pieces.Count - 1];
                pieces.Add(new PieceInfo(0, 1 - lp.Hand, lp.PieceLen,
                                         lp.EndTime, lp.EndTime + lp.PieceLen, total));
            }

            var output = GenerateHitEventsFromPieces(evTime, evPress, evFloor, pieces, sim);
            FixSameKeyOverlaps(output);

            _hitEvents     = [.. output];
            _hitEventCount = _hitEvents.Length;
            Log($"[Macro-Main] C# 手法模拟完成：{_hitEventCount} 事件，{pieces.Count} 时间片");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildPieces(
            List<double> evTime, List<int> evPress, List<int> evFloor,
            int total, List<PieceInfo> pieces)
        {
            double nowT    = 0.0;
            int    nowD    = 0;
            int    cHand   = (Main.Settings.TechniqueHandPreference == 0) ? -1 : 1;
            int    mult    = 0;
            double changeTol = 0;

            var mCnt    = new long[16];
            var mCntPre = new long[16];
            int  canMulti  = 0;
            bool needBack  = false;

            float  lastSegLimit = GetSegmentBpmLimit(evFloor[0]);
            double nowBpm       = GetAdviceBpm(lastSegLimit);

            while (nowD < total)
            {
                int   curFloorIdx = evFloor[nowD];
                float curSegLimit = GetSegmentBpmLimit(curFloorIdx);

                if (Math.Abs(curSegLimit - lastSegLimit) > 1e-6f)
                {
                    mult = 0;
                    Array.Clear(mCnt,    0, mCnt.Length);
                    Array.Clear(mCntPre, 0, mCntPre.Length);
                    lastSegLimit = curSegLimit;
                    nowBpm       = GetAdviceBpm(curSegLimit);
                }

                if (pieces.Count > total * 64) break;

                double pLen = 60.0 / (nowBpm * Math.Pow(2, mult)) / 2.0;
                if (pLen < 1e-9) pLen = 1e-9;

                if (changeTol > 1e-6 && nowD < total)
                {
                    int tryCnt = CountEventsInRange(evTime, nowD, nowT + pLen * (0.995 - changeTol));
                    if (tryCnt > 0)
                    {
                        double nextT = evTime[nowD + tryCnt];
                        double diff  = Math.Abs(nextT - nowT - pLen);
                        if (diff > pLen * 0.001 && diff < pLen * changeTol)
                        {
                            double span = nextT - evTime[nowD];
                            if (span > 1e-9) { nowBpm *= pLen / span; continue; }
                        }
                    }
                }

                int cnt   = CountEventsInRange(evTime, nowD, nowT + pLen * 0.995);
                int csH   = (cHand == 1) ? 1 : 0;

                // 使用分段有效配置来确定当前手的最大按键数
                var   ec   = GetEffectiveConfig(curFloorIdx);
                int   maxK = (csH == 0) ? ec.LeftKeys.Length : ec.RightKeys.Length;

                int  mainHand  = (Main.Settings.TechniqueHandPreference == 0) ? -1 : 1;
                bool isOffHand = (cHand != mainHand);

                if (cnt > maxK)
                {
                    if (canMulti == 1 && isOffHand) needBack = true;
                    if (mult < 7) { mult++; mCnt[mult] = 0; continue; }
                    else           cnt = maxK;
                }

                if (needBack && pieces.Count > 0)
                {
                    needBack = false;
                    cHand    = mainHand;
                    var prev = pieces[pieces.Count - 1];
                    nowT = prev.StartTime;
                    nowD = prev.EvStart;
                    Array.Copy(mCntPre, mCnt, 16);
                    mult = prev.Multiplier + 1;
                    if (mult > 7) mult = 7;
                    pieces.RemoveAt(pieces.Count - 1);
                    canMulti = 0;
                    continue;
                }

                Array.Copy(mCnt, mCntPre, 16);
                pieces.Add(new PieceInfo(cnt, csH, pLen, nowT, nowT + pLen, nowD, mult));

                for (int c = mult; c > 0; c--)
                {
                    mCnt[c] += (long)Math.Pow(2, 16 - (mult - c));
                    mCnt[c] %= (1L << 18);
                }
                while (mult > 0 && mCnt[mult] == 0) mult--;

                nowD += cnt;
                nowT += pLen;
                cHand = -cHand;
                canMulti = 1;

                if (nowD < total && Math.Abs(evTime[nowD] - nowT) < pLen * 0.01)
                    nowT = evTime[nowD];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<HitEvent> GenerateHitEventsFromPieces(
            List<double> evTime, List<int> evPress, List<int> evFloor,
            List<PieceInfo> pieces, bool sim)
        {
            int total  = evTime.Count;
            var output = new List<HitEvent>(total * 2);

            bool          activeHold    = false;
            byte          activeHoldKey = 0;

            for (int pcnt = 0; pcnt < pieces.Count - 1; pcnt++)
            {
                var    cur    = pieces[pcnt];
                var    next   = pieces[pcnt + 1];
                double pStart = (pcnt > 0) ? pieces[pcnt - 1].EndTime : 0.0;

                for (int i = 0; i < cur.EvCount; i++)
                {
                    int    idx   = cur.EvStart + i;
                    int    press = evPress[idx];
                    double t     = evTime[idx];

                    if (press == -1)
                    {
                        if (activeHold)
                        {
                            output.Add(new HitEvent(t, 0, releaseOnly: true,
                                isHoldRelated: true, releaseKeyCode: activeHoldKey));
                            activeHold = false; activeHoldKey = 0;
                        }
                        continue;
                    }

                    // 按当前事件的地板索引解析有效键位
                    int curFloor = (idx < evFloor.Count) ? evFloor[idx] : evFloor[evFloor.Count - 1];
                    var ec       = GetEffectiveConfig(curFloor);

                    byte[]   hK = (cur.Hand == 0) ? ec.LeftKeys        : ec.RightKeys;
                    int[][]  hO = (cur.Hand == 0) ? ec.LeftOrders       : ec.RightOrders;
                    double[] hT = (cur.Hand == 0) ? ec.LeftPressTimes   : ec.RightPressTimes;

                    int oi = Math.Min(cur.EvCount - 1, hK.Length - 1);
                    int ki = (i < hO[oi].Length) ? hO[oi][i] : (i % hK.Length);
                    ki = Mathf.Clamp(ki, 0, hK.Length - 1);

                    byte   kc          = hK[ki];
                    double ratio       = (ki < hT.Length) ? hT[ki] : 0.8;
                    bool   isHoldHead  = (press == 2);

                    if (isHoldHead && activeHold)
                    {
                        output.Add(new HitEvent(t - 0.000001, 0, releaseOnly: true,
                            isHoldRelated: true, releaseKeyCode: activeHoldKey));
                        activeHold = false; activeHoldKey = 0;
                    }

                    output.Add(new HitEvent(t, kc, false, isHoldHead));

                    if (isHoldHead) { activeHold = true; activeHoldKey = kc; }
                    if (!sim || isHoldHead) continue;

                    // 计算松键时间
                    double dur;
                    if (next.PieceLen > cur.PieceLen + 5e-6)
                    {
                        dur = (pStart + cur.PieceLen > cur.EndTime + 5e-6)
                            ? (next.EndTime - t) * ratio / 2.0
                            : (pStart + cur.PieceLen * 2.0 - t) * ratio / 2.0;
                    }
                    else
                    {
                        dur = (pStart + cur.PieceLen + 5e-6 < cur.EndTime)
                            ? (pStart + cur.PieceLen + next.PieceLen - t) * ratio / 2.0
                            : (next.EndTime - t) * ratio / 2.0;
                    }

                    double rel = t + dur;

                    if (next.Hand != cur.Hand || next.EvCount == 0)
                        { if (rel >= next.EndTime) rel = next.EndTime - 1e-6; }
                    else
                        { if (rel >= cur.EndTime)  rel = cur.EndTime  - 1e-6; }

                    if (rel <= t) rel = t + (next.EndTime - t) * 0.4;

                    output.Add(new HitEvent(rel, 0, true, false, releaseKeyCode: kc));
                }
            }

            if (activeHold && pieces.Count > 0)
            {
                double lastTime = pieces[pieces.Count - 1].EndTime;
                output.Add(new HitEvent(lastTime, 0, releaseOnly: true,
                    isHoldRelated: true, releaseKeyCode: activeHoldKey));
            }

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FixSameKeyOverlaps(List<HitEvent> events)
        {
            events.Sort((a, b) => a.TriggerTime.CompareTo(b.TriggerTime));

            int n = events.Count;
            var pending = new Dictionary<byte, int>(8);

            for (int i = 0; i < n; i++)
            {
                var ev = events[i];

                if (ev.ReleaseOnly)
                {
                    if (ev.ReleaseKeyCode != 0) pending.Remove(ev.ReleaseKeyCode);
                    continue;
                }

                byte kc = ev.KeyCode;
                if (kc == 0) continue;

                if (pending.TryGetValue(kc, out int relIdx))
                {
                    var relEv = events[relIdx];
                    if (relEv.TriggerTime >= ev.TriggerTime)
                    {
                        events[relIdx] = new HitEvent(ev.TriggerTime - 1e-6,
                            relEv.KeyCode, releaseOnly: true,
                            isHoldRelated: relEv.IsHoldRelated,
                            releaseKeyCode: relEv.ReleaseKeyCode);
                    }
                    pending.Remove(kc);
                }

                for (int j = i + 1; j < n; j++)
                {
                    var fwd = events[j];
                    if (fwd.ReleaseOnly && fwd.ReleaseKeyCode == kc && !fwd.IsHoldRelated)
                    {
                        pending[kc] = j;
                        break;
                    }
                }
            }

            events.Sort((a, b) => a.TriggerTime.CompareTo(b.TriggerTime));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountEventsInRange(List<double> times, int start, double endTime)
        {
            if (start >= times.Count) return 0;
            int left = start, right = times.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) >> 1;
                if (times[mid] < endTime) left  = mid + 1;
                else                      right = mid - 1;
            }
            return left - start;
        }
#endif

        // ═══════════════════════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Reset(scrController controller) => ResetState(controller);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ResetState(scrController? controller)
        {
            initialized = false;
            _hitEvents = null;
            _hitEventCount = 0;
            cachedFloors = null;
            levelMaker = null;
            conductor = null;

            Volatile.Write(ref _workerLastTriggeredFloor, -1);
            Interlocked.Exchange(ref _workerNeedsHit, 0);
            Volatile.Write(ref _anchorA.validFlag, 0);
            Volatile.Write(ref _anchorB.validFlag, 0);
            Interlocked.Increment(ref _resetVersion);

            if (skyHookInitialized) AsyncInputManager.ClearQueue();
            if (controller != null) ApplyHoldBehavior(controller);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureWorkerRunning()
        {
            if (_workerRunning && _workerThread?.IsAlive == true) return;

            _workerRunning = true;
            _workerStarted = false;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Highest,
                Name = "MacroWorkerThread"
            };
            _workerThread.Start();
            Log("[Macro-Main] 工作线程已启动");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StopWorkerIfNeeded()
        {
            if (!_workerRunning) return;

            if (skyHookInitialized) _cachedSkyHookMode = false;
            _workerRunning = false;

            if (!_workerStarted)
            {
                _workerStarted = true;
                try { _startSignal.Release(); }
                catch (SemaphoreFullException) { }
            }

            _workerThread?.Join(50);

            if (skyHookInitialized)
            {
                AsyncInputManager.Stop();
                skyHookInitialized = false;
            }
            Log("[Macro-Main] 工作线程已停止");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwitchMode(bool useSkyHook)
        {
            if (useSkyHook == skyHookInitialized) return;
            Log($"[Macro-Main] 切换模式: {(useSkyHook ? "SkyHook" : "SendInput")}");

            if (useSkyHook)
            {
                AsyncInputManager.Start();
                if (!AsyncInputManager.IsInitialized)
                {
                    Log("[Macro-Main] SkyHook 启动失败，回退到 SendInput");
                    Main.Settings.SkyHookMode = false; return;
                }
                skyHookInitialized = true;
                _cachedSkyHookMode = true;
                Main.Settings.SkyHookMode = true;
            }
            else
            {
                _cachedSkyHookMode = false;
                AsyncInputManager.Stop();
                skyHookInitialized = false;
                Main.Settings.SkyHookMode = false;
            }
        }

        // ─────────────────────────────────────────────
        //  计时器
        // ─────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetRawTicks()
        {
            if (_cachedHighPrecision) return DSPTimeSimulater.GetDSPTimeAsFileTime();
            return GetTicks();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetTicks()
        {
            if (usePerfCounter && QueryPerformanceCounter(out long c)) return c;
            return DateTime.UtcNow.Ticks;
        }

        // ─────────────────────────────────────────────
        //  输入调整
        // ─────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HandleInput()
        {
            if (!Main.Settings.Macro ||
                ADOBase.sceneName == GCNS.sceneLevelSelect ||
                ADOBase.controller.paused) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Main.Settings.EnableKeyAdjust)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Main.Settings.AdjustStep = Mathf.Clamp(Main.Settings.AdjustStep - 0.1f, 0.1f, 10f);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) Main.Settings.AdjustStep = Mathf.Clamp(Main.Settings.AdjustStep + 0.1f, 0.1f, 10f);
            }
            else if (!ctrl && Main.Settings.EnableArrowTimeAdjust)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Main.Settings.TimeOffset -= Main.Settings.AdjustStep;
                else if (Input.GetKeyDown(KeyCode.RightArrow)) Main.Settings.TimeOffset += Main.Settings.AdjustStep;
            }
        }

        // ─────────────────────────────────────────────
        //  日志
        // ─────────────────────────────────────────────
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Log(string message)
        {
            bool logToMod = false;
            if (logToMod) Main.Mod?.Logger.Log(message);
        }
    }

    #endregion
#pragma warning restore CS0420
}