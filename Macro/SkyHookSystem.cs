using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable enable

namespace ADOFAIMacro.Macro
{
    #region SkyHook 系统独立实现

    /// <summary>
    /// SkyHook 兼容层：事件字段对齐游戏内 SkyHookEvent（Type/Label/Key 分离字段）。
    /// </summary>
    public static class SkyHookSystem
    {
        private static readonly long EpochTicks = new DateTime(1970, 1, 1).Ticks;

        /// <summary>
        /// SkyHook 事件结构（与游戏内结构对齐）。
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        public struct SkyHookEvent
        {
            [FieldOffset(0)]
            public long TimeSec;

            [FieldOffset(8)]
            public uint TimeSubsecNano;

            [FieldOffset(12)]
            public global::SkyHook.EventType Type;

            [FieldOffset(16)]
            public global::SkyHook.KeyLabel Label;

            [FieldOffset(20)]
            public ushort Key;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly long GetTimeInTicks() =>
                TimeSec * 10000000L + (long)(TimeSubsecNano / 100U) + EpochTicks;

            /// <summary>
            /// 创建 SkyHook 事件。
            /// elapsedTicks 输入单位为 100ns（DateTime ticks）。
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static SkyHookEvent Create(byte keyCode, bool isDown, long elapsedTicks)
            {
                var (sec, nano) = ConvertTicksToSkyHookTime(elapsedTicks);
                return new SkyHookEvent
                {
                    TimeSec = sec,
                    TimeSubsecNano = nano,
                    Type = isDown ? global::SkyHook.EventType.KeyPressed : global::SkyHook.EventType.KeyReleased,
                    Label = GetKeyLabel(keyCode),
                    Key = keyCode
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static (long sec, uint nano) ConvertTicksToSkyHookTime(long ticks)
            {
                long sec = ticks / 10000000L;
                uint nano = (uint)((ticks % 10000000L) * 100);
                return (sec, nano);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static global::SkyHook.KeyLabel GetKeyLabel(byte keyCode) => keyCode switch
            {
                >= 0x41 and <= 0x5A => (global::SkyHook.KeyLabel)((int)global::SkyHook.KeyLabel.A + (keyCode - 0x41)),
                >= 0x31 and <= 0x39 => (global::SkyHook.KeyLabel)((int)global::SkyHook.KeyLabel.Alpha1 + (keyCode - 0x31)),
                0x30 => global::SkyHook.KeyLabel.Alpha0,
                >= 0x70 and <= 0x7B => (global::SkyHook.KeyLabel)((int)global::SkyHook.KeyLabel.F1 + (keyCode - 0x70)),
                0x1B => global::SkyHook.KeyLabel.Escape,
                0x08 => global::SkyHook.KeyLabel.Backspace,
                0x09 => global::SkyHook.KeyLabel.Tab,
                0x0D => global::SkyHook.KeyLabel.Enter,
                0x20 => global::SkyHook.KeyLabel.Space,
                0x25 => global::SkyHook.KeyLabel.ArrowLeft,
                0x26 => global::SkyHook.KeyLabel.ArrowUp,
                0x27 => global::SkyHook.KeyLabel.ArrowRight,
                0x28 => global::SkyHook.KeyLabel.ArrowDown,
                0xA0 => global::SkyHook.KeyLabel.LShift,
                0xA1 => global::SkyHook.KeyLabel.RShift,
                0xA2 => global::SkyHook.KeyLabel.LControl,
                0xA3 => global::SkyHook.KeyLabel.RControl,
                0xA4 => global::SkyHook.KeyLabel.LAlt,
                0xA5 => global::SkyHook.KeyLabel.RAlt,
                _ => global::SkyHook.KeyLabel.Unknown
            };
        }

        /// <summary>
        /// Windows INPUT 结构定义
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
    }

    #endregion
}
