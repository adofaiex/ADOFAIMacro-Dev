using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace BaseMacro.Macro
{

    internal class TechniqueSimulator
    {

        // DLL句柄和委托
        private static IntPtr _techDllHandle = IntPtr.Zero;
        private static DelegateSetTechConfig? _setTechConfig;
        private static DelegateBuildTechEvents? _buildTechEvents;
        private static DelegateFreeTechEvents? _freeTechEvents;
        private static bool _dllLoadAttempted = false;

        // 缓存配置数据，避免重复分配
        private static byte[]? _cachedLeftKeys;
        private static byte[]? _cachedRightKeys;
        private static int[][]? _cachedLeftKeyOrders;
        private static int[][]? _cachedRightKeyOrders;
        private static double[]? _cachedLeftPressTimes;
        private static double[]? _cachedRightPressTimes;
        private static int _cachedBpmLimit;
        private static int _cachedHandPreference;

        // 结构体定义（必须与C++完全一致）
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NativeHitEvent
        {
            public double TriggerTime;
            public byte KeyCode;
            [MarshalAs(UnmanagedType.Bool)]
            public bool ReleaseOnly;
            [MarshalAs(UnmanagedType.Bool)]
            public bool IsHoldRelated;
            public byte ReleaseKeyCode;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NativeTechniqueConfig
        {
            public IntPtr LeftKeys;
            public int LeftKeyCount;
            public IntPtr RightKeys;
            public int RightKeyCount;
            public IntPtr LeftKeyOrders;
            public IntPtr LeftOrderLengths;
            public int LeftOrderCounts;
            public IntPtr RightKeyOrders;
            public IntPtr RightOrderLengths;
            public int RightOrderCounts;
            public IntPtr LeftPressTimes;
            public IntPtr RightPressTimes;
            public double BpmLimit;
            public int HandPreference;
        }

        // 委托定义
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateSetTechConfig(ref NativeTechniqueConfig config);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DelegateBuildTechEvents(
            [In] double[] entryTimes,
            [In] int[] pressTypes,
            int eventCount,
            double bpm,
            double speed,
            double pitch,
            out int outEventCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateFreeTechEvents(IntPtr events);

        // Kernel32函数
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        /// <summary>
        /// 更新配置缓存
        /// </summary>
        /// <summary>
        /// 更新配置缓存
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateConfig(
            byte[] leftKeys,
            byte[] rightKeys,
            int[][] leftKeyOrders,
            int[][] rightKeyOrders,
            double[] leftPressTimes,
            double[] rightPressTimes,
            double bpmLimit,
            int handPreference)
        {
            _cachedLeftKeys = leftKeys;
            _cachedRightKeys = rightKeys;
            _cachedLeftKeyOrders = leftKeyOrders;
            _cachedRightKeyOrders = rightKeyOrders;
            _cachedLeftPressTimes = leftPressTimes;
            _cachedRightPressTimes = rightPressTimes;
            _cachedBpmLimit = (int)bpmLimit;
            _cachedHandPreference = handPreference;
        }

        /// <summary>
        /// 加载手法模拟DLL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LoadTechniqueDll()
        {
            if (_dllLoadAttempted) return _techDllHandle != IntPtr.Zero;
            _dllLoadAttempted = true;

            try
            {
                string modPath = Main.Mod?.Path
                   ?? Path.GetDirectoryName(typeof(InputSystem).Assembly.Location);

                string dllName = "TechniqueSimulator.dll";

                string dllPath = Path.Combine(modPath, dllName);
                if (dllPath == null)
                {
                    Macro.Log($"[Macro] 找不到手法模拟DLL: {dllName}");
                    return false;
                }

                Macro.Log($"[Macro] 加载DLL: {dllPath}");
                _techDllHandle = LoadLibrary(dllPath);

                if (_techDllHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Macro.Log($"[Macro] 加载DLL失败，错误码: {error}");
                    return false;
                }

                // 获取函数指针
                IntPtr setConfigPtr = GetProcAddress(_techDllHandle, "SetTechniqueConfig");
                IntPtr buildEventsPtr = GetProcAddress(_techDllHandle, "BuildTechniqueHitEvents");
                IntPtr freeEventsPtr = GetProcAddress(_techDllHandle, "FreeHitEvents");

                if (setConfigPtr == IntPtr.Zero || buildEventsPtr == IntPtr.Zero || freeEventsPtr == IntPtr.Zero)
                {
                    Macro.Log("[Macro] 获取函数地址失败");
                    FreeLibrary(_techDllHandle);
                    _techDllHandle = IntPtr.Zero;
                    return false;
                }

                // 创建委托
                _setTechConfig = Marshal.GetDelegateForFunctionPointer<DelegateSetTechConfig>(setConfigPtr);
                _buildTechEvents = Marshal.GetDelegateForFunctionPointer<DelegateBuildTechEvents>(buildEventsPtr);
                _freeTechEvents = Marshal.GetDelegateForFunctionPointer<DelegateFreeTechEvents>(freeEventsPtr);

                Macro.Log("[Macro] 手法模拟DLL加载成功");
                return true;
            }
            catch (Exception ex)
            {
                Macro.Log($"[Macro] 加载DLL异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 准备Native配置
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeTechniqueConfig PrepareNativeConfig()
        {
            if (_cachedLeftKeys == null || _cachedRightKeys == null)
                throw new InvalidOperationException("请先调用 UpdateConfig 更新配置");

            var config = new NativeTechniqueConfig
            {
                BpmLimit = _cachedBpmLimit,
                HandPreference = _cachedHandPreference
            };

            try
            {
                // 分配左右手按键数据
                config.LeftKeys = Marshal.AllocCoTaskMem(_cachedLeftKeys.Length);
                Marshal.Copy(_cachedLeftKeys, 0, config.LeftKeys, _cachedLeftKeys.Length);
                config.LeftKeyCount = _cachedLeftKeys.Length;

                config.RightKeys = Marshal.AllocCoTaskMem(_cachedRightKeys.Length);
                Marshal.Copy(_cachedRightKeys, 0, config.RightKeys, _cachedRightKeys.Length);
                config.RightKeyCount = _cachedRightKeys.Length;

                // 分配按键顺序数据
                if (_cachedLeftKeyOrders != null)
                    PrepareOrderData(0, _cachedLeftKeyOrders, ref config);
                if (_cachedRightKeyOrders != null)
                    PrepareOrderData(1, _cachedRightKeyOrders, ref config);

                // 分配按键时长数据
                if (_cachedLeftPressTimes != null)
                {
                    config.LeftPressTimes = Marshal.AllocCoTaskMem(_cachedLeftPressTimes.Length * sizeof(double));
                    Marshal.Copy(_cachedLeftPressTimes, 0, config.LeftPressTimes, _cachedLeftPressTimes.Length);
                }

                if (_cachedRightPressTimes != null)
                {
                    config.RightPressTimes = Marshal.AllocCoTaskMem(_cachedRightPressTimes.Length * sizeof(double));
                    Marshal.Copy(_cachedRightPressTimes, 0, config.RightPressTimes, _cachedRightPressTimes.Length);
                }

                return config;
            }
            catch
            {
                FreeNativeConfig(ref config);
                throw;
            }
        }

        /// <summary>
        /// 准备按键顺序数据
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void PrepareOrderData(int hand, int[][] orders, ref NativeTechniqueConfig config)
        {
            if (orders == null || orders.Length == 0) return;

            int orderCount = orders.Length;

            // 分配指针数组
            IntPtr* orderPtrs = stackalloc IntPtr[orderCount];
            int* lengthPtrs = stackalloc int[orderCount];

            for (int i = 0; i < orderCount; i++)
            {
                lengthPtrs[i] = orders[i].Length;
                orderPtrs[i] = Marshal.AllocCoTaskMem(orders[i].Length * sizeof(int));

                // 直接复制 int[] 数组
                Marshal.Copy(orders[i], 0, orderPtrs[i], orders[i].Length);
            }

            // 分配主数组
            int totalSize = orderCount * IntPtr.Size;
            IntPtr ordersArray = Marshal.AllocCoTaskMem(totalSize);
            for (int i = 0; i < orderCount; i++)
            {
                Marshal.WriteIntPtr(ordersArray, i * IntPtr.Size, orderPtrs[i]);
            }

            IntPtr lengthsArray = Marshal.AllocCoTaskMem(orderCount * sizeof(int));

            // 复制长度数组
            int[] lengthArray = new int[orderCount];
            for (int i = 0; i < orderCount; i++)
            {
                lengthArray[i] = lengthPtrs[i];
            }
            Marshal.Copy(lengthArray, 0, lengthsArray, orderCount);

            if (hand == 0)
            {
                config.LeftKeyOrders = ordersArray;
                config.LeftOrderLengths = lengthsArray;
                config.LeftOrderCounts = orderCount;
            }
            else
            {
                config.RightKeyOrders = ordersArray;
                config.RightOrderLengths = lengthsArray;
                config.RightOrderCounts = orderCount;
            }
        }

        /// <summary>
        /// 释放Native配置内存
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FreeNativeConfig(ref NativeTechniqueConfig config)
        {
            // 释放基本数组
            if (config.LeftKeys != IntPtr.Zero) Marshal.FreeCoTaskMem(config.LeftKeys);
            if (config.RightKeys != IntPtr.Zero) Marshal.FreeCoTaskMem(config.RightKeys);
            if (config.LeftPressTimes != IntPtr.Zero) Marshal.FreeCoTaskMem(config.LeftPressTimes);
            if (config.RightPressTimes != IntPtr.Zero) Marshal.FreeCoTaskMem(config.RightPressTimes);

            // 释放左手指令数组
            if (config.LeftKeyOrders != IntPtr.Zero)
            {
                for (int i = 0; i < config.LeftOrderCounts; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(config.LeftKeyOrders, i * IntPtr.Size);
                    if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
                }
                Marshal.FreeCoTaskMem(config.LeftKeyOrders);
            }

            if (config.LeftOrderLengths != IntPtr.Zero)
                Marshal.FreeCoTaskMem(config.LeftOrderLengths);

            // 释放右手指令数组
            if (config.RightKeyOrders != IntPtr.Zero)
            {
                for (int i = 0; i < config.RightOrderCounts; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(config.RightKeyOrders, i * IntPtr.Size);
                    if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
                }
                Marshal.FreeCoTaskMem(config.RightKeyOrders);
            }

            if (config.RightOrderLengths != IntPtr.Zero)
                Marshal.FreeCoTaskMem(config.RightOrderLengths);
        }

        /// <summary>
        /// 构建手法模拟事件
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BuildHitEvents(
            double[] entryTimes,
            int[] pressTypes,
            int eventCount,
            double bpm,
            double speed,
            double pitch,
            out Macro.HitEvent[]? hitEvents)
        {
            hitEvents = null;

            if (_cachedLeftKeys == null)
            {
                Macro.Log("[Macro] 手法模拟配置未初始化");
                return false;
            }

            NativeTechniqueConfig config = default;
            IntPtr nativeEvents = IntPtr.Zero;

            try
            {
                config = PrepareNativeConfig();
                _setTechConfig!(ref config);

                nativeEvents = _buildTechEvents!(
                    entryTimes,
                    pressTypes,
                    eventCount,
                    bpm,
                    speed,
                    pitch,
                    out int outCount);

                if (nativeEvents != IntPtr.Zero && outCount > 0)
                {
                    // 转换回C#结构
                    var events = new Macro.HitEvent[outCount];
                    int size = Marshal.SizeOf<NativeHitEvent>();

                    for (int i = 0; i < outCount; i++)
                    {
                        IntPtr ptr = IntPtr.Add(nativeEvents, i * size);
                        var native = Marshal.PtrToStructure<NativeHitEvent>(ptr);
                        events[i] = new Macro.HitEvent(
                            native.TriggerTime,
                            native.KeyCode,
                            native.ReleaseOnly,
                            native.IsHoldRelated,
                            native.ReleaseKeyCode);
                    }

                    hitEvents = events;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Macro.Log($"[Macro] 手法模拟DLL调用异常: {ex.Message}");
                return false;
            }
            finally
            {
                FreeNativeConfig(ref config);
                if (nativeEvents != IntPtr.Zero)
                    _freeTechEvents!(nativeEvents);
            }
        }

        /// <summary>
        /// 检查DLL是否已加载
        /// </summary>
        public static bool IsDllLoaded()
        {
            return _techDllHandle != IntPtr.Zero;
        }

        /// <summary>
        /// 重置加载状态（在需要重新加载时调用）
        /// </summary>
        public static void Reset()
        {
            Unload();
            _dllLoadAttempted = false;
        }

        /// <summary>
        /// 卸载DLL
        /// </summary>
        public static void Unload()
        {
            if (_techDllHandle != IntPtr.Zero)
            {
                FreeLibrary(_techDllHandle);
                _techDllHandle = IntPtr.Zero;
            }
            _setTechConfig = null;
            _buildTechEvents = null;
            _freeTechEvents = null;
            _dllLoadAttempted = false;
        }
    }
}