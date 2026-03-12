using BaseMacro;
using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Documents;
using UnityEngine;

#nullable enable

namespace BaseMacro.Macro
{
#pragma warning disable CS0420
    #region TimeBasedMacro

    internal static class Macro
    {
        // ─────────────────────────────────────────────
        //  预处理按键事件结构体
        //  在 Initialize() 阶段一次性算好：触发时间、按哪个键、是否只松键
        //  工作线程热路径不再碰任何 floor 对象
        // ─────────────────────────────────────────────
        private readonly struct HitEvent(double triggerTime, byte keyCode, bool releaseOnly,
                                          bool isHoldRelated = false, byte releaseKeyCode = 0)
        {
            public readonly double TriggerTime = triggerTime;
            public readonly byte KeyCode = keyCode;
            public readonly bool ReleaseOnly = releaseOnly;
            public readonly bool IsHoldRelated = isHoldRelated;
            // 释放事件时指定要释放哪个键，0 = 释放当前持有键（旧行为）
            public readonly byte ReleaseKeyCode = releaseKeyCode;
        }

        private readonly struct PieceInfo(int ec, int h, double pl, double st, double et, int es)
        {
            public readonly int EvCount = ec;
            public readonly int Hand = h;       // 0=左, 1=右
            public readonly double PieceLen = pl;
            public readonly double StartTime = st;
            public readonly double EndTime = et;
            public readonly int EvStart = es;
        }

        // ─────────────────────────────────────────────
        //  主线程专属数据
        // ─────────────────────────────────────────────
        private static scrLevelMaker? levelMaker;
        private static scrConductor? conductor;
        private static scrFloor[]? cachedFloors;
        private static bool initialized = false;
        private static string lastKeysSetting = "";
        private static readonly List<byte> keyCodes = new(4);
        private static int _keyCodesVersion = 0;

        // ─────────────────────────────────────────────
        //  只读共享数据（初始化后不变）
        // ─────────────────────────────────────────────
        private static HitEvent[]? _hitEvents;
        private static int _hitEventCount;
        private static int floorCount;

        // ─────────────────────────────────────────────
        //  时间锚点
        //
        //  时钟架构（三层）：
        //
        //  层1 - DSPTimeSimulater（主线程独占）
        //        每帧做漂移修正，提供长期精度。但其字段（m_dspTime/m_lastTime）是
        //        普通 static double，工作线程直接调用 GetDSPTime() 是数据竞争：
        //        32-bit Mono 下 double 读写非原子 → 撕裂读。
        //        + GetDSPTime() 内部每次调用 BaseSelect.GetFileTime()（Win32 系统调用），
        //          在工作线程热路径里比 QPC 慢 5 倍以上。
        //
        //  层2 - QPC（工作线程插值）
        //        主线程在采 DSP 快照后立刻采 QPC，一起写入 anchor。
        //        工作线程只做 (QPC_now - qpcSnapshot) * perfFreqInv，零系统调用，~20ns。
        //
        //  层3 - anchor 双缓冲（发布协议）
        //        主线程写 inactive anchor，Volatile.Write 发布，工作线程读。
        //
        //  公式：
        //    audioNow = songPosRef
        //             + (dspSnapshot + (QPC_now - qpcSnapshot) * perfFreqInv - dspTimeRef) * pitch
        //
        //  好处：DSP 长期校准 + QPC 帧内精度 + 零竞争
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

            // 预处理事件表（替换原来的 triggerTimes + floors）
            public HitEvent[]? hitEvents;
            public int hitEventCount;

            public byte[] keyCodesSnapshot;
            public int keyCodesVersion;

            // valid 改为字段（引用类型字段），以支持 Volatile.Write
            public int validFlag;   // 0=false, 1=true（int 可 Volatile 操作）
            // 静态数据版本号，跳过热帧对不变字段的重复赋值
            public int staticVersion;

            public bool valid
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

