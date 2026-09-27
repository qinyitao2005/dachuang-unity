using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RobotTeaching
{
    [Serializable]
    public class MenuBinding
    {
        public string label;
        public KeyCode key = KeyCode.None;
#if ENABLE_INPUT_SYSTEM
        [Tooltip("使用新输入系统时生效；Both 模式也优先使用此字段。")]
        public Key inputSystemKey = Key.None;
#endif
        public bool continuous;
        public UnityEvent onPressed = new UnityEvent();
        [Tooltip("方向按钮按住时每帧调用；移动量应乘 Time.deltaTime。")]
        public UnityEvent onHeld = new UnityEvent();
        public UnityEvent onReleased = new UnityEvent();
        [HideInInspector] public Button button;
        [HideInInspector] public Text caption;
        [NonSerialized] public bool pointerHeld;
        [NonSerialized] public bool wasHeld;

        public MenuBinding(string title, KeyCode shortcut, bool hold)
        {
            label = title; key = shortcut; continuous = hold;
#if ENABLE_INPUT_SYSTEM
            if (shortcut == KeyCode.Escape) inputSystemKey = Key.Escape;
            else if (Enum.TryParse(shortcut.ToString(), out Key parsed)) inputSystemKey = parsed;
#endif
        }
    }

    public class RobotTrajectoryMenu : MonoBehaviour
    {
        [Tooltip("建议指定包含中文字形的字体资源，确保打包后也能显示中文。")]
        public Font chineseFont;
        [Tooltip("接入已有键盘控制器时关闭，避免重复触发。")]
        public bool readKeyboard = true;
        public MenuBinding[] bindings = {
            new MenuBinding("X+", KeyCode.D, true),
            new MenuBinding("X−", KeyCode.A, true),
            new MenuBinding("Y+", KeyCode.E, true),
            new MenuBinding("Y−", KeyCode.Q, true),
            new MenuBinding("Z+", KeyCode.W, true),
            new MenuBinding("Z−", KeyCode.S, true),
            new MenuBinding("记录点", KeyCode.P, false),
            new MenuBinding("循迹", KeyCode.T, false),
            new MenuBinding("退出本教学模块", KeyCode.Escape, false)
        };
        private Font resolvedFont;
        private bool focused = true;
        [HideInInspector] public Button speedDecrease;
        [HideInInspector] public Button speedIncrease;
        [HideInInspector] public Text speedCaption;

        private void Awake()
        {
            if (bindings[0].button == null) Build();
            EnsureSpeedControls();
            ResolveFont();
            foreach (var text in GetComponentsInChildren<Text>(true))
                if (text.font == null) text.font = resolvedFont;
            for (int i = 0; i < bindings.Length; i++)
            {
                int index = i;
                if (!bindings[i].continuous && bindings[i].button != null)
                    bindings[i].button.onClick.AddListener(() => Trigger(index));
            }
        }

        private void Update()
        {
            bool typing = EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != null &&
                (EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null ||
                 EventSystem.current.currentSelectedGameObject.GetComponent("TMP_InputField") != null);
            foreach (var b in bindings)
            {
                if (b.caption != null)
                {
                    string keyLabel = Shortcut(b);
                    string title = b.label + (keyLabel == "" ? "" : "  [" + keyLabel + "]");
                    if (b.caption.text != title) b.caption.text = title;
                }
                bool allowed = focused && b.button != null && b.button.IsActive() && b.button.IsInteractable();
                bool keyboardHeld = false, keyDown = false;
                if (readKeyboard && allowed && !typing) ReadKey(b, out keyboardHeld, out keyDown);
                if (b.continuous)
                {
                    bool held = allowed && (b.pointerHeld || keyboardHeld);
                    bool previous = b.wasHeld;
                    b.wasHeld = held;
                    if (held && !previous) b.onPressed.Invoke();
                    if (!isActiveAndEnabled) return;
                    if (held) b.onHeld.Invoke();
                    if (!held && previous) b.onReleased.Invoke();
                }
                else if (allowed && keyDown) b.onPressed.Invoke();
                if (!isActiveAndEnabled) return;
            }
        }

        public void Trigger(int index)
        {
            if (!isActiveAndEnabled || index < 0 || index >= bindings.Length) return;
            var b = bindings[index];
            if (b.button != null && b.button.IsActive() && b.button.IsInteractable())
                b.onPressed.Invoke();
        }

        public void SetPointerHeld(int index, bool held)
        {
            if (index >= 0 && index < bindings.Length) bindings[index].pointerHeld = held;
        }

        private void OnDisable() { ReleaseAll(); }
        private void OnApplicationFocus(bool hasFocus) { focused = hasFocus; if (!hasFocus) ReleaseAll(); }
        private void OnApplicationPause(bool paused) { if (paused) ReleaseAll(); }
        private void ReleaseAll()
        {
            foreach (var b in bindings)
            {
                bool release = b.wasHeld;
                b.pointerHeld = b.wasHeld = false;
                if (release) b.onReleased.Invoke();
            }
        }

        private static void ReadKey(MenuBinding b, out bool held, out bool down)
        {
            held = down = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null || b.inputSystemKey == Key.None) return;
            var key = Keyboard.current[b.inputSystemKey];
            held = key.isPressed; down = key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (b.key == KeyCode.None) return;
            held = Input.GetKey(b.key); down = Input.GetKeyDown(b.key);
#endif
        }

        private static string Shortcut(MenuBinding b)
        {
#if ENABLE_INPUT_SYSTEM
            return b.inputSystemKey == Key.None ? "" : b.inputSystemKey == Key.Escape ? "Esc" : b.inputSystemKey.ToString();
#else
            return b.key == KeyCode.None ? "" : b.key == KeyCode.Escape ? "Esc" : b.key.ToString();
#endif
        }

        private void ResolveFont()
        {
            if (chineseFont != null) resolvedFont = chineseFont;
            else if (resolvedFont == null)
                resolvedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimHei", "Arial" }, 28);
        }

        // Called by the editor creator, or automatically when attached to an empty GameObject.
        public void EnsureSpeedControls()
        {
            if (speedDecrease && speedIncrease && speedCaption) return;
            if (bindings == null || bindings.Length == 0 || !bindings[0].button) return;
            ResolveFont();
            Transform panel = bindings[0].button.transform.parent;
            // Upgrade an already saved menu at runtime without rebuilding its Canvas.
            Transform row = panel.Find("Speed Row");
            if (row == null)
            {
                var hint = panel.Find("Hint");
                if (hint != null) hint.gameObject.SetActive(false);
                row = Rect(panel, "Speed Row", 30, 204, 400, 48);
                speedCaption = Label(row, "Speed Value", "速度  0.10", 0, 0, 192, 48, 23, Color.white);
                speedDecrease = SpeedButton(row, "Decrease", "− 0.05", 208);
                speedIncrease = SpeedButton(row, "Increase", "+ 0.05", 308);
            }
            else
            {
                speedCaption = row.Find("Speed Value").GetComponent<Text>();
                speedDecrease = row.Find("Decrease").GetComponent<Button>();
                speedIncrease = row.Find("Increase").GetComponent<Button>();
            }
        }

        private Button SpeedButton(Transform parent, string name, string caption, float x)
        {
            var rect = Box(parent, name, x, 0, 92, 48, new Color32(47, 58, 70, 255));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Label(rect, "Label", caption, 0, 0, 92, 48, 20, Color.white).alignment = TextAnchor.MiddleCenter;
            return button;
        }

        public void RefreshSpeed(float speed, bool allow)
        {
            EnsureSpeedControls();
            if (!speedCaption) return;
            speedCaption.text = "速度  " + speed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            speedDecrease.interactable = allow && speed > 0f;
            speedIncrease.interactable = allow && speed < 0.5f;
        }

        public void Build()
        {
            if (GetComponentInChildren<Canvas>(true) != null) return;
            ResolveFont();
            var canvasObject = new GameObject("Trajectory Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = Box(canvasObject.transform, "Menu Panel", 24, 32, 460, 816, new Color32(27, 33, 40, 245));
            Box(panel, "Accent", 0, 0, 4, 816, new Color32(83, 161, 175, 255));
            Label(panel, "Eyebrow", "ROBOT TRAINING   /   02", 30, 28, 400, 26, 16, new Color32(132, 170, 181, 255));
            Label(panel, "Title", "机械臂循迹", 30, 69, 400, 52, 34, Color.white);
            Label(panel, "Subtitle", "末端点动控制  /  手动模式", 30, 133, 400, 32, 21, new Color32(183, 197, 207, 255));
            Box(panel, "Divider", 30, 185, 400, 1, new Color32(63, 75, 88, 255));
            Label(panel, "Hint", "按住方向键移动 · 点击记录轨迹点", 30, 204, 400, 32, 19, new Color32(159, 177, 190, 255));
            // X/Y/Z are literal axis labels; adapt coordinates in your robot controller.
            MakeButton(panel, 4, 153, 264, 154, 64, false);
            MakeButton(panel, 1, 30, 340, 192, 64, false);
            MakeButton(panel, 0, 238, 340, 192, 64, false);
            MakeButton(panel, 5, 153, 416, 154, 64, false);
            MakeButton(panel, 2, 30, 508, 192, 64, false);
            MakeButton(panel, 3, 238, 508, 192, 64, false);
            Box(panel, "Action Divider", 30, 599, 400, 1, new Color32(63, 75, 88, 255));
            MakeButton(panel, 6, 30, 626, 192, 64, false);
            MakeButton(panel, 7, 238, 626, 192, 64, true);
            MakeButton(panel, 8, 30, 720, 400, 60, false);
            EnsureSpeedControls();

            if (FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                events.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                events.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private void MakeButton(Transform parent, int index, float x, float y, float w, float h, bool accent)
        {
            var b = bindings[index];
            var rect = Box(parent, b.label, x, y, w, h,
                accent ? new Color32(78, 151, 166, 255) : new Color32(47, 58, 70, 255));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
            colors.pressedColor = new Color(0.7f, 0.85f, 0.9f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            b.button = button;
            b.caption = Label(rect, "Label", b.label + (Shortcut(b) == "" ? "" : "  [" + Shortcut(b) + "]"), 0, 0, w, h, index == 8 ? 23 : 25, Color.white);
            b.caption.alignment = TextAnchor.MiddleCenter;
            if (b.continuous)
            {
                var pointer = rect.gameObject.AddComponent<TrajectoryHoldButton>();
                pointer.menu = this; pointer.bindingIndex = index;
            }
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }
        private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var r = Rect(parent, name, x, y, w, h);
            r.gameObject.AddComponent<Image>().color = color;
            return r;
        }
        private Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color color)
        {
            var r = Rect(parent, name, x, y, w, h);
            var text = r.gameObject.AddComponent<Text>();
            text.font = resolvedFont; text.text = value; text.fontSize = size; text.color = color;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }
    }
}
