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
    /// Mod settings class / Mod 设置类
    /// </summary>
    public class Settings : UnityModManager.ModSettings
    {
        // ─────────────────────────────────────────────
        //  手法配置文件
        // ─────────────────────────────────────────────
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
            public List<TechniqueSegment> techniqueSegments = [];

            public TechniqueProfile() { }

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
                    handPreference = this.handPreference,
                    techniqueSegments = [.. this.techniqueSegments.Select(s => new TechniqueSegment {
                        startFloor          = s.startFloor,
                        endFloor            = s.endFloor,
                        bpmLimit            = s.bpmLimit,
                        leftHandKeys        = s.leftHandKeys,
                        rightHandKeys       = s.rightHandKeys,
                        leftHandOrders      = s.leftHandOrders,
                        rightHandOrders     = s.rightHandOrders,
                        leftHandPressTimes  = s.leftHandPressTimes,
                        rightHandPressTimes = s.rightHandPressTimes,
                    })]
                };
            }
        }

        // ─────────────────────────────────────────────
        //  变速分段（含可选按键覆盖）
        // ─────────────────────────────────────────────
        [Serializable]
        public class TechniqueSegment
        {
            public int startFloor;
            public int endFloor;
            public float bpmLimit;

            // 可选按键覆盖（留空 = 继承全局配置）
            public string leftHandKeys = "";
            public string rightHandKeys = "";
            public string leftHandOrders = "";
            public string rightHandOrders = "";
            public string leftHandPressTimes = "";
            public string rightHandPressTimes = "";

            /// <summary>任一手的按键字段非空即视为有覆盖</summary>
            public bool HasKeyOverride =>
                !string.IsNullOrWhiteSpace(leftHandKeys) ||
                !string.IsNullOrWhiteSpace(rightHandKeys);
        }

        // ─────────────────────────────────────────────
        //  UI 内部状态
        // ─────────────────────────────────────────────
        public List<TechniqueSegment> techniqueSegments = [];

        private class SegmentEditState
        {
            public string startInput = "";
            public bool startFocused;
            public string endInput = "";
            public bool endFocused;
            public string bpmInput = "";
            public bool bpmFocused;
        }
        private List<SegmentEditState> _segmentEditStates = [];
        private List<bool> _segmentExpanded = [];

        // ─────────────────────────────────────────────
        //  基础设置属性
        // ─────────────────────────────────────────────
        public event Action<bool> OnMacroChanged;

        private bool _useChinese = true;
        public bool UseChinese
        {
            get => _useChinese;
            set { if (_useChinese == value) return; _useChinese = value; }
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

        private string _macroKeys = "D,F,J,K";
        public string MacroKeys
        {
            get => _macroKeys;
            set { if (_macroKeys == value) return; _macroKeys = value; }
        }

        private bool _simulateKeyPress = false;
        public bool SimulateKeyPress
        {
            get => _simulateKeyPress;
            set { if (_simulateKeyPress == value) return; _simulateKeyPress = value; }
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
            set { if (_skyHookMode == value) return; _skyHookMode = value; }
        }

        private (string input, bool focused) _adjustStepState = (string.Empty, false);
        private (string input, bool focused) _timeOffsetState = (string.Empty, false);

        private bool _highPrecisionAsync = false;
        public bool HighPrecisionAsync
        {
            get => _highPrecisionAsync;
            set { if (_highPrecisionAsync == value) return; _highPrecisionAsync = value; }
        }

        // ── 版本信息 ──────────────────────────────────
        private int? _betaVersion = null;
        public int BetaVersion
        {
            get
            {
                if (_betaVersion == null) _betaVersion = GetBetaVersionFromAssembly();
                return _betaVersion.Value;
            }
        }
        public bool IsBeta => BetaVersion > 0;

        private int GetBetaVersionFromAssembly()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version.Revision; }
            catch { return 0; }
        }

        // ── 输入模式 ──────────────────────────────────
        private int _inputMode = 0;
        public int InputMode
        {
            get => _inputMode;
            set
            {
                if (_inputMode == value) return;
                _inputMode = value;
                if (ADOFAIMacro.Macro.InputSystem.IsInitialized)
                    ADOFAIMacro.Macro.InputSystem.SetInputMode((Macro.InputMode)value);
            }
        }

        // ── 死亡按键 ──────────────────────────────────
        private bool _enableDeathKey = false;
        public bool EnableDeathKey
        {
            get => _enableDeathKey;
            set { if (_enableDeathKey == value) return; _enableDeathKey = value; }
        }

        private float _deathKeyDelay = 5f;
        public float DeathKeyDelay
        {
            get => _deathKeyDelay;
            set => _deathKeyDelay = Mathf.Clamp(value, 0.1f, 30f);
        }

        private int _deathKeyCode = 0x52;
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
                int? code = GetKeyCodeFromString(_deathKeyInput);
                if (code.HasValue) _deathKeyCode = code.Value;
            }
        }

        public bool ChangeNoFaillInPlay = false;
        public bool ChangeJudementInPlay = false;
        public bool LockLevelEditor = false;

        // ─────────────────────────────────────────────
        //  手法模拟全局设置
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

        public string TechLeftHandKeys = "D,F";
        public string TechRightHandKeys = "J,K";
        public string TechLeftHandOrders = "";
        public string TechRightHandOrders = "";
        public string TechLeftHandPressTimes = "0.8,0.8";
        public string TechRightHandPressTimes = "0.8,0.8";

        private (string input, bool focused) _techBpmState = (string.Empty, false);
        private (string input, bool focused) _deathKeyDelayState = (string.Empty, false);

        // ── 按键过滤 ──────────────────────────────────
        private bool _enableKeyFilter = false;
        public bool EnableKeyFilter
        {
            get => _enableKeyFilter;
            set { if (_enableKeyFilter == value) return; _enableKeyFilter = value; }
        }

        private int _filterMode = 0;
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

        // ── 配置选项卡索引 ─────────────────────────────
        private int selectedCardIndex = 0;

        // ── 手法起始手 ────────────────────────────────
        private int _techniqueHandPreference = 1;
        public int TechniqueHandPreference
        {
            get => _techniqueHandPreference;
            set { if (_techniqueHandPreference == value) return; _techniqueHandPreference = value; }
        }

        // ── 手法输入框状态 ────────────────────────────
        private (string input, bool focused) _techLeftKeysState = (string.Empty, false);
        private (string input, bool focused) _techRightKeysState = (string.Empty, false);
        private (string input, bool focused) _techLeftPressTimesState = (string.Empty, false);
        private (string input, bool focused) _techRightPressTimesState = (string.Empty, false);
        private (string input, bool focused) _techLeftOrdersState = (string.Empty, false);
        private (string input, bool focused) _techRightOrdersState = (string.Empty, false);

        // ── 配置列表 ──────────────────────────────────
        private List<TechniqueProfile> _techniqueProfiles = [];
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