        // 静态数据版本号（hitEvents），reinit 时递增，
        // 避免主线程热帧每次都重写不变字段
        private static int _staticAnchorVersion = 0;

        // ─────────────────────────────────────────────
        //  OPT-1: 预计算 QPC 频率倒数，将内层热路径除法 (~5-10ns) 改为乘法 (~1-2ns)
        //         同时消除 usePerfCounter 分支判断。
        //         perfFrequency == 0 时（QueryPerformanceFrequency 失败）回退 100ns 单位。
        // ─────────────────────────────────────────────
        private static readonly double perfFreqInv;

        // ─────────────────────────────────────────────
        //  工作线程 → 主线程反馈
        // ─────────────────────────────────────────────
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

        // 长按键独立追踪（与 _pendingKey/_isKeyDown 完全隔离）
        private static byte _holdKey;
        private static bool _isHoldDown;

        private static volatile bool _cachedSkyHookMode = false;

        // OPT-2: 缓存 HighPrecisionTime 设置，避免热路径每次解引用 Main.Settings 对象
        private static volatile bool _cachedHighPrecision = false;

        private static volatile bool skyHookInitialized = false;

        // ─────────────────────────────────────────────
        //  Win32
        // ─────────────────────────────────────────────
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0;
        private const uint KEYEVENTF_KEYUP = 2;

        // ─────────────────────────────────────────────
        //  手法模拟数据（ParseTechniqueConfig 填充）
        // ─────────────────────────────────────────────
        private static byte[] _techLeftKeys = [];
        private static byte[] _techRightKeys = [];
        // [hand][keyCount-1][i] → 第 i 次按键使用的键下标
        private static int[][][] _techKeyOrders = { [], [] };
        // [hand][keyIndex] → 按下时长占比(0~1)
        private static double[][] _techPressDur = { [], [] };

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ParseTechKeyList(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return [0x4A]; // 默认返回 'J'

            var result = new List<byte>();
            foreach (var part in input!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = part.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(name))
                    continue;

                // 单个字母或数字直接使用 ASCII 码
                if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z')
                {
                    result.Add((byte)name[0]);
                    continue;
                }
                if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
                {
                    result.Add((byte)name[0]);
                    continue;
                }

                // 其他按键从字典中查找
                if (KeyNameToCode.TryGetValue(name, out byte code))
                    result.Add(code);
            }

            return result.Count == 0 ? [0x4A] : [.. result];
        }

