using ADOFAIMacro.Platform;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using static ADOFAIMacro.Macro.SkyHookSystem;

#nullable enable

namespace ADOFAIMacro.Macro
{
    public static class AsyncInputManager
    {
        // ══════════════════════════════════════════════════════
        //  Win32
        // ══════════════════════════════════════════════════════
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint p);

        // ══════════════════════════════════════════════════════
        //  环形缓冲区（保留，供未来批量统计等用途；热路径不再走此路径）
        //  8192 而非 4096：更大的缓冲区在突发场景下减少丢弃
        // ══════════════════════════════════════════════════════
        private const int BUFFER_SIZE = 8192;
        private const int BUFFER_MASK = BUFFER_SIZE - 1;

        private static readonly SkyHookEvent[] _ring = new SkyHookEvent[BUFFER_SIZE];

        private static volatile int _writeIndex = 0;
        private static volatile int _readIndex = 0;

        private static volatile bool _isInitialized = false;

        // 统计（跨线程写用 Interlocked）
        private static long _totalProcessed = 0;
        private static long _totalDropped = 0;

        public static bool IsInitialized => _isInitialized;

        // ══════════════════════════════════════════════════════
        //  启动
        //
        //  修复：移除 Start() 内的强制 Gen2 GC。
        //  原代码在此处调用三次阻塞式 GC（50~200ms），若在关卡进行中切换模式，
        //  主线程会卡顿导致锚点时间数据失效、音符触发全部偏移。
        //  GC 预热应在模组加载入口（Main.OnLoad）执行一次，远离关卡运行期。
        //
        //  修复：不再启动内部消费者线程。
        //  热路径已改为工作线程直接调用 InputSystem.PushKeyEvent()，
        //  中间的 ring buffer → ConsumeLoop → SpinWait 链路（0~1ms 抖动）已彻底消除。
        //  AsyncInputManager 现在只负责：
        //    ① 初始化 / 清理 C++ InputSystem 资源
        //    ② timeBeginPeriod(1) 保证 Sleep(1) 精度
        //    ③ GC LatencyMode 切换
        //    ④ 统计（可选）
        // ══════════════════════════════════════════════════════
        public static void Start()
        {
            if (_isInitialized)
            {
                Macro.Log("[InputSystem] 已在运行中");
                return;
            }

            try
            {
                // ① 时钟精度 15.6ms → 1ms
                //    工作线程 Sleep(1) 退化时，精度从 15.6ms 变成 1ms
                timeBeginPeriod(1);

                // ② GC 低延迟模式
                //    SustainedLowLatency：允许 Gen0/1 GC，抑制 Gen2 阻塞式 GC
                //    避免 50-200ms Stop-the-World 打断工作线程
                System.Runtime.GCSettings.LatencyMode =
                    System.Runtime.GCLatencyMode.SustainedLowLatency;

                // ③ 重置统计
                _writeIndex = 0;
                _readIndex = 0;
                _totalProcessed = 0;
                _totalDropped = 0;

                // ④ 初始化并启动 C++ 处理层
                InputSystem.StartProcessing();

                _isInitialized = true;

                Macro.Log("[InputSystem] 启动成功（直接调用模式）");
            }
            catch (Exception ex)
            {
                Macro.Log($"[InputSystem] 启动失败: {ex.Message}");
                _isInitialized = false;
                timeEndPeriod(1);
            }
        }

        // ══════════════════════════════════════════════════════
        //  停止
        // ══════════════════════════════════════════════════════
        public static void Stop()
        {
            if (!_isInitialized) return;

            InputSystem.EmergencyStop();
            InputSystem.StopProcessing();

            System.Runtime.GCSettings.LatencyMode =
                System.Runtime.GCLatencyMode.Interactive;
            timeEndPeriod(1);

            _isInitialized = false;
            Macro.Log($"[InputSystem] 已停止 | 处理: {_totalProcessed} | 丢弃: {Interlocked.Read(ref _totalDropped)}");
        }

        // ══════════════════════════════════════════════════════
        //  直接调用入口（热路径，由 Macro 工作线程调用）
        //
        //  修复前：工作线程 → EnqueueEvent → ring buffer → ConsumeLoop → PushKeyEvent
        //          中间 SpinWait 退化 Sleep(1) 引入 0~1ms 不可控抖动
        //          PushKeyEvent 传 delayMs=0，SkyHookEvent 时间戳被完全丢弃
        //
        //  修复后：工作线程 → PushKeyEvent（直接，零队列）
        //          工作线程已精确等待到触发时刻，delayMs=0 语义正确（立刻执行）
        //          与 SendInput 模式延迟链等长，理论精度一致
        // ══════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DirectPushKey(byte keyCode, bool isDown)
        {
            if (!_isInitialized) return -1;

            int result = InputSystem.PushKeyEvent(keyCode, isDown, 0);

            // 统计（非热路径分支，result!=0 极少发生）
            if (result == 0)
                // 仅消费者自己写 _totalProcessed，无竞争，直接 ++
                _totalProcessed++;
            else
                Interlocked.Increment(ref _totalDropped);

            return result;
        }

        // ══════════════════════════════════════════════════════
        //  队列清空（Reset 时调用，清除 C++ 内部残留事件）
        // ══════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearQueue()
        {
            // 同步 ring buffer 指针（保持内部一致性）
            Interlocked.Exchange(ref _readIndex, _writeIndex);

            // 清空 C++ 层内部队列
            if (_isInitialized)
                InputSystem.ClearQueue();

            Macro.Log("[InputSystem] 队列已清空");
        }

        // ══════════════════════════════════════════════════════
        //  保留的 EnqueueEvent / EnqueueEvents
        //
        //  说明：热路径（Macro.SendKey）已改为 DirectPushKey，
        //  这两个方法不再被主流程调用，但保留接口供外部扩展或调试使用。
        //  若确认不需要可安全删除。
        // ══════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnqueueEvent(SkyHookEvent evt)
        {
            if (!_isInitialized) return;

            int write = _writeIndex;
            int read = _readIndex;

            if ((write - read) >= BUFFER_SIZE)
            {
                Interlocked.Increment(ref _totalDropped);
                return;
            }

            _ring[write & BUFFER_MASK] = evt;
            Interlocked.Increment(ref _writeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnqueueEvents(SkyHookEvent[] events)
        {
            if (!_isInitialized || events == null || events.Length == 0) return;

            int write = _writeIndex;
            int read = _readIndex;
            int space = BUFFER_SIZE - (write - read);
            int count = Math.Min(events.Length, space);

            int dropped = events.Length - count;
            if (dropped > 0)
                Interlocked.Add(ref _totalDropped, dropped);

            for (int i = 0; i < count; i++)
                _ring[(write + i) & BUFFER_MASK] = events[i];

            Interlocked.Add(ref _writeIndex, count);
        }

        // ══════════════════════════════════════════════════════
        //  统计
        // ══════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int queueSize, long processed, long dropped) GetStats() =>
            (_writeIndex - _readIndex, _totalProcessed, Interlocked.Read(ref _totalDropped));
    }
}