#if DEBUG
        private bool _useCppTechniqueInDebug = true;
        public bool UseCppTechniqueInDebug
        {
            get => _useCppTechniqueInDebug;
            set { if (_useCppTechniqueInDebug == value) return; _useCppTechniqueInDebug = value; }
        }
#endif

        // ─────────────────────────────────────────────
        //  按键代码映射
        // ─────────────────────────────────────────────
        private static readonly Dictionary<string, int> KeyCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            {"A",0x41},{"B",0x42},{"C",0x43},{"D",0x44},{"E",0x45},{"F",0x46},
            {"G",0x47},{"H",0x48},{"I",0x49},{"J",0x4A},{"K",0x4B},{"L",0x4C},
            {"M",0x4D},{"N",0x4E},{"O",0x4F},{"P",0x50},{"Q",0x51},{"R",0x52},
            {"S",0x53},{"T",0x54},{"U",0x55},{"V",0x56},{"W",0x57},{"X",0x58},
            {"Y",0x59},{"Z",0x5A},
            {"0",0x30},{"1",0x31},{"2",0x32},{"3",0x33},{"4",0x34},
            {"5",0x35},{"6",0x36},{"7",0x37},{"8",0x38},{"9",0x39},
            {"F1",0x70},{"F2",0x71},{"F3",0x72},{"F4",0x73},{"F5",0x74},
            {"F6",0x75},{"F7",0x76},{"F8",0x77},{"F9",0x78},{"F10",0x79},
            {"F11",0x7A},{"F12",0x7B},
            {"SPACE",0x20},{"ENTER",0x0D},{"RETURN",0x0D},{"ESC",0x1B},
            {"TAB",0x09},{"SHIFT",0x10},{"CTRL",0x11},{"ALT",0x12},
            {"BACKSPACE",0x08},{"DELETE",0x2E},{"INSERT",0x2D},
            {"HOME",0x24},{"END",0x23},{"PAGEUP",0x21},{"PAGEDOWN",0x22},
            {"UP",0x26},{"DOWN",0x28},{"LEFT",0x25},{"RIGHT",0x27}
        };

        private int? GetKeyCodeFromString(string keyString)
        {
            if (string.IsNullOrEmpty(keyString)) return null;
            if (keyString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                if (int.TryParse(keyString.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hex))
                    return hex;
            if (KeyCodeMap.TryGetValue(keyString, out int code)) return code;
            return null;
        }

        // ─────────────────────────────────────────────
        //  OnGUI 主入口
        // ─────────────────────────────────────────────
        public void OnGUI(UnityModManager.ModEntry modEntry)
        {
            UIUtils.InitializeStyles();

            var cards = new List<(string name, Action draw)>();
            cards.Add((UseChinese ? "语言" : "Language", DrawLanguageCard));
            cards.Add((UseChinese ? "宏" : "Macro", DrawMainSwitchCard));

            if (Macro)
            {
                cards.Add((UseChinese ? "按键设置" : "Key Settings", DrawKeySettingsCard));
                cards.Add((UseChinese ? "按键过滤" : "Key Filter", DrawKeyFilterCard));
                cards.Add((UseChinese ? "延迟设置" : "Offset Settings", DrawOffsetSettingsCard));
                cards.Add((UseChinese ? "其他选项" : "Other Settings", DrawOtherSettingsCard));

                if (SimulateKeyPress)
                    cards.Add((UseChinese ? "手法模拟" : "Technique Simulation", DrawTechniqueSimCard));
            }

            cards.Add((UseChinese ? "更新日志" : "Update Log", DrawUpdateLogCard));
            cards.Add((UseChinese ? "作者" : "Author", DrawAuthorCard));

            if (IsBeta)
                cards.Add((UseChinese ? "测试版" : "Beta", DrawBetaCard));

            if (selectedCardIndex >= cards.Count) selectedCardIndex = 0;

            string[] names = cards.Select(c => c.name).ToArray();
            selectedCardIndex = UIUtils.M3SelectionGrid(selectedCardIndex, names, cards.Count, GUILayout.Height(30));
            GUILayout.Space(10);

            if (cards.Count > 0) cards[selectedCardIndex].draw();
        }

        // ─────────────────────────────────────────────
        //  语言卡
        // ─────────────────────────────────────────────
        private void DrawLanguageCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "语言" : "Language", UIUtils.HeaderStyle);
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "显示语言" : "Display Language", UIUtils.LabelStyle, GUILayout.Width(150));
            string[] langs = ["中文", "English"];
            int sel = UseChinese ? 0 : 1;
            int newSel = UIUtils.M3SelectionGrid(sel, langs, 2, GUILayout.Width(200));
            if (newSel != sel) UseChinese = newSel == 0;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  宏主开关卡
        // ─────────────────────────────────────────────
        private void DrawMainSwitchCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "宏" : "Macro", UIUtils.HeaderStyle);
            bool newMacro = UIUtils.M3Switch(Macro, UseChinese ? "启用宏" : "Enable Macro");
            if (newMacro != Macro) { Macro = newMacro; ADOBase.controller.Restart(); }
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  延迟设置卡
        // ─────────────────────────────────────────────
        private void DrawOffsetSettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "延迟设置" : "Offset Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            EnableKeyAdjust = UIUtils.M3Switch(EnableKeyAdjust,
                UseChinese ? "允许Ctrl+左右键调整步长偏移(游戏中)" : "Allow adjusting step offset using Ctrl and arrow keys (in-game)");
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            AdjustStep = UIUtils.M3HorizontalSliderWithLabelAndInput(
                UseChinese ? "调整步长" : "Adjust Step", AdjustStep, 0.1f, 10f,
                ref _adjustStepState.input, ref _adjustStepState.focused, "F2", 120, 240, 60);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            TimeOffset = UIUtils.M3HorizontalSliderWithLabelAndInput(
                UseChinese ? "延迟 (ms)" : "Offset (ms)", TimeOffset, -100f, 100f,
                ref _timeOffsetState.input, ref _timeOffsetState.focused, "F2", 120, 240, 60);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            EnableArrowTimeAdjust = UIUtils.M3Switch(EnableArrowTimeAdjust,
                UseChinese ? "允许左右键调整延迟(游戏中)" : "Allow adjustment of delay using left and right keys (in-game)");
            GUILayout.Space(2);
            HighPrecisionTime = UIUtils.M3Switch(HighPrecisionTime,
                UseChinese ? "启用高精度时间（提高同步精度）" : "Enable High Precision Time (improves sync accuracy)");
            GUILayout.Space(2);
            HighPrecisionAsync = UIUtils.M3Switch(HighPrecisionAsync,
                UseChinese ? "[实验性]启用高精度异步" : "[Experimental]Enable High Precision Async");
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  按键设置卡
        // ─────────────────────────────────────────────
        private void DrawKeySettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "按键设置" : "Key Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            if (!Main.Settings.EnableTechniqueSimulation)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "按键序列 (逗号分隔)" : "Keys (comma separated)",
                    UIUtils.LabelStyle, GUILayout.Width(180));
                MacroKeys = GUILayout.TextField(MacroKeys, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(2);
            bool newSim = UIUtils.M3Switch(SimulateKeyPress, UseChinese ? "按键模拟" : "Key simulation");
            if (newSim != SimulateKeyPress) { SimulateKeyPress = newSim; ADOBase.controller.Restart(); }

            if (SimulateKeyPress)
            {
                GUILayout.Space(2);
                SkyHookMode = UIUtils.M3Switch(SkyHookMode,
                    UseChinese ? "使用高级输入(否则使用SendInput API)" : "Use advanced input (if closed, use SendInput API)");

                if (SkyHookMode)
                {
                    GUILayout.Space(6);
                    Color oc = GUI.color;
                    GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                    GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                    GUI.color = oc;
                    GUILayout.Space(4);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(UseChinese ? "Win API 输入模式" : "Win API Input Mode",
                        UIUtils.LabelStyle, GUILayout.Width(150));
                    if (InputSystem.IsInitialized)
                    {
                        var actual = InputSystem.GetInputMode();
                        GUIStyle hintStyle = new(UIUtils.LabelStyle);
                        hintStyle.normal.textColor = new Color(0.5f, 0.9f, 0.5f, 0.8f);
                        hintStyle.fontSize = 10;
                        GUILayout.Label(UseChinese
                            ? $"[实际: {GetModeLabel(actual, true)}]"
                            : $"[Active: {GetModeLabel(actual, false)}]", hintStyle);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);

                    bool hasInject = !ADOFAIMacro.Macro.InputSystem.IsInitialized ||
                                     ADOFAIMacro.Macro.InputSystem.IsModeAvailable(ADOFAIMacro.Macro.InputMode.NtUserInjectKeyboard);
                    bool hasNtSend = !ADOFAIMacro.Macro.InputSystem.IsInitialized ||
                                     ADOFAIMacro.Macro.InputSystem.IsModeAvailable(ADOFAIMacro.Macro.InputMode.NtUserSendInput);

                    string[] modeLabels = UseChinese
                        ? ["自动", "NtInject", "NtSendInput ★", "SendInput"]
                        : ["Auto", "NtInject", "NtSendInput ★", "SendInput"];

                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < 4; i++)
                    {
                        bool available = i switch { 1 => hasInject, 2 => hasNtSend, _ => true };
                        string lbl = modeLabels[i] + (available ? "" : (UseChinese ? "(不支持)" : "(N/A)"));
                        if (GUILayout.Button(lbl, UIUtils.ButtonStyle, GUILayout.Height(24)) && available && InputMode != i)
                            InputMode = i;
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    GUIStyle descStyle = new(UIUtils.LabelStyle);
                    descStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);
                    descStyle.fontSize = 10;
                    descStyle.wordWrap = true;
                    GUILayout.Label(InputMode switch
                    {
                        0 => UseChinese ? "自动：优先使用最底层可用方式" : "Auto: use the lowest available layer automatically",
                        1 => UseChinese ? "NtInject（最底层）：直接注入原始输入流" : "NtInject (deepest): inject into raw input stream",
                        2 => UseChinese ? "NtSendInput ★：内核边界注入" : "NtSendInput ★: kernel-boundary injection",
                        3 => UseChinese ? "SendInput：标准 Win32 API，兼容性最佳" : "SendInput: standard Win32 API, best compatibility",
                        _ => ""
                    }, descStyle);
                }
            }
            GUILayout.EndVertical();
        }

        private string GetModeLabel(Macro.InputMode mode, bool chinese) => mode switch
        {
            ADOFAIMacro.Macro.InputMode.Auto => chinese ? "自动" : "Auto",
            ADOFAIMacro.Macro.InputMode.NtUserInjectKeyboard => chinese ? "NtInject" : "NtInject",
            ADOFAIMacro.Macro.InputMode.NtUserSendInput => chinese ? "NtSendInput ★" : "NtSendInput ★",
            ADOFAIMacro.Macro.InputMode.SendInput => chinese ? "SendInput" : "SendInput",
            _ => mode.ToString()
        };

        // ─────────────────────────────────────────────
        //  按键过滤卡
        // ─────────────────────────────────────────────
        private void DrawKeyFilterCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "按键过滤" : "Key Filter", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            bool newEnable = UIUtils.M3Switch(EnableKeyFilter, UseChinese ? "启用按键过滤" : "Enable Key Filter");
            if (newEnable != EnableKeyFilter) EnableKeyFilter = newEnable;

            if (EnableKeyFilter)
            {
                GUILayout.Space(6);
                Color oc = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                GUI.color = oc;
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "过滤模式" : "Filter Mode", UIUtils.LabelStyle, GUILayout.Width(100));
                string[] modes = UseChinese ? ["黑名单模式", "白名单模式"] : ["Blacklist Mode", "Whitelist Mode"];
                int newMode = UIUtils.M3SelectionGrid(FilterMode, modes, 2, GUILayout.Width(200));
                if (newMode != FilterMode) FilterMode = newMode;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUIStyle descStyle = new(UIUtils.LabelStyle);
                descStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                descStyle.fontSize = 11;
                descStyle.wordWrap = true;
                GUILayout.Label(FilterMode == 0
                    ? (UseChinese ? "⛔ 黑名单模式：列表中的按键将被阻止" : "⛔ Blacklist: Keys in the list will be blocked")
                    : (UseChinese ? "✅ 白名单模式：只有列表中的按键允许通过" : "✅ Whitelist: Only keys in the list are allowed"),
                    descStyle);
                GUILayout.Space(8);

                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "按键列表 (逗号分隔)" : "Keys (comma separated)",
                    UIUtils.LabelStyle, GUILayout.Width(140));
                string newFK = UIUtils.M3TextField(FilteredKeys,
                    ref _filteredKeysState.input, ref _filteredKeysState.focused,
                    UIUtils.TextFieldStyle, "TechnicalkeysNormal");
                if (newFK != FilteredKeys) FilteredKeys = newFK;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "异步按键列表 (逗号分隔)" : "Async Keys (comma separated)",
                    UIUtils.LabelStyle, GUILayout.Width(140));
                if (SkyHookMode)
                {
                    string newAK = UIUtils.M3TextField(FilteredAsyncKeys,
                        ref _filteredAsyncKeysState.input, ref _filteredAsyncKeysState.focused,
                        UIUtils.TextFieldStyle, "TechnicalkeysAsync");
                    if (newAK != FilteredAsyncKeys) FilteredAsyncKeys = newAK;
                }
                else
                {
                    GUIStyle dis = new(UIUtils.LabelStyle);
                    dis.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    GUILayout.Label(UseChinese ? "（需开启 SkyHook 模式）" : "(Requires SkyHook Mode)", dis);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label(UseChinese ? "常用按键:" : "Common Keys:", UIUtils.LabelStyle);
                GUILayout.Space(2);

                void QuickSet(string k) { FilteredKeys = k; if (SkyHookMode) FilteredAsyncKeys = k; }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("F1,F2,F3,F4", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("F1,F2,F3,F4");
                if (GUILayout.Button("F5,F6,F7,F8", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("F5,F6,F7,F8");
                if (GUILayout.Button("F9,F10,F11,F12", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("F9,F10,F11,F12");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("A,S,D,F", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("A,S,D,F");
                if (GUILayout.Button("J,K,L", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("J,K,L");
                if (GUILayout.Button("1,2,3,4", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("1,2,3,4");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SPACE,ENTER,ESC", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("SPACE,ENTER,ESC");
                if (GUILayout.Button("UP,DOWN,LEFT,RIGHT", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("UP,DOWN,LEFT,RIGHT");
                if (GUILayout.Button("CTRL,ALT,SHIFT", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true))) QuickSet("CTRL,ALT,SHIFT");
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
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

        // ─────────────────────────────────────────────
        //  其他选项卡
        // ─────────────────────────────────────────────
        private void DrawOtherSettingsCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "其他选项" : "Other Settings", UIUtils.HeaderStyle);
            GUILayout.Space(2);

            bool newDK = UIUtils.M3Switch(EnableDeathKey,
                UseChinese ? "死亡后自动按键(仅SkyHook模式)" : "Auto-press key on death(Only SkyHook Mode)");
            if (newDK != EnableDeathKey) EnableDeathKey = newDK;

            if (EnableDeathKey)
            {
                GUILayout.Space(6);
                Color oc = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                GUI.color = oc;
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                DeathKeyDelay = UIUtils.M3HorizontalSliderWithLabelAndInput(
                    UseChinese ? "延迟秒数" : "Delay (seconds)",
                    DeathKeyDelay, 0.1f, 30f,
                    ref _deathKeyDelayState.input, ref _deathKeyDelayState.focused, "F1", 140, 200, 60);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Label(UseChinese ? "按键" : "Key", UIUtils.LabelStyle, GUILayout.Width(80));
                string newDKI = GUILayout.TextField(DeathKeyInput, UIUtils.TextFieldStyle, GUILayout.Width(100));
                if (newDKI != DeathKeyInput) DeathKeyInput = newDKI;
                GUILayout.Space(10);
                GUIStyle codeStyle = new(UIUtils.LabelStyle);
                codeStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                codeStyle.fontSize = 10;
                GUILayout.Label($"0x{DeathKeyCode:X2}", codeStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                foreach (string k in new[] { "R", "SPACE", "ENTER", "F2", "ESC" })
                    if (GUILayout.Button(k, UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                        DeathKeyInput = k;
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUIStyle tipStyle = new(UIUtils.LabelStyle);
                tipStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
                tipStyle.fontSize = 10;
                tipStyle.wordWrap = true;
                GUILayout.Label(UseChinese
                    ? "提示：可直接输入字母、数字、F1-F12或特殊键名（如SPACE、ENTER），也可输入十六进制代码（如0x52）"
                    : "Tip: Enter letters, numbers, F1-F12, or special keys (like SPACE, ENTER), or hex code (e.g., 0x52)",
                    tipStyle);
                GUILayout.Space(4);
            }

            ChangeNoFaillInPlay = UIUtils.M3Switch(ChangeNoFaillInPlay,
                UseChinese ? "游戏中允许切换失败模式" : "The game allows switching to failure mode");
            ChangeJudementInPlay = UIUtils.M3Switch(ChangeJudementInPlay,
                UseChinese ? "游戏中允许切换判定" : "Switching Judement is allowed in the game");

            bool newLock = UIUtils.M3Switch(LockLevelEditor,
                UseChinese ? "锁定关卡编辑器（防止误操作）" : "Lock Level Editor (prevent misoperation)");
            if (LockLevelEditor != newLock)
            {
                LockLevelEditor = newLock;
                if (ADOBase.sceneName == GCNS.sceneEditor) ADOBase.controller.Restart();
            }
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  作者卡
        // ─────────────────────────────────────────────
        private void DrawAuthorCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUIStyle authorStyle = new(UIUtils.LabelStyle);
            authorStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.8f);
            authorStyle.richText = true;
            authorStyle.alignment = TextAnchor.MiddleLeft;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"👤 {Main.Mod.Info.Author}", authorStyle);
            GUILayout.FlexibleSpace();
            string ver = Main.Mod.Info.Version.Replace('\n', ' ').Replace('\r', ' ');
            GUILayout.Label($"📦 {ver}", authorStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"📧 {(UseChinese ? "hitmargin@qq.com" : "hitmargin@Outlook.com")}", authorStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            Color oc = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            GUILayout.Box("", GUILayout.Height(10), GUILayout.ExpandWidth(true));
            GUI.color = oc;
            GUILayout.Space(4);

            GUIStyle thanksStyle = new(UIUtils.LabelStyle);
            thanksStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.4f);
            thanksStyle.fontSize = 9;
            thanksStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label(UseChinese
                ? $"❤️ 感谢使用 {Main.Mod.Info.Id}"
                : $"❤️ Thanks for using {Main.Mod.Info.Id}", thanksStyle);
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  测试版卡
        // ─────────────────────────────────────────────
        private void DrawBetaCard()
        {
            GUILayout.BeginHorizontal();
            GUIStyle betaStyle = new(UIUtils.LabelStyle);
            betaStyle.normal.textColor = new Color(1f, 0.5f, 0f, 0.9f);
            betaStyle.fontSize = 12;
            betaStyle.fontStyle = FontStyle.Bold;
            betaStyle.alignment = TextAnchor.MiddleLeft;
            betaStyle.richText = true;
            GUILayout.Label(UseChinese
                ? $"⚠️ 测试版本 {BetaVersion} - 功能可能不稳定，请谨慎使用 ⚠️"
                : $"⚠️ Beta Version {BetaVersion} - Features may be unstable, use with caution ⚠️", betaStyle);

            GUIStyle feedbackStyle = new(UIUtils.LabelStyle);
            feedbackStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            feedbackStyle.fontSize = 10;
            feedbackStyle.alignment = TextAnchor.MiddleRight;
            GUILayout.Space(4);
            GUILayout.Label(UseChinese
                ? "如遇问题请通过邮箱反馈，感谢您的测试！"
                : "Please report issues via email. Thank you for testing!", feedbackStyle);
            GUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────
        //  更新日志卡
        // ─────────────────────────────────────────────
        private Vector2 _updateLogScrollPos;
        private void DrawUpdateLogCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "更新日志" : "What's New", UIUtils.HeaderStyle);
            GUILayout.Space(4);
            _updateLogScrollPos = GUILayout.BeginScrollView(_updateLogScrollPos, GUILayout.Height(150));
            GUIStyle logStyle = new(UIUtils.LabelStyle) { wordWrap = true, richText = true };
            string ver = Main.Mod.Info.Version.Replace('\n', ' ').Replace('\r', ' ');
            GUILayout.Label(UseChinese
                ? $"<b>版本 {ver}</b>\n• 手法模拟优化和修复\n• 分段支持按键覆盖"
                : $"<b>Version {ver}</b>\n• Technique Simulation Optimization and Repair\n• Per-segment key override support",
                logStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────
        //  手法模拟主卡
        // ─────────────────────────────────────────────
        private void DrawTechniqueSimCard()
        {
            GUILayout.BeginVertical(UIUtils.CardStyle);
            GUILayout.Label(UseChinese ? "手法模拟" : "Technique Simulation", UIUtils.HeaderStyle);
            GUILayout.Space(2);
            GUILayout.Label(
                UseChinese ? "注：最开始进入游戏需要死亡一次来校准时间"
                           : "Note: The first time you enter the game, you need to die once to calibrate the time.",
                UIUtils.HeaderStyle);
            GUILayout.Space(2);

            bool dllLoaded = TechniqueSimulator.IsDllLoaded();

            GUILayout.BeginHorizontal();
#if DEBUG
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUIStyle verStyle = new(UIUtils.LabelStyle);
            verStyle.normal.textColor = new Color(0.3f, 0.6f, 1f, 0.8f);
            GUILayout.Label(UseChinese
                ? $"🔧 调试模式 - {(dllLoaded ? "DLL可用" : "DLL不可用")}"
                : $"🔧 Debug Mode - {(dllLoaded ? "DLL Available" : "DLL Unavailable")}", verStyle);
            GUILayout.EndHorizontal();

            if (dllLoaded)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                bool newUseCpp = UIUtils.M3Switch(UseCppTechniqueInDebug, UseChinese ? "使用C++版本" : "Use C++ Version");
                if (newUseCpp != UseCppTechniqueInDebug)
                {
                    UseCppTechniqueInDebug = newUseCpp;
                    ADOFAIMacro.Macro.Macro.Log($"[Macro] 手法模拟切换到{(newUseCpp ? "C++" : "C#")}版本");
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUIStyle warnStyle = new(UIUtils.LabelStyle);
                warnStyle.normal.textColor = new Color(0.8f, 0.5f, 0.5f, 0.8f);
                warnStyle.fontSize = 10;
                GUILayout.Label(UseChinese ? "⚠️ DLL不可用，将使用C#版本" : "⚠️ DLL unavailable, using C# version", warnStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
#else
            string dllStatus = dllLoaded
                ? (UseChinese ? "✅ C++原生DLL已加载" : "✅ C++ Native DLL Loaded")
                : (UseChinese ? "❌ 检测不到手法模拟库" : "❌ Technique simulation library not found");
            if (!dllLoaded) Main.Settings.EnableTechniqueSimulation = false;
            GUIStyle statusStyle = new(UIUtils.LabelStyle);
            statusStyle.normal.textColor = dllLoaded ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f);
            GUILayout.Label(dllStatus, statusStyle);
#endif
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            bool oldEnabled = GUI.enabled;
#if !DEBUG
            if (!dllLoaded) GUI.enabled = false;
#endif
            bool newEnable = UIUtils.M3Switch(EnableTechniqueSimulation,
                UseChinese ? "启用手法模拟（左右手交替）" : "Enable Technique Simulation (L/R alternation)");
            GUI.enabled = oldEnabled;

#if !DEBUG
            if (dllLoaded && newEnable != EnableTechniqueSimulation)
                EnableTechniqueSimulation = newEnable;
#else
            if (newEnable != EnableTechniqueSimulation)
                EnableTechniqueSimulation = newEnable;
#endif

#if !DEBUG
            if (!dllLoaded)
            {
                GUILayout.Space(4);
                GUIStyle warnStyle = new(UIUtils.LabelStyle)
                {
                    fontSize = 11,
                    wordWrap = true,
                    normal = { textColor = new Color(0.8f, 0.3f, 0.3f, 0.8f) }
                };
                GUILayout.Label(UseChinese
                    ? "请将 TechniqueSimulator.dll 放在 Mods/BaseMacro/ 目录下"
                    : "Please place TechniqueSimulator.dll in Mods/BaseMacro/ directory", warnStyle);
                GUILayout.EndVertical();
                return;
            }
#endif
            if (!EnableTechniqueSimulation) { GUILayout.EndVertical(); return; }

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

            // ── 配置管理 ────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "配置名称" : "Profile Name", UIUtils.LabelStyle, GUILayout.Width(100));
            string newName = GUILayout.TextField(_techniqueProfiles[SelectedTechniqueProfileIndex].name,
                UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
            if (newName != _techniqueProfiles[SelectedTechniqueProfileIndex].name)
                _techniqueProfiles[SelectedTechniqueProfileIndex].name = newName;

            if (GUILayout.Button(UseChinese ? "新建" : "New", UIUtils.ButtonStyle, GUILayout.Width(60)))
            {
                _techniqueProfiles.Add(_techniqueProfiles[SelectedTechniqueProfileIndex].Clone());
                SelectedTechniqueProfileIndex = _techniqueProfiles.Count - 1;
            }
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

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "选择配置" : "Select Profile", UIUtils.LabelStyle, GUILayout.Width(100));
            string[] profileNames = _techniqueProfiles.Select(p => p.name).ToArray();
            int newIdx = UIUtils.M3SelectionGrid(SelectedTechniqueProfileIndex, profileNames,
                Mathf.Min(profileNames.Length, 4), GUILayout.ExpandWidth(true));
            if (newIdx != SelectedTechniqueProfileIndex) SelectedTechniqueProfileIndex = newIdx;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ── 起始手 ─────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "起始手" : "Starting Hand", UIUtils.LabelStyle, GUILayout.Width(140));
            string[] handOptions = UseChinese ? ["左手", "右手"] : ["Left", "Right"];
            int newHand = UIUtils.M3SelectionGrid(TechniqueHandPreference, handOptions, 2, GUILayout.Width(200));
            if (newHand != TechniqueHandPreference)
            {
                TechniqueHandPreference = newHand;
                _techniqueProfiles[SelectedTechniqueProfileIndex].handPreference = newHand;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ── 全局 BPM 阈值 ────────────────────────────────
            GUILayout.BeginHorizontal();
            TechniqueBpmLimit = UIUtils.M3HorizontalSliderWithLabelAndInput(
                UseChinese ? "全局·速度阈值 (BPM)" : "Global · Speed Threshold (BPM)",
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

            // ── 变速分段 ─────────────────────────────────────
            DrawTechniqueSegments();

            GUILayout.Space(8);

            // ── 全局左右手按键 ────────────────────────────────
            string[] handLabels = UseChinese ? ["左手", "右手"] : ["Left Hand", "Right Hand"];

            GUILayout.Label($"── {handLabels[0]} ──", UIUtils.LabelStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键序列:" : "Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLK = UIUtils.M3TextField(TechLeftHandKeys, ref _techLeftKeysState.input, ref _techLeftKeysState.focused, UIUtils.TextFieldStyle, "TechLeftKeys");
            if (newLK != TechLeftHandKeys) { TechLeftHandKeys = newLK; _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = newLK; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "时长比例:" : "Press Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLP = UIUtils.M3TextField(TechLeftHandPressTimes, ref _techLeftPressTimesState.input, ref _techLeftPressTimesState.focused, UIUtils.TextFieldStyle, "TechLeftPressTimes");
            if (newLP != TechLeftHandPressTimes) { TechLeftHandPressTimes = newLP; _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandPressTimes = newLP; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键顺序:" : "Key Order:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newLO = UIUtils.M3TextField(TechLeftHandOrders, ref _techLeftOrdersState.input, ref _techLeftOrdersState.focused, UIUtils.TextFieldStyle, "TechLeftOrders");
            if (newLO != TechLeftHandOrders) { TechLeftHandOrders = newLO; _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandOrders = newLO; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            GUILayout.Label($"── {handLabels[1]} ──", UIUtils.LabelStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键序列:" : "Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRK = UIUtils.M3TextField(TechRightHandKeys, ref _techRightKeysState.input, ref _techRightKeysState.focused, UIUtils.TextFieldStyle, "TechRightKeys");
            if (newRK != TechRightHandKeys) { TechRightHandKeys = newRK; _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = newRK; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "时长比例:" : "Press Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRP = UIUtils.M3TextField(TechRightHandPressTimes, ref _techRightPressTimesState.input, ref _techRightPressTimesState.focused, UIUtils.TextFieldStyle, "TechRightPressTimes");
            if (newRP != TechRightHandPressTimes) { TechRightHandPressTimes = newRP; _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandPressTimes = newRP; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(UseChinese ? "按键顺序:" : "Key Order:", UIUtils.LabelStyle, GUILayout.Width(80));
            string newRO = UIUtils.M3TextField(TechRightHandOrders, ref _techRightOrdersState.input, ref _techRightOrdersState.focused, UIUtils.TextFieldStyle, "TechRightOrders");
            if (newRO != TechRightHandOrders) { TechRightHandOrders = newRO; _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandOrders = newRO; }
            GUILayout.EndHorizontal();

            // ── 预设 ──────────────────────────────────────────
            GUILayout.Space(6);
            GUILayout.Label(UseChinese ? "预设:" : "Presets:", UIUtils.LabelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("DF / JK", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "D,F"; TechRightHandKeys = "J,K";
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = "D,F";
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = "J,K";
            }
            if (GUILayout.Button("DS / JK", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "D,S"; TechRightHandKeys = "J,K";
                _techniqueProfiles[SelectedTechniqueProfileIndex].leftHandKeys = "D,S";
                _techniqueProfiles[SelectedTechniqueProfileIndex].rightHandKeys = "J,K";
            }
            if (GUILayout.Button("ASDF / JKL", UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
            {
                TechLeftHandKeys = "A,S,D,F"; TechRightHandKeys = "J,K,L";
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

        // ─────────────────────────────────────────────
        //  变速分段 UI（可折叠 + 按键覆盖）
        // ─────────────────────────────────────────────
        private void DrawTechniqueSegments()
        {
            var currentProfile = _techniqueProfiles[SelectedTechniqueProfileIndex];
            var segments = currentProfile.techniqueSegments;

            // 同步辅助列表长度
            while (_segmentEditStates.Count < segments.Count) _segmentEditStates.Add(new SegmentEditState());
            while (_segmentEditStates.Count > segments.Count) _segmentEditStates.RemoveAt(_segmentEditStates.Count - 1);
            while (_segmentExpanded.Count < segments.Count) _segmentExpanded.Add(false);
            while (_segmentExpanded.Count > segments.Count) _segmentExpanded.RemoveAt(_segmentExpanded.Count - 1);

            GUILayout.Space(6);
            GUILayout.Label(UseChinese ? "变速分段设置" : "Speed Segments", UIUtils.HeaderStyle);

            GUIStyle tipStyle = new(UIUtils.LabelStyle)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f, 0.8f) }
            };
            GUILayout.Label(UseChinese
                ? "留空的按键字段将继承全局配置。"
                : "Empty key fields inherit the global configuration.", tipStyle);
            GUILayout.Space(4);

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var state = _segmentEditStates[i];

                // ── 标题行（折叠/展开 + 删除）───────────────
                GUILayout.BeginHorizontal();

                string arrow = _segmentExpanded[i] ? "▼" : "▶";
                string overrideMk = seg.HasKeyOverride ? " ✎" : "";
                string segLabel = UseChinese
                    ? $"{arrow} 段 {i + 1}  [{seg.startFloor}~{seg.endFloor}]  BPM≤{seg.bpmLimit:F0}{overrideMk}"
                    : $"{arrow} Seg {i + 1}  [{seg.startFloor}~{seg.endFloor}]  BPM≤{seg.bpmLimit:F0}{overrideMk}";

                if (GUILayout.Button(segLabel, UIUtils.ButtonStyle, GUILayout.ExpandWidth(true)))
                    _segmentExpanded[i] = !_segmentExpanded[i];

                if (GUILayout.Button("✕", UIUtils.ButtonStyle, GUILayout.Width(36)))
                {
                    segments.RemoveAt(i);
                    _segmentEditStates.RemoveAt(i);
                    _segmentExpanded.RemoveAt(i);
                    i--;
                    continue;
                }
                GUILayout.EndHorizontal();

                if (!_segmentExpanded[i]) continue;

                // ── 展开内容 ─────────────────────────────────
                GUILayout.BeginVertical();
                GUILayout.Space(2);

                // 地板范围
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "起始地板" : "Start Floor", UIUtils.LabelStyle, GUILayout.Width(80));
                string newStart = UIUtils.M3TextField(seg.startFloor.ToString(),
                    ref state.startInput, ref state.startFocused,
                    UIUtils.TextFieldStyle, $"SegStart_{i}", GUILayout.Width(60));
                if (int.TryParse(newStart, out int sv)) seg.startFloor = sv;

                GUILayout.Label(" ~ ", UIUtils.LabelStyle, GUILayout.Width(20));
                GUILayout.Label(UseChinese ? "结束地板" : "End Floor", UIUtils.LabelStyle, GUILayout.Width(80));
                string newEnd = UIUtils.M3TextField(seg.endFloor.ToString(),
                    ref state.endInput, ref state.endFocused,
                    UIUtils.TextFieldStyle, $"SegEnd_{i}", GUILayout.Width(60));
                if (int.TryParse(newEnd, out int ev)) seg.endFloor = ev;
                GUILayout.EndHorizontal();

                // BPM 阈值
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                seg.bpmLimit = UIUtils.M3HorizontalSliderWithLabelAndInput(
                    UseChinese ? "BPM 阈值" : "BPM Limit",
                    seg.bpmLimit, 50f, 2000f,
                    ref state.bpmInput, ref state.bpmFocused,
                    "F0", 80, 160, 60);
                GUILayout.EndHorizontal();

                // 分隔线
                GUILayout.Space(4);
                Color oc = GUI.color;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
                GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
                GUI.color = oc;
                GUILayout.Space(4);

                // ── 按键覆盖（可选）──────────────────────────
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "左手按键:" : "L Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.leftHandKeys = GUILayout.TextField(seg.leftHandKeys, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "右手按键:" : "R Keys:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.rightHandKeys = GUILayout.TextField(seg.rightHandKeys, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "左手时长:" : "L Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.leftHandPressTimes = GUILayout.TextField(seg.leftHandPressTimes, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "右手时长:" : "R Ratio:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.rightHandPressTimes = GUILayout.TextField(seg.rightHandPressTimes, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "左手顺序:" : "L Order:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.leftHandOrders = GUILayout.TextField(seg.leftHandOrders, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label(UseChinese ? "右手顺序:" : "R Order:", UIUtils.LabelStyle, GUILayout.Width(80));
                seg.rightHandOrders = GUILayout.TextField(seg.rightHandOrders, UIUtils.TextFieldStyle, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.EndVertical();
            }

            GUILayout.Space(2);
            if (GUILayout.Button(UseChinese ? "+ 添加分段" : "+ Add Segment", UIUtils.ButtonStyle))
                segments.Add(new TechniqueSegment { bpmLimit = Main.Settings.TechniqueBpmLimit });
        }

        // ─────────────────────────────────────────────
        //  持久化
        // ─────────────────────────────────────────────
        public void OnSaveGUI(UnityModManager.ModEntry modEntry) => Save(modEntry);
        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
        public static Settings Load(UnityModManager.ModEntry modEntry) => Load<Settings>(modEntry);
    }
}