        private static readonly Dictionary<string, byte> KeyNameToCode = new()
        {
            // 字母（已由单字符逻辑处理，但保留以支持别名或扩展）
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

            // 数字（同样可由单字符处理，保留字典便于统一）
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

            // 基础符号键（无需 Shift）
            ["`"] = 0xC0, // VK_OEM_3
            ["-"] = 0xBD, // VK_OEM_MINUS
            ["="] = 0xBB, // VK_OEM_PLUS
            ["["] = 0xDB, // VK_OEM_4
            ["]"] = 0xDD, // VK_OEM_6
            ["\\"] = 0xDC, // VK_OEM_5
            [";"] = 0xBA, // VK_OEM_1
            ["'"] = 0xDE, // VK_OEM_7
            [","] = 0xBC, // VK_OEM_COMMA
            ["."] = 0xBE, // VK_OEM_PERIOD
            ["/"] = 0xBF, // VK_OEM_2
            [" "] = 0x20, // SPACE

            // 功能键
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

            // 控制键
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
            ["MENU"] = 0x5D,   // 右键菜单键

            // 导航键
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

            // 编辑键
            ["BACKSPACE"] = 0x08,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["SPACE"] = 0x20,
            ["SPACEBAR"] = 0x20,

            // 小键盘
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
            ["NUMPADENTER"] = 0x0D, // 与主键盘 Enter 相同，可根据需要区分
            ["NUMLOCK"] = 0x90,

            // 其他常用键
            ["PRINTSCREEN"] = 0x2C,
            ["SCROLLLOCK"] = 0x91,
            ["PAUSE"] = 0x13,
            ["BREAK"] = 0x13,
            ["CAPSLOCK"] = 0x14,
            ["HELP"] = 0x2F,

            // 多媒体键（可选）
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
        static Macro()
        {
            usePerfCounter = QueryPerformanceFrequency(out perfFrequency);

            // OPT-1: 预计算倒数，后续所有 QPC 时间换算均用乘法
            perfFreqInv = (usePerfCounter && perfFrequency > 0)
                ? 1.0 / perfFrequency
                : 1e-7;  // 100ns 单位回退

            for (int i = 0; i < 256; i++)
                scanCodeCache[i] = (byte)MapVirtualKey((uint)i, 0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  主线程：每帧只写锚点，不做触发决策
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

            if (settings.SkyHookMode != skyHookInitialized)
                SwitchMode(settings.SkyHookMode);

            // OPT-2: 每帧同步缓存，避免工作线程热路径解引用 Main.Settings
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

            // 先 volatile 读做快速路径，避免每帧无条件执行 Interlocked 全栅障
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

            // 每帧变化的时钟字段
            anchor.songPosRef = _songPosRef;
            anchor.dspTimeRef = _dspTimeRef;
            anchor.dspSnapshot = dspSnap;
            anchor.qpcSnapshot = qpcSnap;
            anchor.pitch = pitch;
            anchor.timeOffset = settings.TimeOffset * 0.001;
            anchor.simulateKeyPress = settings.SimulateKeyPress;

            // 静态数据（HitEvent 表）：只在版本号变化时更新
            if (anchor.staticVersion != _staticAnchorVersion)
            {
                anchor.hitEvents = _hitEvents;
                anchor.hitEventCount = _hitEventCount;
                anchor.staticVersion = _staticAnchorVersion;
            }

            // keyCodesSnapshot 已在 BuildHitEvents 时固化到 HitEvent.KeyCode，
            // 这里仍保留快照用于将来可能的动态键位切换检测
            if (anchor.keyCodesVersion != _keyCodesVersion)
            {
                if (anchor.keyCodesSnapshot.Length != keyCodes.Count)
                    anchor.keyCodesSnapshot = new byte[keyCodes.Count];
                keyCodes.CopyTo(anchor.keyCodesSnapshot, 0);
                anchor.keyCodesVersion = _keyCodesVersion;
            }

            // valid 通过 Volatile.Write 写 int 字段，保证工作线程读到正确值
            // 且写入顺序在 _currentAnchor 发布之前（防 CPU 乱序）
            Volatile.Write(ref anchor.validFlag, 1);
            Volatile.Write(ref _currentAnchor, anchor);

            if (!_workerStarted)
            {
                _workerStarted = true;
                _startSignal.Release();
            }

#if DEBUG
            Log($"[Macro-Main] 锚点已发布 pitch={pitch} lastFloor={lastFloor}");
#endif
        }

        // ═══════════════════════════════════════════════════════════════
        //  工作线程：自旋 + 精确计时触发
        //
        //  预处理后热路径彻底消除了：
        //    · floor 对象读取
        //    · auto / midSpin 判断
        //    · holdLength 判断
        //    · localKeyIndex 轮换
        //  工作线程只做：等待到时间 → 按键 / 松键
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
                        Thread.Sleep(1);
                        continue;
                    }

                    // 检测重置
                    int curResetVer = Volatile.Read(ref _resetVersion);
                    if (curResetVer != localResetVer)
                    {
                        localResetVer = curResetVer;
                        localLastFloor = Volatile.Read(ref _workerLastTriggeredFloor);
                        continue;
                    }

                    // 从 anchor 拷贝热路径所需局部变量（消除重复 volatile 读）
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

                    // 全部事件已触发，空转等待重置
                    if (localLastFloor >= evCount - 1)
                    {
                        int ver = Volatile.Read(ref _resetVersion);
                        for (int s = 0; s < 50 && _workerRunning
                             && Volatile.Read(ref _resetVersion) == ver; s++)
                            Thread.Sleep(1);
                        goto WriteBack;
                    }

                    // ── 核心触发循环 ──────────────────────────────────────────
                    // i 只在成功触发后递增，对同一事件反复重入实现精确等待。
                    for (int i = localLastFloor + 1; i < evCount; /* i++ 在触发后 */)
                    {
                        if (Volatile.Read(ref _resetVersion) != localResetVer)
                            goto WriteBack;

                        // ── 计算当前音频时间（~20ns，无系统调用）──
                        long qpcNow = GetRawTicks();
                        double elapsed = (double)(qpcNow - qpcSnapshot) * perfFreqInv;
                        double audioNow = songPosRef + (dspSnapshot + elapsed - dspTimeRef) * pitch;

                        double triggerAt = events[i].TriggerTime + timeOffset;

                        if (triggerAt > audioNow)
                        {
                            // 还没到时间：按等待长度选择等待策略
                            if (pitch <= 0.0) { Thread.Sleep(1); break; }
                            double waitSec = (triggerAt - audioNow) / pitch;

                            if (waitSec > 0.005) { Thread.Sleep(1); break; } // 回外层刷新 anchor
                            else if (waitSec > 0.002) Thread.Yield();              // 让出 CPU 片
                            // else: 纯自旋，直到到时间
                            continue;
                        }

                        // ── 到时间，执行按键 ─────────────────────────────────
                        ref readonly var ev = ref events[i];

                        // 在 WorkerLoop 的核心循环中，处理事件时区分模式
                        bool enableTechnique = Main.Settings.EnableTechniqueSimulation; // 手法模拟模式标志

                        if (!simulateKey)
                        {
                            // 不模拟物理按键，只计数 Hit
                            hitCount++;
                            Log($"[Macro-Worker] 请求 Hit() EventIndex={i}");
                        }
                        else if (ev.ReleaseOnly)
                        {
                            if (enableTechnique)
                            {
                                // 手法模拟：直接发送松开事件
                                byte keyToRelease = ev.IsHoldRelated
                                    ? (ev.ReleaseKeyCode != 0 ? ev.ReleaseKeyCode : _holdKey)   // 长按释放：优先用事件指定键，否则用记录的 _holdKey
                                    : ev.ReleaseKeyCode;                                         // 普通键释放
                                SendKey(keyToRelease, false);

                                if (ev.IsHoldRelated)
                                {
                                    // 清除长按状态
                                    _holdKey = 0;
                                    _isHoldDown = false;
                                }
                                Log($"[Macro-Worker] 直接释放 key=0x{keyToRelease:X2} EventIndex={i} audioNow={audioNow:F6}");
                            }
                            else
                            {
                                // 普通模式：使用原有的状态管理函数
                                if (ev.IsHoldRelated)
                                    WorkerReleaseHoldKey();
                                else
                                    WorkerReleaseKey(ev.ReleaseKeyCode);
                                Log($"[Macro-Worker] 松键(hold={ev.IsHoldRelated} key=0x{ev.ReleaseKeyCode:X2}) EventIndex={i}");
                            }
                        }
                        else if (ev.IsHoldRelated)
                        {
                            if (enableTechnique)
                            {
                                // 强制释放上一个长按（解决连续长按顺序问题）
                                if (_isHoldDown)
                                {
                                    byte oldKey = _holdKey;
                                    SendKey(oldKey, false);
                                    _holdKey = 0;
                                    _isHoldDown = false;
                                    Log($"[Macro-Worker] 强制释放上一个长按 0x{oldKey:X2}");
                                }

                                // 按下新的长按键并记录状态
                                SendKey(ev.KeyCode, true);
                                _holdKey = ev.KeyCode;
                                _isHoldDown = true;
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
                                // 手法模拟：直接按下普通键，不更新 _pendingKey/_isKeyDown
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

                        // 触发后尝试接收新 anchor（爆发段跨帧时刷新 DSP 基准）
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

                    // 所有事件触发完毕，确保松开最后一个持有键
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
                {
                    // 手法模拟：强制释放所有可能按下的键（保守方案：释放所有配置的键）
                    // 但更好的做法是依赖事件表，这里只释放长按键（因为长按键状态被维护）
                    if (_isHoldDown)
                        SendKey(_holdKey, false);
                    // 普通键没有记录，但事件表应已全部释放，不再额外处理
                }
                else
                {
                    WorkerReleaseKey();
                    WorkerReleaseHoldKey();
                }

                Log("[Macro-Worker] 工作线程退出");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  按键操作
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerPressKey(byte keyCode)
        {
            if (_isKeyDown && _pendingKey != keyCode)
                WorkerReleaseKey();

            // 若当前 hold 键与目标键相同，跳过重复按下
            if (_isHoldDown && _holdKey == keyCode) return;

            if (!_isKeyDown)
            {
                SendKey(keyCode, isDown: true);
                _pendingKey = keyCode;
                _isKeyDown = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerHoldKey(byte keyCode)
        {
            // 上一个长按未释放时先强制释放（防止连续长按叠加）
            if (_isHoldDown) WorkerReleaseHoldKey();
            SendKey(keyCode, isDown: true);
            _holdKey = keyCode;
            _isHoldDown = true;
            Log($"[Macro-Worker] Hold 按下 0x{keyCode:X2}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerReleaseHoldKey()
        {
            if (_isHoldDown)
            {
                SendKey(_holdKey, isDown: false);
                _holdKey = 0;
                _isHoldDown = false;
                Log($"[Macro-Worker] Hold 释放");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WorkerReleaseKey(byte targetKey = 0)
        {
            if (!_isKeyDown) return;
            // targetKey==0 或 匹配当前持有键才释放
            if (targetKey != 0 && _pendingKey != targetKey) return;
            SendKey(_pendingKey, isDown: false);
            _pendingKey = 0;
            _isKeyDown = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SendKey(byte keyCode, bool isDown)
        {
            if (_cachedSkyHookMode)
            {
                int result = AsyncInputManager.DirectPushKey(keyCode, isDown);
                if (result != 0)
                    Log($"[Macro-Worker] PushKeyEvent 失败 result={result} key=0x{keyCode:X2}");
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

            // ── 预处理：一次性构建 HitEvent 表 ────────────────────────
            BuildHitEvents();

            initialized = true;

            // 静态数据版本递增，通知 anchor 更新 hitEvents
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

        /// <summary>
        /// 预处理阶段：遍历所有 floor，计算触发时间、分配按键、判断 hold/release，
        /// 结果写入 _hitEvents[]。工作线程热路径不再访问任何 floor 对象。
        /// </summary>
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

                // auto 拍 / midSpin：游戏自动处理，不需要按键，直接跳过
                if ((floor.nextfloor != null && floor.nextfloor.auto) || floor.midSpin)
                    continue;

                // 触发时间 = 下一拍的 entryTime
                double t = floors[i + 1]?.entryTime ?? double.MaxValue;

                if (simulate && floor.holdLength > -1 && i + 1 < n)
                {
                    var nf = floors[i + 1];
                    if (nf != null && nf.holdLength == -1)
                    {
                        // hold 结束拍：只松键，不分配新 key
                        events.Add(new HitEvent(t, 0, releaseOnly: true));
                        continue;
                    }
                }

                // 普通拍：轮换分配按键
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
        private static bool NeedReinitialize()
        {
            return levelMaker?.listFloors.Count != floorCount;
        }

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
                if (KeyNameToCode.TryGetValue(keyName, out byte code))
                    keyCodes.Add(code);
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
        //  手法模拟：解析配置
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParseTechniqueConfig()
        {
            var s = Main.Settings;

            // 解析左右手按键
            _techLeftKeys = ParseTechKeyList(s.TechLeftHandKeys);
            _techRightKeys = ParseTechKeyList(s.TechRightHandKeys);

            // 确保按键顺序数组大小正确
            _techKeyOrders[0] = ParseTechOrders(s.TechLeftHandOrders, _techLeftKeys.Length);
            _techKeyOrders[1] = ParseTechOrders(s.TechRightHandOrders, _techRightKeys.Length);
            _techPressDur[0] = ParseTechPressTimes(s.TechLeftHandPressTimes, _techLeftKeys.Length);
            _techPressDur[1] = ParseTechPressTimes(s.TechRightHandPressTimes, _techRightKeys.Length);
        }


        /// <summary>
        /// 格式：用 | 分隔不同按键数的方案，每方案内用逗号分隔键序号(1-based)
        /// 例 "1|2,1|1,2,3": 1键时用键[0]；2键时先[1]后[0]；3键时[0][1][2]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int[][] ParseTechOrders(string? input, int keyCount)
        {
            int slots = keyCount;
            var result = new int[slots][];
            for (int n = 0; n < slots; n++)
            {
                result[n] = new int[n + 1];
                for (int i = 0; i <= n; i++) result[n][i] = i % keyCount;
            }
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
        private static double GetAdviceBpm()
        {
            double bpm = (double)(conductor!.bpm * ADOBase.controller.speed * conductor.song.pitch);
            double limit = (double)Main.Settings.TechniqueBpmLimit;
            while (bpm > limit) bpm /= 2.0;
            while (bpm <= limit / 2.0) bpm *= 2.0;
            return bpm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BuildTechniqueHitEvents()
        {
            ParseTechniqueConfig();

            var floors = cachedFloors!;
            bool sim = Main.Settings.SimulateKeyPress;

            // ══ 第一步：收集原始事件 ════════════════════════════════════
            var evTime = new List<double>();
            var evPress = new List<int>();

            for (int i = 0; i < floors.Length - 1; i++)
            {
                var fl = floors[i];
                if (fl == null) continue;
                if ((fl.nextfloor?.auto ?? false) || fl.midSpin) continue;

                var nf = floors[i + 1];
                double t = nf?.entryTime ?? double.MaxValue;

                // hold 尾：当前格正在hold，下一格结束hold
                if (sim && fl.holdLength > -1 && nf != null && nf.holdLength == -1)
                { evTime.Add(t); evPress.Add(-1); continue; }

                // hold 头：下一格开始hold
                bool isHoldHead = sim && nf != null && nf.holdLength > -1;
                evTime.Add(t);
                evPress.Add(isHoldHead ? 2 : 1);
            }

            int total = evTime.Count;
            if (total == 0)
            { _hitEvents = []; _hitEventCount = 0; return; }

            var pieces = new List<PieceInfo>();
            double nowT = 0.0;          // ← 从 0 开始！
            int nowD = 0;
            // 从设置中获取起始手偏好
            int cHand = Main.Settings.TechniqueHandPreference == 0 ? -1 : 1; // 0:左手优先(-1), 1:右手优先(1)
            double nowBpm = GetAdviceBpm();
            int mult = 0;
            var mCnt = new int[8];

            while (nowD < total)
            {
                if (pieces.Count > total * 64) break; // 防死循环守卫

                double pLen = 60.0 / (nowBpm * Math.Pow(2, mult)) / 2.0;
                if (pLen < 1e-9) pLen = 1e-9;

                // 统计本片内的事件数
                int cnt = 0;
                while (cnt + nowD < total && evTime[cnt + nowD] < nowT + pLen * 0.995)
                    cnt++;

                // 乘数检查
                int csH = cHand == 1 ? 1 : 0;
                int maxKeys = csH == 0 ? _techLeftKeys.Length : _techRightKeys.Length;
                if (cnt > maxKeys)
                {
                    mult = Math.Min(mult + 1, 7);
                    mCnt[mult] = 0;
                    continue;
                }

                // 确认时间片
                pieces.Add(new PieceInfo(cnt, csH, pLen, nowT, nowT + pLen, nowD));

                // 乘数降级计数器
                if (mult > 0)
                {
                    mCnt[mult]++;
                    if (mCnt[mult] >= 1 << (mult + 1)) { mCnt[mult] = 0; mult--; }
                }

                nowD += cnt;
                nowT += pLen;
                cHand = -cHand; // 交替手：1→-1→1→...

                // 微小误差矫正
                if (nowD < total && Math.Abs(evTime[nowD] - nowT) < pLen * 0.01)
                    nowT = evTime[nowD];
            }

            // 哨兵片
            if (pieces.Count > 0)
            {
                // 哨兵片，手值用 1 - lp.Hand 即可，反正 EvCount==0 会兜底
                var lp = pieces[pieces.Count - 1];
                pieces.Add(new PieceInfo(0, 1 - lp.Hand, lp.PieceLen,
                    lp.EndTime, lp.EndTime + lp.PieceLen, nowD));
            }

            //   只由 (hand, 本片事件总数, 片内位置i) 决定，跨片不变
            //
            var output = new List<HitEvent>(total * 2);

            for (int pcnt = 0; pcnt < pieces.Count - 1; pcnt++)
            {
                var cur = pieces[pcnt];
                var next = pieces[pcnt + 1];

                // 上一片的 EndTime，无 restart 时等于 cur.StartTime
                double pStart = pcnt > 0 ? pieces[pcnt - 1].EndTime : 0.0;

                for (int i = 0; i < cur.EvCount; i++)
                {
                    int idx = cur.EvStart + i;
                    int press = evPress[idx];
                    double t = evTime[idx];

                    // 我们直接发送松键事件
                    if (press == -1)
                    {
                        output.Add(new HitEvent(t, 0, releaseOnly: true, isHoldRelated: true));
                        continue;
                    }

                    // ── 键位分配──────────────────────────
                    byte[] hK = cur.Hand == 0 ? _techLeftKeys : _techRightKeys;
                    int[][] hO = cur.Hand == 0 ? _techKeyOrders[0] : _techKeyOrders[1];
                    double[] hT = cur.Hand == 0 ? _techPressDur[0] : _techPressDur[1];

                    int oi = Math.Min(cur.EvCount - 1, hO.Length - 1);
                    int ki = (i < hO[oi].Length) ? hO[oi][i] : (i % hK.Length);
                    ki = Mathf.Clamp(ki, 0, hK.Length - 1);

                    byte kc = hK[ki];
                    double ratio = ki < hT.Length ? hT[ki] : 0.8;
                    bool hold = press == 2;

                    output.Add(new HitEvent(t, kc, false, hold));

                    // hold 头不插入定时松键（松键来自 hold 尾事件）
                    if (!sim || hold) continue;

                    double dur;
                    if (next.PieceLen > cur.PieceLen + 5e-6)
                    {
                        // 下一片更慢
                        if (pStart + cur.PieceLen > cur.EndTime + 5e-6)
                            // 当前片被压缩（restart 情况）
                            dur = (next.EndTime - t) * ratio / 2.0;
                        else
                            // 正常情况：(pieceStart + pieceLen*2 - t) * ratio / 2
                            dur = (pStart + cur.PieceLen * 2.0 - t) * ratio / 2.0;
                    }
                    else
                    {
                        // 下一片更快或相同
                        if (pStart + cur.PieceLen + 5e-6 < cur.EndTime)
                            // 当前片被拉长（restart 情况）
                            dur = (pStart + cur.PieceLen + next.PieceLen - t) * ratio / 2.0;
                        else
                            // 正常情况：(nextPiece.endTime - t) * ratio / 2
                            dur = (next.EndTime - t) * ratio / 2.0;
                    }

                    double rel = t + dur;

                    // 边界检查（-1 微秒 → -1e-6 秒）
                    if (next.Hand != cur.Hand || next.EvCount == 0)
                    {
                        // 换手或末尾：松键不超过下一片结束
                        if (rel >= next.EndTime) rel = next.EndTime - 1e-6;
                    }
                    else
                    {
                        // 同手连续：松键不超过本片结束
                        if (rel >= cur.EndTime) rel = cur.EndTime - 1e-6;
                    }

                    // 保底
                    if (rel <= t) rel = t + (next.EndTime - t) * 0.4;

                    output.Add(new HitEvent(rel, 0, true, false, releaseKeyCode: kc));
                }
            }

            output.Sort((a, b) => a.TriggerTime.CompareTo(b.TriggerTime));
            _hitEvents = [.. output];
            _hitEventCount = _hitEvents.Length;

            Log($"[Macro-Main] BuildTechniqueHitEvents 完成：{_hitEventCount} 事件，" +
                $"{pieces.Count} 时间片，advBpm={GetAdviceBpm():F1}");
        }

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

            // valid 改为 Volatile.Write(ref int)，保证写入顺序在 resetVersion 递增之前
            Volatile.Write(ref _anchorA.validFlag, 0);
            Volatile.Write(ref _anchorB.validFlag, 0);

            Interlocked.Increment(ref _resetVersion);

            if (skyHookInitialized)
                AsyncInputManager.ClearQueue();

            if (controller != null)
                ApplyHoldBehavior(controller);
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

            if (skyHookInitialized)
                _cachedSkyHookMode = false;

            _workerRunning = false;

            // FIX-BUG: 用 try-catch 防止 SemaphoreFullException（maxCount=1）
            if (!_workerStarted)
            {
                _workerStarted = true;
                try { _startSignal.Release(); }
                catch (SemaphoreFullException) { /* 信号已满，工作线程会自行醒来 */ }
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
                    Main.Settings.SkyHookMode = false;
                    return;
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

        // ═══════════════════════════════════════════════════════════════
        //  计时器
        //  OPT-2: 读 _cachedHighPrecision 而非每次解引用 Main.Settings
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetRawTicks()
        {
            if (_cachedHighPrecision)
                return DSPTimeSimulater.GetDSPTimeAsFileTime();
            return GetTicks();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long GetTicks()
        {
            if (usePerfCounter && QueryPerformanceCounter(out long c)) return c;
            return DateTime.UtcNow.Ticks;
        }

        // ═══════════════════════════════════════════════════════════════
        //  输入调整
        // ═══════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HandleInput()
        {
            if (!Main.Settings.Macro ||
                ADOBase.sceneName == GCNS.sceneLevelSelect ||
                ADOBase.controller.paused) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Main.Settings.EnableKeyAdjust)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                    Main.Settings.AdjustStep = Mathf.Clamp(Main.Settings.AdjustStep - 0.1f, 0.1f, 10f);
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                    Main.Settings.AdjustStep = Mathf.Clamp(Main.Settings.AdjustStep + 0.1f, 0.1f, 10f);
            }
            else if (!ctrl && Main.Settings.EnableArrowTimeAdjust)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                    Main.Settings.TimeOffset -= Main.Settings.AdjustStep;
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                    Main.Settings.TimeOffset += Main.Settings.AdjustStep;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  日志（仅 DEBUG）
        // ═══════════════════════════════════════════════════════════════
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Log(string message) => Main.Mod?.Logger.Log(message);
    }

    #endregion
#pragma warning restore CS0420
}
