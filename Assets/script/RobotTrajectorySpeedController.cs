using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>独立的速度输入步骤。UI 在现有 Canvas 下创建，不控制机械臂。</summary>
public class RobotTrajectorySpeedController : MonoBehaviour
{
    [Header("公屏命令")]
    [SerializeField] private string startCommand = "开始设置轨迹速度";
    [SerializeField] private string completeCommand = "轨迹速度设置完成";

    [Header("现有场景引用")]
    [SerializeField] private Canvas hostCanvas;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private CentralInputReceiver centralInput;

    [Header("速度（单位/秒）")]
    [SerializeField] private float defaultSpeed = 0.1f;
    [SerializeField] private float minimumSpeed = 0.001f;
    [SerializeField] private float maximumSpeed = 10f;
    [SerializeField] private float currentSpeed;

    /// <summary>本场景会话的共享速度；后续模块通过组件引用读取。</summary>
    public float CurrentSpeed => currentSpeed;
    public bool HasConfirmedSpeed { get; private set; }
    public bool IsAwaitingInput { get; private set; }

    private 公屏叫号 commandBus;
    private GameObject panel;
    private TMP_InputField inputField;
    private TMP_Text feedback;
    private bool completing;
    private bool ownsInput;
    private bool previousKeyboardMouse;
    private bool previousGamepad;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private EventSystem modalEventSystem;
    private bool previousNavigationEvents;

    private void Awake()
    {
        currentSpeed = defaultSpeed;
    }

    private void OnEnable() => TrySubscribe();

    private void Update()
    {
        if (commandBus == null)
            TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (commandBus != null || 公屏叫号.Instance == null)
            return;
        commandBus = 公屏叫号.Instance;
        commandBus.叫号广播.AddListener(ReceiveSignal);
    }

    public void ReceiveSignal(string command)
    {
        if (command == startCommand && !IsAwaitingInput && !completing)
            BeginInput();
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void BeginInput()
    {
        if (centralInput == null)
            centralInput = CentralInputReceiver.Instance;

        if (hostCanvas == null || fontAsset == null || centralInput == null ||
            EventSystem.current == null || !hostCanvas.isActiveAndEnabled)
        {
            Debug.LogError("[轨迹速度] 缺少可用的 Canvas、TMP 字体、中央输入或 EventSystem；未完成速度设置。", this);
            return;
        }
        if (!IsFinite(minimumSpeed) || !IsFinite(maximumSpeed) || minimumSpeed <= 0f ||
            maximumSpeed < minimumSpeed || !IsFinite(defaultSpeed) ||
            defaultSpeed < minimumSpeed || defaultSpeed > maximumSpeed)
        {
            Debug.LogError("[轨迹速度] Inspector 配置无效：必须 0 < 最小速度 <= 默认速度 <= 最大速度，且均为有限数值。", this);
            return;
        }

        if (panel == null)
            CreateUI();

        previousKeyboardMouse = centralInput.readKeyboardMouse;
        previousGamepad = centralInput.readGamepad;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        modalEventSystem = EventSystem.current;
        previousNavigationEvents = modalEventSystem.sendNavigationEvents;
        ownsInput = true;
        // 保持组件运行，让它每帧清零按钮状态；TMP 仍可直接读取键盘文字。
        centralInput.readKeyboardMouse = false;
        centralInput.readGamepad = false;
        // TMP 的文字/Enter 处理使用 UpdateSelected，不需要 UI 导航提交事件。
        modalEventSystem.sendNavigationEvents = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        IsAwaitingInput = true;
        inputField.interactable = true;
        inputField.SetTextWithoutNotify(currentSpeed.ToString("G", CultureInfo.InvariantCulture));
        feedback.text = "按 Enter 确认";
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        StartCoroutine(FocusNextFrame());
    }

    private IEnumerator FocusNextFrame()
    {
        yield return null;
        if (!IsAwaitingInput || completing)
            yield break;
        inputField.Select();
        inputField.ActivateInputField();
    }

    private void Submit(string text)
    {
        if (!IsAwaitingInput || completing)
            return;

        // 使用小数点，不接受千位分隔符，防止把 0,2 误读为 2。
        if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) ||
            !IsFinite(speed) || speed <= 0f || speed < minimumSpeed || speed > maximumSpeed)
        {
            feedback.text = $"请输入 {minimumSpeed.ToString(CultureInfo.InvariantCulture)} 至 {maximumSpeed.ToString(CultureInfo.InvariantCulture)} 的正数（小数点用 .）";
            Debug.LogWarning("[轨迹速度] 输入无效。" + feedback.text, this);
            StartCoroutine(FocusNextFrame());
            return;
        }

