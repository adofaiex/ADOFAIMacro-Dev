using UnityEngine;
using System;
using ADOFAIMacro.Macro;
using ADOFAIMacro.Localization;

namespace ADOFAIMacro
{
    public class ShowText : MonoBehaviour
    {
        private bool _showMacroText;
        private Rect _rect;
        private GUIStyle _textStyle;
        private GUIStyle _shadowStyle;

        public void Awake()
        {
            _rect = new Rect(0, 0, 200, 20);
            _showMacroText = Main.Settings.Macro;

            // 初始化文本样式
            _textStyle = new GUIStyle();
            _textStyle.richText = false; // 不使用 richText，因为我们在样式里设置颜色
            _textStyle.fontSize = 12;
            _textStyle.normal.textColor = Color.green; // 直接在这里设置绿色
            _textStyle.alignment = TextAnchor.MiddleLeft;

            // 初始化阴影样式
            _shadowStyle = new GUIStyle(_textStyle);
            _shadowStyle.normal.textColor = new Color(0, 0, 0, 0.5f);
        }

        public void OnEnable()
        {
            if (Main.Settings != null)
                Main.Settings.OnMacroChanged += OnMacroChanged;
        }

        public void OnDisable()
        {
            if (Main.Settings != null)
                Main.Settings.OnMacroChanged -= OnMacroChanged;
        }

        private void OnMacroChanged(bool newValue)
        {
            _showMacroText = newValue;
        }

        public void Update()
        {
            // 每帧更新 AudioDSPManager
            DSPTimeSimulater.Update();
        }
        public void OnGUI()
        {
            if (_showMacroText)
            {
                string text = LocalizationManager.Get("macro.enabled_text");

                // 绘制阴影
                GUI.Label(new Rect(_rect.x + 2, _rect.y + 2, _rect.width, _rect.height),
                    text, _shadowStyle);

                // 绘制原文本
                GUI.Label(_rect, text, _textStyle);
            }
        }

        public void OnDestroy()
        {
            // 清理样式
            if (_textStyle != null)
            {
                _textStyle = null;
            }
            if (_shadowStyle != null)
            {
                _shadowStyle = null;
            }
        }
    }
}