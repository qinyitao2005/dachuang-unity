using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>
    /// settingsUI 画布上的齿轮设置面板：点击齿轮弹出设置，包含「音量调节」与「跳过教程，直接操作」。
    /// 无需改动场景：运行时自动挂载，并把 settingsUI 下名为 Image 的齿轮绑定为按钮。
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class LabSettingsMenu : MonoBehaviour
    {
        const string CanvasName = "settingsUI";
        const string GearChild = "Image";
        const string VolumeKey = "RobotLab.Volume";

        LabTrainingApp app;
        Canvas canvas;
        GameObject panel;
        Slider volumeSlider;
        Text volumeValue;
        bool syncingVolume;

        static readonly Color Navy = new Color(.045f, .075f, .11f, .985f);
        static readonly Color Teal = new Color(.04f, .61f, .67f, 1f);
        static readonly Color Muted = new Color(.15f, .21f, .27f, 1f);
        static readonly Color Light = new Color(.88f, .93f, .95f, 1f);
        static readonly Color MutedText = new Color(.57f, .73f, .77f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var canvasGo = GameObject.Find(CanvasName);
            if (!canvasGo || canvasGo.GetComponent<LabSettingsMenu>()) return;
            canvasGo.AddComponent<LabSettingsMenu>();
        }

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (!canvas) { enabled = false; return; }
            canvas.sortingOrder = 200;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = .5f;
            }

            app = FindObjectOfType<LabTrainingApp>();
            EnsureEventSystem();
            WireGear();
            BuildPanel();
            ApplyVolume(PlayerPrefs.GetFloat(VolumeKey, Mathf.Clamp01(AudioListener.volume)), false);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2)) Toggle();
            if (panel && panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void WireGear()
        {
            Transform gear = transform.Find(GearChild);
            if (!gear) return;
            var image = gear.GetComponent<Image>();
            if (image) image.raycastTarget = true;
            var button = gear.GetComponent<Button>();
            if (!button) button = gear.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Toggle);
        }

        void BuildPanel()
        {
            RectTransform rect = Rect("SettingsPanel", transform, 0, 0, 620, 430);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            panel = rect.gameObject;
            var background = panel.AddComponent<Image>();
            background.color = Navy;
            background.raycastTarget = true;

            Label("Title", rect, "设置  /  SETTINGS", 40, 28, 540, 44, 30, Color.white, TextAnchor.MiddleLeft);

            Label("VolumeLabel", rect, "音量", 44, 110, 110, 36, 24, Light, TextAnchor.MiddleLeft);
            volumeSlider = SliderAt(rect, 170, 116, 330, 26, AudioListener.volume);
            volumeValue = Label("VolumeValue", rect, "100%", 510, 110, 70, 36, 22, MutedText, TextAnchor.MiddleRight);

            ButtonAt("SkipTutorial", rect, "跳过教程，直接操作", 44, 180, 532, 64, Teal, 26, ApplySkip);
            Label("SkipHint", rect, "解锁后无需佩戴安全帽、观看课程与问答，可直接走近任一工位按 E 操作。",
                44, 252, 532, 62, 20, MutedText, TextAnchor.UpperLeft);

            ButtonAt("Close", rect, "关闭", 44, 336, 532, 60, Muted, 24, Close);

            panel.SetActive(false);
        }

        void Toggle()
        {
            if (!panel) return;
            if (panel.activeSelf) { Close(); return; }
            if (!app) app = FindObjectOfType<LabTrainingApp>();
            if (app && app.player) app.player.pointerReleased = true;
            panel.SetActive(true);
        }

        void Close()
        {
            if (panel) panel.SetActive(false);
        }

        void ApplyVolume(float value, bool save)
        {
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            if (volumeValue) volumeValue.text = Mathf.RoundToInt(value * 100f) + "%";
            if (volumeSlider && !syncingVolume && !Mathf.Approximately(volumeSlider.value, value))
            {
                syncingVolume = true;
                volumeSlider.value = value;
                syncingVolume = false;
            }
            if (save) PlayerPrefs.SetFloat(VolumeKey, value);
        }

        void ApplySkip()
        {
            if (!app) app = FindObjectOfType<LabTrainingApp>();
            if (!app) return;
            app.HelmetWorn = true;
            app.LessonCompleted = true;
            app.QuizPassed = true;
            if (app.panel == "lesson" || app.panel == "lessonText" || app.panel == "quiz") app.ClosePanel();
            app.Notify("已跳过教程：可直接走近任一机械臂工位，按 E 开始操作。", 6);
            Close();
        }

        static void EnsureEventSystem()
        {
            var events = FindObjectOfType<EventSystem>();
            if (!events)
            {
                var go = new GameObject("LabSettingsEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events = go.GetComponent<EventSystem>();
                events.sendNavigationEvents = false;
            }
            else if (!events.GetComponent<BaseInputModule>())
            {
                events.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        Text Label(string name, Transform parent, string value, float x, float y, float width, float height,
            int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = app && app.uiFont ? app.uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Button ButtonAt(string name, Transform parent, string caption, float x, float y, float width, float height,
            Color background, int fontSize, UnityAction action)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = background;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(.7f, .8f, .84f);
            colors.fadeDuration = .08f;
            button.colors = colors;

            var label = Label("Label", image.transform, caption, 0, 0, width, height, fontSize, Color.white, TextAnchor.MiddleCenter);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12, 4);
            labelRect.offsetMax = new Vector2(-12, -4);

            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        Slider SliderAt(Transform parent, float x, float y, float width, float height, float value)
        {
            var root = Rect("VolumeSlider", parent, x, y, width, height);
            var slider = root.gameObject.AddComponent<Slider>();

            var background = Rect("Background", root, 0, 0, width, height).gameObject.AddComponent<Image>();
            background.color = new Color(.16f, .22f, .28f, 1f);
            background.raycastTarget = true;
            var backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0, .25f);
            backgroundRect.anchorMax = new Vector2(1, .75f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            backgroundRect.pivot = new Vector2(.5f, .5f);
            backgroundRect.anchoredPosition = Vector2.zero;

            var fillArea = Rect("Fill Area", root, 0, 0, width, height);
            fillArea.anchorMin = new Vector2(0, .25f);
            fillArea.anchorMax = new Vector2(1, .75f);
            fillArea.offsetMin = new Vector2(8, 0);
            fillArea.offsetMax = new Vector2(-8, 0);
            fillArea.pivot = new Vector2(.5f, .5f);
            fillArea.anchoredPosition = Vector2.zero;
            var fill = Rect("Fill", fillArea, 0, 0, 0, 0).gameObject.AddComponent<Image>();
            fill.color = Teal;
            fill.raycastTarget = false;
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.pivot = new Vector2(.5f, .5f);

            var handleArea = Rect("Handle Slide Area", root, 0, 0, width, height);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10, 0);
            handleArea.offsetMax = new Vector2(-10, 0);
            handleArea.pivot = new Vector2(.5f, .5f);
            handleArea.anchoredPosition = Vector2.zero;
            var handle = Rect("Handle", handleArea, 0, 0, 0, 0).gameObject.AddComponent<Image>();
            handle.color = Color.white;
            handle.raycastTarget = true;
            var handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.sizeDelta = new Vector2(22, 0);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.wholeNumbers = false;
            slider.value = Mathf.Clamp01(value);
            slider.onValueChanged.AddListener(v => ApplyVolume(v, true));
            return slider;
        }
    }
}
