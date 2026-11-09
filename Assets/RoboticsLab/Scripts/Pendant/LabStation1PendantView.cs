using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Shared pendant artwork and buttons. One instance per station retains its teaching session.</summary>
    public sealed class LabStation1PendantView : MonoBehaviour
    {
        [SerializeField] string lastButtonId = "";
        [SerializeField] int clickCount;

        public bool Visible => canvasObject && canvasObject.activeSelf;
        public string LastButtonId => lastButtonId;
        public int ClickCount => clickCount;
        public IReadOnlyDictionary<string, Button> Buttons => buttons;
        public event Action<string> ButtonClicked;
        public LabStation1Programming Programming { get; private set; }

        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        LabTrainingApp app;
        LabRobotStation station;
        GameObject canvasObject;
        RectTransform keyLayer;

        public void Open(LabTrainingApp owner, GameObject prefab)
        {
            if (!owner || !owner.ActiveStation || !owner.ActiveStation.IsOperating) return;
            if (station && station != owner.ActiveStation) return;
            if (!prefab) { owner.Notify("示教器外观资源未配置。"); return; }
            app = owner;
            station = owner.ActiveStation;
            if (!canvasObject) Build(prefab);
            canvasObject.SetActive(true);
            Programming.Activate(owner.ActiveStation);
        }

        public void Close()
        {
            if (Programming) Programming.Deactivate();
            if (canvasObject) canvasObject.SetActive(false);
        }

        void Update()
        {
            if (Visible && (!app || app.panel != "operate" || !app.ActiveStation
                || app.ActiveStation != station || !app.ActiveStation.IsOperating)) Close();
        }

        void OnDisable() { Close(); }
        void OnDestroy() { if (canvasObject) Destroy(canvasObject); }

        void Build(GameObject prefab)
        {
            canvasObject = new GameObject("Station" + (station.stationIndex + 1) + "PendantCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.SetActive(false);
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var root = Instantiate(prefab, canvasObject.transform, false).GetComponent<RectTransform>();
            root.name = "Station" + (station.stationIndex + 1) + "TeachingPendant";
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            // Right-middle column; leave the left menu and robot view unobstructed.
            root.anchoredPosition = new Vector2(6, -7);
            root.localScale = new Vector3(1.3f, 1.3f, 1);

            // Keep only the artwork. No inherited menu, keyboard, or motion handlers run.
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script && !(script is Graphic)) script.enabled = false;
            foreach (var effect in root.GetComponentsInChildren<BtnEffect>(true))
                if (effect.redHighlight) effect.redHighlight.SetActive(false);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

            var screen = root.Find("Screen");
            foreach (Transform child in screen) child.gameObject.SetActive(false);
            var screenImage = screen.GetComponent<Image>();
            screenImage.sprite = null;
            screenImage.color = new Color(.035f, .05f, .065f, 1);
            screen.SetAsLastSibling();

            var housing = root.Find("Panel1_Background").GetComponent<RectTransform>();
            housing.GetComponent<Image>().raycastTarget = true;
            keyLayer = new GameObject("ButtonHitAreas", typeof(RectTransform)).GetComponent<RectTransform>();
            keyLayer.SetParent(root, false);
            keyLayer.anchorMin = housing.anchorMin;
            keyLayer.anchorMax = housing.anchorMax;
            keyLayer.pivot = housing.pivot;
            keyLayer.sizeDelta = housing.sizeDelta;
            keyLayer.anchoredPosition = housing.anchoredPosition;
            keyLayer.localScale = housing.localScale;
            BuildKeys();

            var programObject = new GameObject("Station" + (station.stationIndex + 1) + "Programming");
            programObject.transform.SetParent(transform, false);
            Programming = programObject.AddComponent<LabStation1Programming>();
            Programming.Initialize(app, this, screen.GetComponent<RectTransform>());
            string[] jogKeys = { "J1-", "J1+", "J2-", "J2+", "J3-", "J3+" };
            Vector3[] directions = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward, Vector3.down, Vector3.up };
            for (int i = 0; i < jogKeys.Length; i++)
            {
                var jog = buttons[jogKeys[i]].gameObject.AddComponent<LabStation1JogButton>();
                jog.programming = Programming; jog.direction = directions[i];
            }

            if (!FindObjectOfType<EventSystem>())
                new GameObject("Station1PendantEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        void BuildKeys()
        {
            // Pixel coordinates on the existing housing artwork, normalized to 1150 x 2048.
            string[] functionKeys = { "PREV", "F1", "F2", "F3", "F4", "F5", "NEXT" };
            for (int i = 0; i < functionKeys.Length; i++) AddKey(functionKeys[i], 394 + i * 65, 1005, 42, 42);
            string[] menuKeys = { "SHIFT_LEFT", "MENU", "SELECT", "EDIT", "DATA", "FCTN", "SHIFT_RIGHT" };
            float[] menuX = { 410, 480, 534, 590, 644, 700, 768 };
            for (int i = 0; i < menuKeys.Length; i++) AddKey(menuKeys[i], menuX[i], 1070, 44, 44);

            AddKey("INFO", 406, 1133, 54, 54);
            AddKey("UP", 516, 1142, 44, 42);
            AddKey("LEFT", 462, 1172, 44, 42);
            AddKey("RIGHT", 572, 1172, 44, 42);
            AddKey("DOWN", 516, 1203, 44, 42);
            AddKey("DISP", 406, 1204, 44, 44);

            float[] jointY = { 1140, 1197, 1255, 1314, 1373, 1431, 1490, 1550 };
            for (int i = 0; i < jointY.Length; i++)
            {
                AddKey("J" + (i + 1) + "-", 700, jointY[i], 50, 50);
                AddKey("J" + (i + 1) + "+", 765, jointY[i], 50, 50);
            }
            string[] runKeys = { "STEP", "HOLD", "FWD", "BWD", "COORD", "GROUP", "OVERRIDE_PLUS", "OVERRIDE_MINUS" };
            for (int i = 0; i < runKeys.Length; i++) AddKey(runKeys[i], 632, jointY[i], 48, 48);

            string[,] numberKeys = {
                { "RESET", "BACKSPACE", "ITEM", "ENTER" },
                { "7", "8", "9", "TOOL1" },
                { "4", "5", "6", "TOOL2" },
                { "1", "2", "3", "MOVE_MENU" },
                { "0", "DOT", "COMMA_MINUS", "SETUP" },
                { "DIAG_HELP", "POSN", "IO", "STATUS" }
            };
            float[] numberX = { 402, 460, 516, 572 };
            float[] numberY = { 1268, 1325, 1380, 1437, 1495, 1550 };
            for (int row = 0; row < numberY.Length; row++)
                for (int column = 0; column < numberX.Length; column++)
                    AddKey(numberKeys[row, column], numberX[column], numberY[row], 44, 44);
            AddKey("POWER", 320, 390, 100, 100);
            AddKey("EMERGENCY_STOP", 855, 380, 105, 105);
        }

        void AddKey(string id, float x, float y, float width, float height)
        {
            var key = new GameObject(id, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = key.GetComponent<RectTransform>();
            rect.SetParent(keyLayer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2((x / 1150f - .5f) * keyLayer.sizeDelta.x,
                (.5f - y / 2048f) * keyLayer.sizeDelta.y);
            rect.sizeDelta = new Vector2(width / 1150f * keyLayer.sizeDelta.x, height / 2048f * keyLayer.sizeDelta.y);
            var image = key.GetComponent<Image>();
            image.color = Color.white;
            var button = key.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = new Color(.1f, .75f, 1, .25f);
            colors.pressedColor = new Color(1, .2f, .08f, .7f);
            colors.selectedColor = colors.highlightedColor;
            colors.colorMultiplier = 1;
            colors.fadeDuration = .06f;
            button.colors = colors;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(() => RecordClick(id));
            buttons.Add(id, button);
        }

        void RecordClick(string id)
        {
            if (!Visible) return;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            lastButtonId = id;
            clickCount++;
            Debug.Log("[Station" + (station.stationIndex + 1) + " 示教器] 点击 " + id + "（累计 " + clickCount + " 次）", this);
            ButtonClicked?.Invoke(id);
        }
    }
}
