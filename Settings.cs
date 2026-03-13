using ADOFAIMacro.Macro;
using HarmonyLib;
using Newgrounds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace ADOFAIMacro
{
    /// <summary>
    /// Mod settings class
    /// Mod 设置类
    /// </summary>
    public class Settings : UnityModManager.ModSettings
    {

        [Serializable]
        public class TechniqueProfile
        {
            public string name = "默认配置";
            public string leftHandKeys = "D,F";
            public string rightHandKeys = "J,K";
            public string leftHandOrders = "";
            public string rightHandOrders = "";
            public string leftHandPressTimes = "0.8,0.8";
            public string rightHandPressTimes = "0.8,0.8";
            public int handPreference = 1; // 0=左手优先, 1=右手优先

            public TechniqueProfile() { } // 无参构造用于序列化

            // 克隆方法，用于新建配置时复制当前值
            public TechniqueProfile Clone()
            {
                return new TechniqueProfile
                {
                    name = this.name + " (副本)",
                    leftHandKeys = this.leftHandKeys,
                    rightHandKeys = this.rightHandKeys,
                    leftHandOrders = this.leftHandOrders,
                    rightHandOrders = this.rightHandOrders,
                    leftHandPressTimes = this.leftHandPressTimes,
                    rightHandPressTimes = this.rightHandPressTimes,
                    handPreference = this.handPreference
                };
            }
        }
        public event Action<bool> OnMacroChanged;

        private bool _useChinese = true;  // 默认中文
        public bool UseChinese
        {
            get => _useChinese;
            set
            {
                if (_useChinese == value) return;
                _useChinese = value;
            }
        }
        private bool _macro;
        public bool Macro
        {
            get => _macro;
            set
            {
                if (_macro == value) return;
                _macro = value;
                OnMacroChanged?.Invoke(value);
            }
        }

        // 修复属性语法
        private string _macroKeys = "D,F,J,K";
        public string MacroKeys
        {
            get => _macroKeys;
            set
            {
                if (_macroKeys == value) return;
                _macroKeys = value;
            }
        }

        private bool _simulateKeyPress = false;
        public bool SimulateKeyPress
        {
            get => _simulateKeyPress;
            set
            {
                if (_simulateKeyPress == value) return;
                _simulateKeyPress = value;
            }
        }
        public bool EnableKeyAdjust = true;
        public float AdjustStep = 1f;

        private float _timeOffset;
        public float TimeOffset
        {
            get => _timeOffset;
            set => _timeOffset = Mathf.Clamp(value, -100f, 100f);
        }

        public bool EnableArrowTimeAdjust = true;

        private bool _skyHookMode = false;
        public bool SkyHookMode
        {
            get => _skyHookMode;
            set
            {
                if (_skyHookMode == value) return;
                _skyHookMode = value;
                // 可以在这里添加模式切换的即时处理
                // OnSkyHookModeChanged?.Invoke(value);
            }
        }

        // 使用ValueTuple减少GC
        private (string input, bool focused) _adjustStepState = (string.Empty, false);
        private (string input, bool focused) _timeOffsetState = (string.Empty, false);

        private bool _highPrecisionAsync = false;
        public bool HighPrecisionAsync
        {
            get => _highPrecisionAsync;
            set
            {
                if (_highPrecisionAsync == value) return;
                _highPrecisionAsync = value;
            }
        }

        // 添加测试版判断属性
        private int? _betaVersion = null;
        public int BetaVersion
        {
            get
            {
                if (_betaVersion == null)
                {
                    _betaVersion = GetBetaVersionFromAssembly();
                }
                return _betaVersion.Value;
            }
        }

        public bool IsBeta => BetaVersion > 0;

        private int GetBetaVersionFromAssembly()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                Version version = assembly.GetName().Version;
                // 获取版本号的第四位（修订号）
                int betaNumber = version.Revision;
                return betaNumber;
            }
            catch
            {
                return 0; // 如果获取失败，默认为正式版
            }
        }

        private int _inputMode = 0; // 默认 Auto
        public int InputMode
        {
            get => _inputMode;
            set
            {
                if (_inputMode == value) return;
                _inputMode = value;
                // 通知 DLL 切换模式
                if (ADOFAIMacro.Macro.InputSystem.IsInitialized)
                    ADOFAIMacro.Macro.InputSystem.SetInputMode((Macro.InputMode)value);
            }
        }

        private bool _enableDeathKey = false;
        public bool EnableDeathKey
        {
            get => _enableDeathKey;
            set
            {
                if (_enableDeathKey == value) return;
                _enableDeathKey = value;
            }
        }

        private float _deathKeyDelay = 5f;
        public float DeathKeyDelay
        {
            get => _deathKeyDelay;
            set => _deathKeyDelay = Mathf.Clamp(value, 0.1f, 30f);
        }

        private int _deathKeyCode = 0x52; // 默认 R 键 (0x52)
        public int DeathKeyCode
        {
            get => _deathKeyCode;
            set => _deathKeyCode = value;
        }

        private string _deathKeyInput = "R";
        public string DeathKeyInput
        {
            get => _deathKeyInput;
            set
            {
                if (_deathKeyInput == value) return;
                _deathKeyInput = value.ToUpper();
                // 尝试转换按键代码
                int? keyCode = GetKeyCodeFromString(_deathKeyInput);
                if (keyCode.HasValue)
                {
                    _deathKeyCode = keyCode.Value;
                }
            }
        }

        public bool ChangeNoFaillInPlay = false;
        public bool ChangeJudementInPlay = false;
        public bool LockLevelEditor = false;

        // ─────────────────────────────────────────────
        //  手法模拟设置
        // ─────────────────────────────────────────────
        private bool _enableTechSim = false;
        public bool EnableTechniqueSimulation
        {
            get => _enableTechSim;
            set { if (_enableTechSim == value) return; _enableTechSim = value; }
        }

        private float _techniqueBpmLimit = 500f;
        public float TechniqueBpmLimit
        {
            get => _techniqueBpmLimit;
            set => _techniqueBpmLimit = Mathf.Clamp(value, 50f, 2000f);
        }

        // 左右手按键列表，逗号分隔，例 "D,F"
        public string TechLeftHandKeys = "D,F";
        public string TechRightHandKeys = "J,K";

        // 按键顺序表：用 | 分隔不同按键数的方案，逗号分隔每方案内的按键序号(1-based)
        // 例："1|2,1|1,2,3" 表示：1键→第1键；2键→第2键再第1键；3键→第1,2,3键
        // 留空 = 默认顺序
        public string TechLeftHandOrders = "";
        public string TechRightHandOrders = "";

        // 每个按键的按下时长比例(0.05~1.0)，逗号分隔
        public string TechLeftHandPressTimes = "0.8,0.8";
        public string TechRightHandPressTimes = "0.8,0.8";

        // UI 状态（内部使用）
        private (string input, bool focused) _techBpmState = (string.Empty, false);

        private (string input, bool focused) _deathKeyDelayState = (string.Empty, false);

        private static readonly Dictionary<string, int> KeyCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // 字母键
            {"A", 0x41}, {"B", 0x42}, {"C", 0x43}, {"D", 0x44}, {"E", 0x45}, {"F", 0x46},
            {"G", 0x47}, {"H", 0x48}, {"I", 0x49}, {"J", 0x4A}, {"K", 0x4B}, {"L", 0x4C},
            {"M", 0x4D}, {"N", 0x4E}, {"O", 0x4F}, {"P", 0x50}, {"Q", 0x51}, {"R", 0x52},
            {"S", 0x53}, {"T", 0x54}, {"U", 0x55}, {"V", 0x56}, {"W", 0x57}, {"X", 0x58},
            {"Y", 0x59}, {"Z", 0x5A},
            
            // 数字键
            {"0", 0x30}, {"1", 0x31}, {"2", 0x32}, {"3", 0x33}, {"4", 0x34},
            {"5", 0x35}, {"6", 0x36}, {"7", 0x37}, {"8", 0x38}, {"9", 0x39},
            
            // 功能键
            {"F1", 0x70}, {"F2", 0x71}, {"F3", 0x72}, {"F4", 0x73}, {"F5", 0x74},
            {"F6", 0x75}, {"F7", 0x76}, {"F8", 0x77}, {"F9", 0x78}, {"F10", 0x79},
            {"F11", 0x7A}, {"F12", 0x7B},
            
            // 特殊键
            {"SPACE", 0x20}, {"ENTER", 0x0D}, {"RETURN", 0x0D}, {"ESC", 0x1B},
            {"TAB", 0x09}, {"SHIFT", 0x10}, {"CTRL", 0x11}, {"ALT", 0x12},
            {"BACKSPACE", 0x08}, {"DELETE", 0x2E}, {"INSERT", 0x2D},
            {"HOME", 0x24}, {"END", 0x23}, {"PAGEUP", 0x21}, {"PAGEDOWN", 0x22},
            
            // 方向键
            {"UP", 0x26}, {"DOWN", 0x28}, {"LEFT", 0x25}, {"RIGHT", 0x27}
        };

        private int? GetKeyCodeFromString(string keyString)
        {
            if (string.IsNullOrEmpty(keyString))
                return null;

            // 尝试直接解析为十六进制
            if (keyString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(keyString.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hexCode))
                    return hexCode;
            }

            // 尝试从映射表获取
            if (KeyCodeMap.TryGetValue(keyString, out int code))
                return code;

            return null;
        }

        // 在现有属性后面添加
        private bool _enableKeyFilter = false;
        public bool EnableKeyFilter
        {
            get => _enableKeyFilter;
            set
            {
                if (_enableKeyFilter == value) return;
                _enableKeyFilter = value;
            }
        }

        private int _filterMode = 0; // 0: 黑名单模式, 1: 白名单模式
        public int FilterMode
        {
            get => _filterMode;
            set => _filterMode = value;
        }

        private string _filteredKeys = "F1,F2,F3,F4";
        public string FilteredKeys
        {
            get => _filteredKeys;
            set => _filteredKeys = value;
        }

        private string _filteredAsyncKeys = "";
        public string FilteredAsyncKeys
        {
            get => _filteredAsyncKeys;
            set => _filteredAsyncKeys = value;
        }

        private (string input, bool focused) _filteredKeysState = (string.Empty, false);
        private (string input, bool focused) _filteredAsyncKeysState = (string.Empty, false);
        public bool HighPrecisionTime;

        private int selectedCardIndex = 0;

        // 手法模拟起始手偏好：0 = 左手优先, 1 = 右手优先
        private int _techniqueHandPreference = 1;
        public int TechniqueHandPreference
        {
            get => _techniqueHandPreference;
            set
            {
                if (_techniqueHandPreference == value) return;
                _techniqueHandPreference = value;
            }
        }

        // 重构后的 OnGUI 方法
        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            UIUtils.InitializeStyles();

            // 构建卡片列表（名称 + 绘制委托）
            var cards = new List<(string name, Action draw)>();

            // 语言卡
            cards.Add((UseChinese ? "语言" : "Language", DrawLanguageCard));
            // 主开关卡
            cards.Add((UseChinese ? "宏" : "Macro", DrawMainSwitchCard));

            if (Macro)
            {
                // 按键设置卡
                cards.Add((UseChinese ? "按键设置" : "Key Settings", DrawKeySettingsCard));
                // 按键过滤卡
                cards.Add((UseChinese ? "按键过滤" : "Key Filter", DrawKeyFilterCard));
                // 延迟设置卡
                cards.Add((UseChinese ? "延迟设置" : "Offset Settings", DrawOffsetSettingsCard));
                // 其他设置卡
                cards.Add((UseChinese ? "其他选项" : "Other Settings", DrawOtherSettingsCard));

                if (SimulateKeyPress)
                {
                    // 手法模拟卡
                    cards.Add((UseChinese ? "手法模拟" : "Technique Simulation", DrawTechniqueSimCard));
                }
            }

            // 更新日志卡
            cards.Add((UseChinese ? "更新日志" : "Update Log", DrawUpdateLogCard));
            // 作者卡
            cards.Add((UseChinese ? "作者" : "Author", DrawAuthorCard));

            if (IsBeta)
            {
                // 测试版卡
                cards.Add((UseChinese ? "测试版" : "Beta", DrawBetaCard));
            }

            // 确保选中的索引有效
            if (selectedCardIndex >= cards.Count) selectedCardIndex = 0;

            // ----- 顶部卡片选择栏（定制UI）-----
            // 使用 Material 3 风格的 SelectionGrid 作为选项卡
            string[] cardNames = cards.Select(c => c.name).ToArray();
            selectedCardIndex = UIUtils.M3SelectionGrid(selectedCardIndex, cardNames, cards.Count, GUILayout.Height(30));

            GUILayout.Space(10);

            // ----- 当前选中的卡片内容 -----
            if (cards.Count > 0)
            {
                cards[selectedCardIndex].draw();
            }
        }
        private void DrawLanguageCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);

            // 如果是测试版，在标题旁显示测试版标记
            string headerText = UseChinese ? "语言" : "Language";

            GUILayout.Label(headerText, UIUtils.HeaderStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            string LanguageSwitchText = UseChinese ? "显示语言" : "Display Language";

            GUILayout.Label(LanguageSwitchText, UIUtils.LabelStyle, GUILayout.Width(150));

            string[] languages = ["中文", "English"];
            int selected = UseChinese ? 0 : 1;
            int newSelected = UIUtils.M3SelectionGrid(selected, languages, 2, GUILayout.Width(200));
            if (newSelected != selected)
            {
                UseChinese = newSelected == 0;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawMainSwitchCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "宏" : "Macro", UIUtils.HeaderStyle);

            string macroSwitchText = UseChinese ? "启用宏" : "Enable Macro";
            bool newMacro = UIUtils.M3Switch(Macro, macroSwitchText);
            if (newMacro != Macro)
            {
                Macro = newMacro;
                ADOBase.controller.Restart();
            }
            GUILayout.EndVertical();
        }

        private void DrawOffsetSettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "延迟设置" : "Offset Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            string adjustText = UseChinese ? "允许Ctrl+左右键调整步长偏移(游戏中)" : "Allow adjusting step offset using Ctrl and arrow keys (in-game)";
            EnableKeyAdjust = UIUtils.M3Switch(EnableKeyAdjust, adjustText);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            AdjustStep = UIUtils.M3HorizontalSliderWithLabelAndInput(UseChinese ? "调整步长" : "Adjust Step", AdjustStep, 0.1f, 10f,
                ref _adjustStepState.input, ref _adjustStepState.focused, "F2", 120, 240, 60);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            TimeOffset = UIUtils.M3HorizontalSliderWithLabelAndInput(UseChinese ? "延迟 (ms)" : "Offset (ms)", TimeOffset, -100f, 100f,
                ref _timeOffsetState.input, ref _timeOffsetState.focused, "F2", 120, 240, 60);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            string arrowText = UseChinese ? "允许左右键调整延迟(游戏中)" : "Allow adjustment of delay using left and right keys (in-game)";
            EnableArrowTimeAdjust = UIUtils.M3Switch(EnableArrowTimeAdjust, arrowText);
            GUILayout.Space(2);
            string highPrecisionText = UseChinese ? "启用高精度时间（提高同步精度）" : "Enable High Precision Time (improves sync accuracy)";
            HighPrecisionTime = UIUtils.M3Switch(HighPrecisionTime, highPrecisionText);
            GUILayout.Space(2);
            string highPrecisionAsyncText = UseChinese ? "[实验性]启用高精度异步" : "[Experimental]Enable High Precision Async";
            HighPrecisionAsync = UIUtils.M3Switch(HighPrecisionAsync, highPrecisionAsyncText);
            GUILayout.EndVertical();
        }

        private void DrawKeySettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "按键设置" : "Key Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            if (!Main.Settings.EnableTechniqueSimulation)
            {
                GUILayout.BeginHorizontal();
                string keysLabel = UseChinese ? "按键序列 (逗号分隔)" : "Keys (comma separated)";
                GUILayout.Label(keysLabel, UIUtils.LabelStyle, GUILayout.Width(180));
                MacroKeys = GUILayout.TextField(MacroKeys, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(2);
            string simulateText = UseChinese ? "按键模拟" : "Key simulation";
            bool newSimulateKeyPress = UIUtils.M3Switch(SimulateKeyPress, simulateText);
            if (newSimulateKeyPress != SimulateKeyPress)
            {
                SimulateKeyPress = newSimulateKeyPress;
                ADOBase.controller.Restart();
            }

            if (SimulateKeyPress)
            {
                GUILayout.Space(2);
                string skyHook = UseChinese ? "使用高级输入(否则使用SendInput API)" : "Use advanced input (if closed, use SendInput API)";
                SkyHookMode = UIUtils.M3Switch(SkyHookMode, skyHook);

                // ── 仅在 SkyHook 开启时显示输入模式选择 ──────────────
                if (SkyHookMode)
                {
                    GUILayout.Space(6);

                    // 分隔线
                    Color originalColor = GUI.color;
                    GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                    GUI.color = originalColor;

                    GUILayout.Space(4);

                    // 标题 + 当前生效模式
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(
                        UseChinese ? "Win API 输入模式" : "Win API Input Mode",
                        UIUtils.LabelStyle, GUILayout.Width(150));

                    // 当前实际生效模式（只读提示）
                    if (InputSystem.IsInitialized)
                    {
                        var actual = InputSystem.GetInputMode();
                        GUIStyle hintStyle = new(UIUtils.LabelStyle);
                        hintStyle.normal.textColor = new Color(0.5f, 0.9f, 0.5f, 0.8f);
                        hintStyle.fontSize = 10;
                        string actualLabel = UseChinese
                            ? $"[实际: {GetModeLabel(actual, true)}]"
                            : $"[Active: {GetModeLabel(actual, false)}]";
                        GUILayout.Label(actualLabel, hintStyle);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);

                    // 可用模式检测
                    bool hasInject = !ADOFAIMacro.Macro.InputSystem.IsInitialized || ADOFAIMacro.Macro.InputSystem.IsModeAvailable(ADOFAIMacro.Macro.InputMode.NtUserInjectKeyboard);
                    bool hasNtSend = !ADOFAIMacro.Macro.InputSystem.IsInitialized || ADOFAIMacro.Macro.InputSystem.IsModeAvailable(ADOFAIMacro.Macro.InputMode.NtUserSendInput);

                    // 模式按钮组
                    string[] modeLabels = UseChinese
                        ? ["自动", "NtInject", "NtSendInput ★", "SendInput"]
                        : ["Auto", "NtInject", "NtSendInput ★", "SendInput"];

                    // 不可用的模式置灰
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < 4; i++)
                    {
                        bool available = i switch
                        {
                            1 => hasInject,
                            2 => hasNtSend,
                            _ => true
                        };
                        string label = modeLabels[i];
                        if (!available)
                            label += UseChinese ? "(不支持)" : "(N/A)";

                        bool clicked = GUILayout.Button(label, UIUtils.ButtonStyle, GUILayout.Height(24));
                        if (clicked && available && InputMode != i)
                            InputMode = i;
                    }
                    GUILayout.EndHorizontal();

                    // 模式说明
                    GUILayout.Space(4);
                    GUIStyle descStyle = new(UIUtils.LabelStyle);
                    descStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);
                    descStyle.fontSize = 10;
                    descStyle.wordWrap = true;

                    string desc = InputMode switch
                    {
                        0 => UseChinese
                            ? "自动：优先使用最底层可用方式"
                            : "Auto: use the lowest available layer automatically",
                        1 => UseChinese
                            ? "NtInject （最底层）：直接注入原始输入流，绕过用户层检测"
                            : "NtInject  (deepest): inject into raw input stream, bypasses user-mode filters",
                        2 => UseChinese
                            ? "NtSendInput ★：内核边界注入，比 SendInput 底层"
                            : "NtSendInput ★: kernel-boundary injection, lower than SendInput",
                        3 => UseChinese
                            ? "SendInput：标准 Win32 API，兼容性最佳"
                            : "SendInput: standard Win32 API, best compatibility",
                        _ => ""
                    };
                    GUILayout.Label(desc, descStyle);
                }
            }

            GUILayout.EndVertical();
        }

        // ── 模式名称辅助 ──────────────────────────────────────────
        private string GetModeLabel(Macro.InputMode mode, bool chinese) => mode switch
        {
            ADOFAIMacro.Macro.InputMode.Auto => chinese ? "自动" : "Auto",
            ADOFAIMacro.Macro.InputMode.NtUserInjectKeyboard => chinese ? "NtInject" : "NtInject",
            ADOFAIMacro.Macro.InputMode.NtUserSendInput => chinese ? "NtSendInput ★" : "NtSendInput ★",
            ADOFAIMacro.Macro.InputMode.SendInput => chinese ? "SendInput" : "SendInput",
            _ => mode.ToString()
        };

        private void DrawKeyFilterCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "按键过滤" : "Key Filter", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            string enableFilterText = UseChinese ? "启用按键过滤" : "Enable Key Filter";
            bool newEnableFilter = UIUtils.M3Switch(EnableKeyFilter, enableFilterText);
            if (newEnableFilter != EnableKeyFilter)
                EnableKeyFilter = newEnableFilter;

            if (EnableKeyFilter)
            {
                GUILayout.Space(6);

                // 分隔线
                Color originalColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                GUI.color = originalColor;

                GUILayout.Space(4);

                // 过滤模式选择
                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "过滤模式" : "Filter Mode", UIUtils.LabelStyle, GUILayout.Width(100));
                string[] modes = UseChinese
                    ? ["黑名单模式", "白名单模式"]
                    : ["Blacklist Mode", "Whitelist Mode"];
                int newMode = UIUtils.M3SelectionGrid(FilterMode, modes, 2, GUILayout.Width(200));
                if (newMode != FilterMode)
                    FilterMode = newMode;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                // 模式说明
                GUIStyle descStyle = new(UIUtils.LabelStyle);
                descStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                descStyle.fontSize = 11;
                descStyle.wordWrap = true;
                GUILayout.Label(FilterMode == 0
                    ? (UseChinese ? "⛔ 黑名单模式：列表中的按键将被阻止" : "⛔ Blacklist: Keys in the list will be blocked")
                    : (UseChinese ? "✅ 白名单模式：只有列表中的按键允许通过" : "✅ Whitelist: Only keys in the list are allowed"),
                    descStyle);

                GUILayout.Space(8);

                // 普通按键列表
                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "按键列表 (逗号分隔)" : "Keys (comma separated)", UIUtils.LabelStyle, GUILayout.Width(140)); // 原160 -> 140
                string newFilteredKeys = UIUtils.M3TextField(FilteredKeys,
                    ref _filteredKeysState.input,
                    ref _filteredKeysState.focused,
                    UIUtils.TextFieldStyle,
                    "TechnicalkeysNormal");
                if (newFilteredKeys != FilteredKeys)
                    FilteredKeys = newFilteredKeys;
                GUILayout.EndHorizontal();

                // 异步按键列表同理
                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "异步按键列表 (逗号分隔)" : "Async Keys (comma separated)", UIUtils.LabelStyle, GUILayout.Width(140));
                if (SkyHookMode)
                {
                    string newFilteredAsyncKeys = UIUtils.M3TextField(FilteredAsyncKeys,
                        ref _filteredAsyncKeysState.input,
                        ref _filteredAsyncKeysState.focused,
                        UIUtils.TextFieldStyle,
                        "TechnicalkeysAsync"); // 固定宽度250
                    if (newFilteredAsyncKeys != FilteredAsyncKeys)
                        FilteredAsyncKeys = newFilteredAsyncKeys;
                }
                else
                {
                    GUIStyle disabledStyle = new(UIUtils.LabelStyle);
                    disabledStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    GUILayout.Label(UseChinese ? "（需开启 SkyHook 模式）" : "(Requires SkyHook Mode)", disabledStyle);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                // 常用按键标签
                GUILayout.Label(UseChinese ? "常用按键:" : "Common Keys:", UIUtils.LabelStyle);
                GUILayout.Space(2);

                // 第一行
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("F1,F2,F3,F4", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "F1,F2,F3,F4";
                    if (SkyHookMode) FilteredAsyncKeys = "F1,F2,F3,F4";
                }
                if (GUILayout.Button("F5,F6,F7,F8", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "F5,F6,F7,F8";
                    if (SkyHookMode) FilteredAsyncKeys = "F5,F6,F7,F8";
                }
                if (GUILayout.Button("F9,F10,F11,F12", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "F9,F10,F11,F12";
                    if (SkyHookMode) FilteredAsyncKeys = "F9,F10,F11,F12";
                }
                GUILayout.EndHorizontal();

                // 第二行
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("A,S,D,F", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "A,S,D,F";
                    if (SkyHookMode) FilteredAsyncKeys = "A,S,D,F";
                }
                if (GUILayout.Button("J,K,L", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "J,K,L";
                    if (SkyHookMode) FilteredAsyncKeys = "J,K,L";
                }
                if (GUILayout.Button("1,2,3,4", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "1,2,3,4";
                    if (SkyHookMode) FilteredAsyncKeys = "1,2,3,4";
                }
                GUILayout.EndHorizontal();

                // 第三行
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SPACE,ENTER,ESC", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "SPACE,ENTER,ESC";
                    if (SkyHookMode) FilteredAsyncKeys = "SPACE,ENTER,ESC";
                }
                if (GUILayout.Button("UP,DOWN,LEFT,RIGHT", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "UP,DOWN,LEFT,RIGHT";
                    if (SkyHookMode) FilteredAsyncKeys = "UP,DOWN,LEFT,RIGHT";
                }
                if (GUILayout.Button("CTRL,ALT,SHIFT", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                {
                    FilteredKeys = "CTRL,ALT,SHIFT";
                    if (SkyHookMode) FilteredAsyncKeys = "CTRL,ALT,SHIFT";
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                // 提示信息
                GUIStyle tipStyle = new(UIUtils.LabelStyle);
                tipStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
                tipStyle.fontSize = 10;
                tipStyle.wordWrap = true;
                GUILayout.Label(UseChinese
                    ? "提示：支持按键名称（A、B、SPACE、ENTER等）和虚拟键码（0x41格式）。多个按键用逗号分隔。"
                    : "Tip: Supports key names (A, B, SPACE, ENTER, etc.) and virtual key codes (0x41 format). Separate multiple keys with commas.",
                    tipStyle);
            }

            GUILayout.EndVertical();
        }

        // 在 DrawOtherSettingsCard 方法中修改
        private void DrawOtherSettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "其他选项" : "Other Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            // SkyHook 已开启，显示正常功能
            string deathKeySwitchText = UseChinese
                ? "死亡后自动按键(仅SkyHook模式)"
                : "Auto-press key on death(Only SkyHook Mode)";

            bool newEnableDeathKey = UIUtils.M3Switch(EnableDeathKey, deathKeySwitchText);
            if (newEnableDeathKey != EnableDeathKey)
            {
                EnableDeathKey = newEnableDeathKey;
            }

            if (EnableDeathKey)
            {
                GUILayout.Space(6);

                // 分隔线
                Color originalColor = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                GUI.color = originalColor;

                GUILayout.Space(4);

                // 延迟时间设置
                GUILayout.BeginHorizontal();
                DeathKeyDelay = UIUtils.M3HorizontalSliderWithLabelAndInput(
                    UseChinese ? "延迟秒数" : "Delay (seconds)",
                    DeathKeyDelay, 0.1f, 30f,
                    ref _deathKeyDelayState.input, ref _deathKeyDelayState.focused, "F1", 140, 200, 60);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 按键选择
                GUILayout.BeginHorizontal();
                string keyLabel = UseChinese ? "按键" : "Key";
                GUILayout.Label(keyLabel, UIUtils.LabelStyle, GUILayout.Width(80));

                // 按键输入框
                string newDeathKeyInput = GUILayout.TextField(DeathKeyInput, UIUtils.TextFieldStyle, GUILayout.Width(100));
                if (newDeathKeyInput != DeathKeyInput)
                {
                    DeathKeyInput = newDeathKeyInput;
                }

                GUILayout.Space(10);

                // 显示当前按键代码
                GUIStyle codeStyle = new(UIUtils.LabelStyle);
                codeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                codeStyle.fontSize = 10;
                GUILayout.Label($"0x{DeathKeyCode:X2}", codeStyle);

                GUILayout.FlexibleSpace();

                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // 常用按键快捷按钮
                GUILayout.BeginHorizontal();
                string[] commonKeys = ["R", "SPACE", "ENTER", "F2", "ESC"];
                foreach (string key in commonKeys)
                {
                    if (GUILayout.Button(key, UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                    {
                        DeathKeyInput = key;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(2);

                // 提示信息
                GUIStyle tipStyle = new(UIUtils.LabelStyle);
                tipStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
                tipStyle.fontSize = 10;
                tipStyle.wordWrap = true;

                string tipText = UseChinese
                    ? "提示：可直接输入字母、数字、F1-F12或特殊键名（如SPACE、ENTER），也可输入十六进制代码（如0x52）"
                    : "Tip: Enter letters, numbers, F1-F12, or special keys (like SPACE, ENTER), or hex code (e.g., 0x52)";
                GUILayout.Label(tipText, tipStyle);

                GUILayout.Space(4);
            }

            ChangeNoFaillInPlay =  UIUtils.M3Switch(ChangeNoFaillInPlay, UseChinese ? "游戏中允许切换失败模式" : "The game allows switching to failure mode");
            ChangeJudementInPlay =  UIUtils.M3Switch(ChangeJudementInPlay, UseChinese ? "游戏中允许切换判定" : "Switching Judement is allowed in the game");
            bool NewLockLevelEditor = UIUtils.M3Switch(LockLevelEditor, UseChinese ? "锁定关卡编辑器（防止误操作）" : "Lock Level Editor (prevent misoperation)");
            if (LockLevelEditor != NewLockLevelEditor)
            {
                LockLevelEditor = NewLockLevelEditor;
                if (ADOBase.sceneName == GCNS.sceneEditor)
                    ADOBase.controller.Restart();
            }
            GUILayout.EndVertical();
        }

        private void DrawAuthorCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);

            // 创建淡色样式
            GUIStyle authorStyle = new(UIUtils.LabelStyle);
            authorStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.8f);
            authorStyle.richText = true;
            authorStyle.alignment = TextAnchor.MiddleLeft;

            // 作者信息横排 - 紧凑版
            GUILayout.BeginHorizontal();
            GUILayout.Label($"👤 {Main.Mod.Info.Author}", authorStyle);
            GUILayout.FlexibleSpace();

            // 在版本号旁边显示测试版标记
            string cleanTitle = Main.Mod.Info.Version.Replace('\n', ' ').Replace('\r', ' ');
            string versionText = $"📦 {cleanTitle}";
            GUILayout.Label(versionText, authorStyle);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"📧 {(UseChinese ? "hitmargin@qq.com" : "hitmargin@Outlook.com")}", authorStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 分隔线
            Color originalColor = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUILayout.Box("", GUILayout.Height(10), GUILayout.ExpandWidth(true));
            GUI.color = originalColor;

            GUILayout.Space(4);

            // 感谢语 - 使用更淡的颜色
            GUIStyle thanksStyle = new(UIUtils.LabelStyle);
            thanksStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.4f);
            thanksStyle.fontSize = 9;
            thanksStyle.alignment = TextAnchor.MiddleCenter;

            GUILayout.Label(UseChinese ? "❤️ 感谢使用 BaseMacro" : "❤️ Thanks for using BaseMacro", thanksStyle);

            GUILayout.EndVertical();
        }

        private void DrawBetaCard()
        {
            GUILayout.BeginHorizontal();
            // 创建测试版样式（更显眼的颜色）
            GUIStyle betaStyle = new(UIUtils.LabelStyle);
            betaStyle.normal.textColor = new Color(1f, 0.5f, 0f, 0.9f); // 橙色
            betaStyle.fontSize = 12;
            betaStyle.fontStyle = FontStyle.Bold;
            betaStyle.alignment = TextAnchor.MiddleLeft;
            betaStyle.richText = true;

            // 测试版水印
            string betaText = UseChinese ?
                $"⚠️ 测试版本 {BetaVersion} - 功能可能不稳定，请谨慎使用 ⚠️" :
                $"⚠️ Beta Version {BetaVersion} - Features may be unstable, use with caution ⚠️";

            GUILayout.Label(betaText, betaStyle);

            // 可选：添加反馈提示
            GUIStyle feedbackStyle = new(UIUtils.LabelStyle);
            feedbackStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            feedbackStyle.fontSize = 10;
            feedbackStyle.alignment = TextAnchor.MiddleRight;

            string feedbackText = UseChinese ?
                "如遇问题请通过邮箱反馈，感谢您的测试！" :
                "Please report issues via email. Thank you for testing!";

            GUILayout.Space(4);
            GUILayout.Label(feedbackText, feedbackStyle);
            GUILayout.EndHorizontal();
        }

        private Vector2 _updateLogScrollPos;

        private void DrawUpdateLogCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "更新日志" : "What's New", UIUtils.HeaderStyle);
            GUILayout.Space(4);

            // 固定高度滚动视图，避免日志过长撑高卡片
            float scrollViewHeight = 150;
            _updateLogScrollPos = GUILayout.BeginScrollView(_updateLogScrollPos, GUILayout.Height(scrollViewHeight));

            // 日志样式：支持换行和富文本
            GUIStyle logStyle = new(UIUtils.LabelStyle)
            {
                wordWrap = true,
                richText = true
            };
            string cleanTitle = Main.Mod.Info.Version.Replace('\n', ' ').Replace('\r', ' ');
            // 更新内容
            string logText = UseChinese ?
                $"<b>版本 {cleanTitle}</b>\n• 优化 UI 布局\n• 修复若干 bug\n• 支持手法模拟" :
                $"<b>Version {cleanTitle}</b>\n• Improved UI layout\n• Fixed several bugs\n• Support technique simulation";

            GUILayout.Label(logText, logStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        // 手法模拟输入框状态
        private (string input, bool focused) _techLeftKeysState = (string.Empty, false);
        private (string input, bool focused) _techRightKeysState = (string.Empty, false);
        private (string input, bool focused) _techLeftPressTimesState = (string.Empty, false);
        private (string input, bool focused) _techRightPressTimesState = (string.Empty, false);
        private (string input, bool focused) _techLeftOrdersState = (string.Empty, false);
        private (string input, bool focused) _techRightOrdersState = (string.Empty, false);

        private List<TechniqueProfile> _techniqueProfiles = new List<TechniqueProfile>();
        public List<TechniqueProfile> TechniqueProfiles
        {
            get => _techniqueProfiles;
            set => _techniqueProfiles = value;
        }

        private int _selectedTechniqueProfileIndex = 0;
        public int SelectedTechniqueProfileIndex
        {
            get => _selectedTechniqueProfileIndex;
            set
            {
                if (_selectedTechniqueProfileIndex == value) return;
                _selectedTechniqueProfileIndex = value;
                // 当索引改变时，将配置值加载到旧字段（用于 UI 显示）
                LoadTechniqueProfileToFields(value);
            }
        }

        private void LoadTechniqueProfileToFields(int index)
        {
            if (index < 0 || index >= _techniqueProfiles.Count) return;
            var p = _techniqueProfiles[index];
            TechLeftHandKeys = p.leftHandKeys;
            TechRightHandKeys = p.rightHandKeys;
            TechLeftHandOrders = p.leftHandOrders;
            TechRightHandOrders = p.rightHandOrders;
            TechLeftHandPressTimes = p.leftHandPressTimes;
            TechRightHandPressTimes = p.rightHandPressTimes;
            TechniqueHandPreference = p.handPreference;
        }

        private void SaveCurrentToProfile(int index)
        {
            if (index < 0 || index >= _techniqueProfiles.Count) return;
            var p = _techniqueProfiles[index];
            p.leftHandKeys = TechLeftHandKeys;
            p.rightHandKeys = TechRightHandKeys;
            p.leftHandOrders = TechLeftHandOrders;
            p.rightHandOrders = TechRightHandOrders;
            p.leftHandPressTimes = TechLeftHandPressTimes;
            p.rightHandPressTimes = TechRightHandPressTimes;
            p.handPreference = TechniqueHandPreference;
        }

        // DEBUG模式下使用的选项
#if DEBUG
        private bool _useCppTechniqueInDebug = true; // 默认使用C++版本
        public bool UseCppTechniqueInDebug
        {
            get => _useCppTechniqueInDebug;
            set
            {
                if (_useCppTechniqueInDebug == value) return;
                _useCppTechniqueInDebug = value;
            }
        }
#endif

        private void DrawTechniqueSimCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "手法模拟" : "Technique Simulation", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            // 检查DLL状态
            bool dllLoaded = TechniqueSimulator.IsDllLoaded();

            GUILayout.BeginHorizontal();

#if DEBUG
            // DEBUG模式显示版本信息和开关
            GUILayout.BeginVertical(); // 垂直布局容纳多行

            // 第一行：调试模式状态
            GUILayout.BeginHorizontal();
            string versionInfo = UseChinese
                ? $"🔧 调试模式 - {(dllLoaded ? "DLL可用" : "DLL不可用")}"
                : $"🔧 Debug Mode - {(dllLoaded ? "DLL Available" : "DLL Unavailable")}";
            GUIStyle versionStyle = new(UIUtils.LabelStyle);
            versionStyle.normal.textColor = new Color(0.3f, 0.6f, 1f, 0.8f);
            GUILayout.Label(versionInfo, versionStyle);
            GUILayout.EndHorizontal();

            // 第二行：C++/C#切换开关（仅当DLL可用时显示）
            if (dllLoaded)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20); // 缩进

                bool newUseCpp = UIUtils.M3Switch(UseCppTechniqueInDebug,
                    UseChinese ? "使用C++版本" : "Use C++ Version");
                if (newUseCpp != UseCppTechniqueInDebug)
                {
                    UseCppTechniqueInDebug = newUseCpp;
                    // 可以在这里添加提示
                    ADOFAIMacro.Macro.Macro.Log($"[Macro] 手法模拟切换到{(newUseCpp ? "C++" : "C#")}版本");
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                // DLL不可用时显示提示
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUIStyle warnStyle = new(UIUtils.LabelStyle);
                warnStyle.normal.textColor = new Color(0.8f, 0.5f, 0.5f, 0.8f);
                warnStyle.fontSize = 10;
                GUILayout.Label(UseChinese
                    ? "⚠️ DLL不可用，将使用C#版本"
                    : "⚠️ DLL unavailable, using C# version",
                    warnStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical(); // 结束垂直布局

#else
            // 正式版只显示状态
            string dllStatus = dllLoaded
                ? (UseChinese ? "✅ C++原生DLL已加载" : "✅ C++ Native DLL Loaded")
                : (UseChinese ? "❌ 检测不到手法模拟库" : "❌ Technique simulation library not found");
            if (!dllLoaded)
                Main.Settings.EnableTechniqueSimulation = false; // 强制禁用

            GUIStyle statusStyle = new(UIUtils.LabelStyle);
            statusStyle.normal.textColor = dllLoaded ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f);
            GUILayout.Label(dllStatus, statusStyle);
#endif

            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // 保存GUI状态
            bool oldEnabled = GUI.enabled;

#if !DEBUG
            // 正式版且DLL未加载时禁用
            if (!dllLoaded)
            {
                GUI.enabled = false;
            }
#endif

            bool newEnable = UIUtils.M3Switch(EnableTechniqueSimulation,
                UseChinese ? "启用手法模拟（左右手交替）" : "Enable Technique Simulation (L/R alternation)");

            // 恢复GUI状态
            GUI.enabled = oldEnabled;

#if !DEBUG
            // 只有启用时才允许修改
            if (dllLoaded && newEnable != EnableTechniqueSimulation)
            {
                EnableTechniqueSimulation = newEnable;
            }
#else
            if (newEnable != EnableTechniqueSimulation)
            {
            EnableTechniqueSimulation = newEnable;
            }
#endif

            // 如果DLL未加载且是正式版，显示提示信息后直接返回
#if !DEBUG
            if (!dllLoaded)
            {
                GUILayout.Space(4);
                GUIStyle warningStyle = new(UIUtils.LabelStyle)
                {
                    fontSize = 11,
                    wordWrap = true,
                    normal = { textColor = new Color(0.8f, 0.3f, 0.3f, 0.8f) }
                };
                GUILayout.Label(UseChinese
                    ? "请将 TechniqueSimulator.dll 放在 Mods/BaseMacro/ 目录下"
                    : "Please place TechniqueSimulator.dll in Mods/BaseMacro/ directory",
                    warningStyle);
                GUILayout.EndVertical();
                return;
            }
#endif

            if (!EnableTechniqueSimulation)
            {
                GUILayout.EndVertical();
                return;
            }

            // 确保至少有一个配置
            if (_techniqueProfiles.Count == 0)
            {
                _techniqueProfiles.Add(new TechniqueProfile());
                LoadTechniqueProfileToFields(0);
            }

            GUILayout.Space(6);
            Color orig = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUI.color = orig;
            GUILayout.Space(4);

            // ── 配置管理区域 ─────────────────────────────────────
            GUILayout.BeginHorizontal();

            // 配置名称输入框
            GUILayout.Label(UseChinese ? "配置名称" : "Profile Name", UIUtils.LabelStyle, GUILayout.Width(100));
            string newName = GUILayout.TextField(_techniqueProfiles[SelectedTechniqueProfileIndex].name, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
            if (newName != _techniqueProfiles[SelectedTechniqueProfileIndex].name)
            {
                _techniqueProfiles[SelectedTechniqueProfileIndex].name = newName;
            }

            // 新建按钮
            if (GUILayout.Button(UseChinese ? "新建" : "New", UIUtils.ButtonStyle, GUILayout.Width(60)))
            {
                var newProfile = _techniqueProfiles[SelectedTechniqueProfileIndex].Clone();
                _techniqueProfiles.Add(newProfile);
                SelectedTechniqueProfileIndex = _techniqueProfiles.Count - 1; // 自动切换到新配置
            }

            // 删除按钮
            if (GUILayout.Button(UseChinese ? "删除" : "Delete", UIUtils.ButtonStyle, GUILayout.Width(60)))
            {
                if (_techniqueProfiles.Count > 1)
                {
                    _techniqueProfiles.RemoveAt(SelectedTechniqueProfileIndex);
                    SelectedTechniqueProfileIndex = Mathf.Clamp(SelectedTechniqueProfileIndex - 1, 0, _techniqueProfiles.Count - 1);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 配置下拉选择
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "选择配置" : "Select Profile", UIUtils.LabelStyle, GUILayout.Width(100));
            string[] profileNames = _techniqueProfiles.Select(p => p.name).ToArray();
            int newIndex = UIUtils.M3SelectionGrid(SelectedTechniqueProfileIndex, profileNames, Mathf.Min(profileNames.Length, 4), GUILayout.ExpandWidth(true));
            if (newIndex != SelectedTechniqueProfileIndex)
            {
                // 切换前自动保存当前修改（已实时同步，无需额外操作）
                SelectedTechniqueProfileIndex = newIndex;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 起始手偏好
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "起始手" : "Starting Hand", UIUtils.LabelStyle, GUILayout.Width(140));
            string[] handOptions = UseChinese ? ["左手", "右手"] : ["Left", "Right"];
            int newHandPref = UIUtils.M3SelectionGrid(TechniqueHandPreference, handOptions, 2, GUILayout.Width(200));
            if (newHandPref != TechniqueHandPreference)
            {
                TechniqueHandPreference = newHandPref;
                // 同步到当前配置
                _techniqueProfiles[SelectedTechniqueProfileIndex].handPreference = newHandPref;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // 速度阈值
            GUILayout.BeginHorizontal();
            TechniqueBpmLimit = UIUtils.M3HorizontalSliderWithLabelAndInput(
                UseChinese ? "速度阈值 (BPM)" : "Speed Threshold (BPM)",
                TechniqueBpmLimit, 50f, 2000f,
                ref _techBpmState.input, ref _techBpmState.focused,
                "F0", 140, 220, 70);
            GUILayout.EndHorizontal();

            GUIStyle tipStyle = new(UIUtils.LabelStyle)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 0.8f) }
            };
            GUILayout.Label(UseChinese
                ? "超过此BPM时自动细分时间片，允许同一只手连续承担多个事件"
                : "Above this BPM, time slices are subdivided so one hand handles multiple events", tipStyle);

            GUILayout.Space(8);

            // 左右手按键配置
            string[] handLabels = UseChinese ? ["左手", "右手"] : ["Left Hand", "Right Hand"];

            // 左手
            GUILayout.Label($"── {handLabels[0]} ──", UIUtils.LabelStyle);
            GUILayout.Space(2);

            // 左手按键序列
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键序列:" : "Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLeftKeys = UIUtils.M3TextField(TechLeftHandKeys,
                ref _techLeftKeysState.input,
                ref _techLeftKeysState.focused,
                UIUtils.TextFieldStyle,
                "TechLeftKeys");
            if (newLeftKeys != TechLeftHandKeys)
            {
                TechLeftHandKeys = newLeftKeys;
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = newLeftKeys;
            }
            GUILayout.EndHorizontal();

            // 左手时长比例
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "时长比例:" : "Press Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLeftPress = UIUtils.M3TextField(TechLeftHandPressTimes,
                ref _techLeftPressTimesState.input,
                ref _techLeftPressTimesState.focused,
                UIUtils.TextFieldStyle,
                "TechLeftPressTimes");
            if (newLeftPress != TechLeftHandPressTimes)
            {
                TechLeftHandPressTimes = newLeftPress;
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandPressTimes = newLeftPress;
            }
            GUILayout.EndHorizontal();

            // 左手按键顺序
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键顺序:" : "Key Order:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLeftOrder = UIUtils.M3TextField(TechLeftHandOrders,
                ref _techLeftOrdersState.input,
                ref _techLeftOrdersState.focused,
                UIUtils.TextFieldStyle,
                "TechLeftOrders");
            if (newLeftOrder != TechLeftHandOrders)
            {
                TechLeftHandOrders = newLeftOrder;
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandOrders = newLeftOrder;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 右手
            GUILayout.Label($"── {handLabels[1]} ──", UIUtils.LabelStyle);
            GUILayout.Space(2);

            // 右手按键序列
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键序列:" : "Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRightKeys = UIUtils.M3TextField(TechRightHandKeys,
                ref _techRightKeysState.input,
                ref _techRightKeysState.focused,
                UIUtils.TextFieldStyle,
                "TechRightKeys");
            if (newRightKeys != TechRightHandKeys)
            {
                TechRightHandKeys = newRightKeys;
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = newRightKeys;
            }
            GUILayout.EndHorizontal();

            // 右手时长比例
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "时长比例:" : "Press Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRightPress = UIUtils.M3TextField(TechRightHandPressTimes,
                ref _techRightPressTimesState.input,
                ref _techRightPressTimesState.focused,
                UIUtils.TextFieldStyle,
                "TechRightPressTimes");
            if (newRightPress != TechRightHandPressTimes)
            {
                TechRightHandPressTimes = newRightPress;
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandPressTimes = newRightPress;
            }
            GUILayout.EndHorizontal();

            // 右手按键顺序
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键顺序:" : "Key Order:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRightOrder = UIUtils.M3TextField(TechRightHandOrders,
                ref _techRightOrdersState.input,
                ref _techRightOrdersState.focused,
                UIUtils.TextFieldStyle,
                "TechRightOrders");
            if (newRightOrder != TechRightHandOrders)
            {
                TechRightHandOrders = newRightOrder;
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandOrders = newRightOrder;
            }
            GUILayout.EndHorizontal();

            // 常用预设
            GUILayout.Space(6);
            GUILayout.Label(UseChinese ? "预设:" : "Presets:", UIUtils.LabelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("DF / JK", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "D,F";
                TechRightHandKeys = "J,K";
                // 同步到配置
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = "D,F";
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = "J,K";
            }
            if (GUILayout.Button("DS / JK", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "D,S";
                TechRightHandKeys = "J,K";
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = "D,S";
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = "J,K";
            }
            if (GUILayout.Button("ASDF / JKL", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "A,S,D,F";
                TechRightHandKeys = "J,K,L";
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = "A,S,D,F";
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = "J,K,L";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label(UseChinese
                ? "按键顺序格式：用 | 分隔不同按键数，逗号分隔键序号(1-based)。留空=默认顺序。"
                : "Order format: pipe separates key-count groups, comma separates indices (1-based). Empty = default.",
                tipStyle);

            GUILayout.EndVertical();
        }

        public void OnSaveGUI(UnityModManager.ModEntry modEntry) => Save(modEntry);
        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
        public static Settings Load(UnityModManager.ModEntry modEntry) => Load<Settings>(modEntry);
    }
}