        currentSpeed = speed;
        HasConfirmedSpeed = true;
        completing = true;
        inputField.interactable = false;
        feedback.text = "速度已保存，请松开 Enter";
        Debug.Log($"[轨迹速度] 已确认轨迹运动速度：{currentSpeed.ToString("G", CultureInfo.InvariantCulture)} 单位/秒。", this);
        StartCoroutine(CompleteAfterRelease());
    }

    private static bool ConfirmIsHeld()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        return (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)) ||
               (gamepad != null && gamepad.buttonSouth.isPressed);
    }

    private IEnumerator CompleteAfterRelease()
    {
        // 不在 TMP 提交回调内广播；等待按键释放及一整帧，隔离下一步骤。
        yield return null;
        while (ConfirmIsHeld())
            yield return null;
        yield return null;

        IsAwaitingInput = false;
        panel.SetActive(false);
        RestoreInput();
        completing = false;
        if (commandBus != null)
            commandBus.喊话(completeCommand);
    }

    private void RestoreInput()
    {
        if (!ownsInput)
            return;
        if (centralInput != null)
        {
            centralInput.readKeyboardMouse = previousKeyboardMouse;
            centralInput.readGamepad = previousGamepad;
        }
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        if (modalEventSystem != null)
            modalEventSystem.sendNavigationEvents = previousNavigationEvents;
        ownsInput = false;
    }

    private void OnDisable()
    {
        if (commandBus != null)
            commandBus.叫号广播.RemoveListener(ReceiveSignal);
        commandBus = null;
        StopAllCoroutines();
        IsAwaitingInput = false;
        completing = false;
        if (panel != null)
            panel.SetActive(false);
        RestoreInput();
    }

    private void OnDestroy()
    {
        if (panel != null)
            Destroy(panel);
    }

    private void CreateUI()
    {
        RectTransform overlay = CreateRect("TrajectorySpeedPanel", hostCanvas.transform, Vector2.zero, Vector2.zero);
        panel = overlay.gameObject;
        panel.SetActive(false);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform card = CreateRect("SpeedDialog", overlay, Vector2.zero, new Vector2(620f, 320f));
        card.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.2f, 1f);
        CreateLabel("Title", card, "设置轨迹运动速度", new Vector2(0f, 110f), new Vector2(580f, 48f), 30f);
        CreateLabel("Prompt", card, "请输入运动速度：", new Vector2(0f, 52f), new Vector2(580f, 36f), 24f);
        CreateLabel("Unit", card, "单位/秒", new Vector2(205f, -7f), new Vector2(150f, 48f), 24f);
        feedback = CreateLabel("Feedback", card, "按 Enter 确认", new Vector2(0f, -95f), new Vector2(580f, 70f), 22f);

        RectTransform field = CreateRect("SpeedInput", card, new Vector2(-65f, -7f), new Vector2(340f, 50f));
        Image background = field.gameObject.AddComponent<Image>();
        background.color = Color.white;
        RectTransform viewport = CreateRect("TextArea", field, Vector2.zero, new Vector2(316f, 44f));
        viewport.gameObject.AddComponent<RectMask2D>();
        TMP_Text text = CreateLabel("Text", viewport, "", Vector2.zero, new Vector2(316f, 44f), 26f);
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = false;
        inputField = field.gameObject.AddComponent<TMP_InputField>();
        inputField.targetGraphic = background;
        inputField.textViewport = viewport;
        inputField.textComponent = text;
        inputField.contentType = TMP_InputField.ContentType.Standard;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 64;
        inputField.onFocusSelectAll = true;
        inputField.onSubmit.AddListener(Submit);
        // 点击外部或 Esc 仅重新聚焦，不作为成功提交。
        inputField.onEndEdit.AddListener(_ =>
        {
            if (IsAwaitingInput && !completing)
                StartCoroutine(FocusNextFrame());
        });
    }

    private static RectTransform CreateRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private TMP_Text CreateLabel(string objectName, Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        RectTransform rect = CreateRect(objectName, parent, position, size);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = fontAsset;
        label.fontSize = fontSize;
        label.text = value;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }
}
