using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-1000)]
public class CentralInputReceiver : MonoBehaviour
{
    public static CentralInputReceiver Instance { get; private set; }

    [Header("General")]
    public bool dontDestroyOnLoad = false;

    [Header("Input Sources")]
    [Tooltip("启用键盘和鼠标输入。默认作为主要操作方式。")]
    public bool readKeyboardMouse = true;

    [Tooltip("预留手柄输入接口。当前默认关闭，需要时可在 Inspector 中重新勾选。")]
    public bool readGamepad = false;

    [Header("Dead Zones")]
    [Range(0f, 0.5f)] public float leftStickDeadZone = 0.15f;
    [Range(0f, 0.5f)] public float rightStickDeadZone = 0.15f;
    [Range(0f, 0.5f)] public float triggerDeadZone = 0.05f;
    [Range(0f, 0.9f)] public float navigationPressThreshold = 0.55f;

    [Header("Mouse Look")]
    public bool mouseLookOnlyWhenRightButtonHeld = true;
    public bool lockCursorWhileRightMouseHeld = false;

    [Header("Unity Events - Optional")]
    public UnityEvent onInteractPressed = new UnityEvent();
    public UnityEvent onConfirmPressed = new UnityEvent();
    public UnityEvent onCancelPressed = new UnityEvent();
    public UnityEvent onMenuPressed = new UnityEvent();
    public UnityEvent onNextPressed = new UnityEvent();
    public UnityEvent onPreviousPressed = new UnityEvent();

    public UnityEvent onButtonSouthPressed = new UnityEvent(); // 手柄 A
    public UnityEvent onButtonEastPressed = new UnityEvent();  // 手柄 B
    public UnityEvent onButtonWestPressed = new UnityEvent();  // 手柄 X
    public UnityEvent onButtonNorthPressed = new UnityEvent(); // 手柄 Y

    public event Action OnInteractPressed;
    public event Action OnConfirmPressed;
    public event Action OnCancelPressed;
    public event Action OnMenuPressed;
    public event Action OnNextPressed;
    public event Action OnPreviousPressed;

    public event Action OnButtonSouthPressed;
    public event Action OnButtonEastPressed;
    public event Action OnButtonWestPressed;
    public event Action OnButtonNorthPressed;

    public event Action<CentralInputReceiver> OnInputUpdated;

    // =========================
    // 连续输入
    // =========================

    /// <summary>
    /// 平面移动输入。
    /// x = 左右，y = 前后。
    /// 键盘 WASD / 手柄左摇杆。
    /// </summary>
    public Vector2 Move2D { get; private set; }

    /// <summary>
    /// 上下移动输入。
    /// Space / Ctrl，手柄 RT / LT。
    /// </summary>
    public float MoveY { get; private set; }

    /// <summary>
    /// 三维移动输入。
    /// x = 左右，y = 上下，z = 前后。
    /// </summary>
    public Vector3 Move3D => new Vector3(Move2D.x, MoveY, Move2D.y);

    /// <summary>
    /// 鼠标视角增量。
    /// 默认需要按住鼠标右键才有值。
    /// </summary>
    public Vector2 MouseLookDelta { get; private set; }

    /// <summary>
    /// 手柄右摇杆视角输入。
    /// x = 左右看，y = 上下看。
    /// </summary>
    public Vector2 GamepadLookStick { get; private set; }

    /// <summary>
    /// 菜单导航输入。
    /// 方向键 / DPad / 左摇杆。
    /// </summary>
    public Vector2 Navigate2D { get; private set; }

    public bool SprintHeld { get; private set; }

    public float LeftTrigger { get; private set; }
    public float RightTrigger { get; private set; }

    public bool LeftMouseHeld { get; private set; }
    public bool RightMouseHeld { get; private set; }
    public bool MiddleMouseHeld { get; private set; }

    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseScroll { get; private set; }

    // =========================
    // 通用动作按钮
    // =========================

    /// <summary>
    /// 交互键。默认：键盘 X / 手柄 X(buttonWest)。
    /// 用于：靠近仪器后进入教学。
    /// </summary>
    public bool InteractPressedThisFrame { get; private set; }
    public bool InteractHeld { get; private set; }

    /// <summary>
    /// 确认键。默认：Enter / 手柄 A(buttonSouth)。
    /// </summary>
    public bool ConfirmPressedThisFrame { get; private set; }
    public bool ConfirmHeld { get; private set; }

    /// <summary>
    /// 取消键。默认：Esc / 手柄 B(buttonEast)。
    /// </summary>
    public bool CancelPressedThisFrame { get; private set; }
    public bool CancelHeld { get; private set; }

    /// <summary>
    /// 菜单键。默认：Tab / Start。
    /// </summary>
    public bool MenuPressedThisFrame { get; private set; }

    /// <summary>
    /// 下一项。默认：E / RB。
    /// </summary>
    public bool NextPressedThisFrame { get; private set; }

    /// <summary>
    /// 上一项。默认：Q / LB。
    /// </summary>
    public bool PreviousPressedThisFrame { get; private set; }

    // =========================
    // 原始手柄按钮
    // =========================

    public bool ButtonSouthPressedThisFrame { get; private set; } // A
    public bool ButtonEastPressedThisFrame { get; private set; }  // B
    public bool ButtonWestPressedThisFrame { get; private set; }  // X
    public bool ButtonNorthPressedThisFrame { get; private set; } // Y

    public bool ButtonSouthHeld { get; private set; }
    public bool ButtonEastHeld { get; private set; }
    public bool ButtonWestHeld { get; private set; }
    public bool ButtonNorthHeld { get; private set; }

    // =========================
    // 菜单方向的一帧按下
    // =========================

    public bool NavigateUpPressedThisFrame { get; private set; }
    public bool NavigateDownPressedThisFrame { get; private set; }
    public bool NavigateLeftPressedThisFrame { get; private set; }
    public bool NavigateRightPressedThisFrame { get; private set; }

    private bool wasNavUpHeld;
    private bool wasNavDownHeld;
    private bool wasNavLeftHeld;
    private bool wasNavRightHeld;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CentralInputReceiver] 场景里出现了多个输入接收器，已销毁重复对象。");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        ResetFrameInput();

        if (readKeyboardMouse)
            ReadKeyboardAndMouse();

        if (readGamepad)
            ReadGamepad();

        NormalizeAxes();
        UpdateNavigationEdges();
        InvokeEvents();
        HandleCursorLock();

        OnInputUpdated?.Invoke(this);
    }

    private void OnDisable()
    {
        UnlockCursor();
    }

    private void ReadKeyboardAndMouse()
    {
        // 当前键鼠方案固定为经典 WASD 映射。
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null)
        {
            // WASD：移动
            if (keyboard.wKey.isPressed) Move2D += Vector2.up;
            if (keyboard.sKey.isPressed) Move2D += Vector2.down;
            if (keyboard.aKey.isPressed) Move2D += Vector2.left;
            if (keyboard.dKey.isPressed) Move2D += Vector2.right;

            // 上下移动
            if (keyboard.spaceKey.isPressed) MoveY += 1f;

            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
                MoveY -= 1f;

            // 加速
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                SprintHeld = true;

            // 菜单导航：方向键
            if (keyboard.upArrowKey.isPressed) Navigate2D += Vector2.up;
            if (keyboard.downArrowKey.isPressed) Navigate2D += Vector2.down;
            if (keyboard.leftArrowKey.isPressed) Navigate2D += Vector2.left;
            if (keyboard.rightArrowKey.isPressed) Navigate2D += Vector2.right;

            // 通用动作
            if (keyboard.xKey.wasPressedThisFrame)
            {
                InteractPressedThisFrame = true;
                ButtonWestPressedThisFrame = true;
            }

            if (keyboard.xKey.isPressed)
            {
                InteractHeld = true;
                ButtonWestHeld = true;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                ConfirmPressedThisFrame = true;
                ButtonSouthPressedThisFrame = true;
            }

            if (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)
            {
                ConfirmHeld = true;
                ButtonSouthHeld = true;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelPressedThisFrame = true;
                ButtonEastPressedThisFrame = true;
            }

            if (keyboard.escapeKey.isPressed)
            {
                CancelHeld = true;
                ButtonEastHeld = true;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
                MenuPressedThisFrame = true;

            if (keyboard.eKey.wasPressedThisFrame)
                NextPressedThisFrame = true;

            if (keyboard.qKey.wasPressedThisFrame)
                PreviousPressedThisFrame = true;

            if (keyboard.yKey.wasPressedThisFrame)
                ButtonNorthPressedThisFrame = true;

            if (keyboard.yKey.isPressed)
                ButtonNorthHeld = true;
        }

        if (mouse != null)
        {
            LeftMouseHeld = mouse.leftButton.isPressed;
            RightMouseHeld = mouse.rightButton.isPressed;
            MiddleMouseHeld = mouse.middleButton.isPressed;

            MousePosition = mouse.position.ReadValue();
            MouseScroll = mouse.scroll.ReadValue();

            bool allowMouseLook = true;

            if (mouseLookOnlyWhenRightButtonHeld)
                allowMouseLook = mouse.rightButton.isPressed;

            if (allowMouseLook)
                MouseLookDelta = mouse.delta.ReadValue();
        }
    }

    private void ReadGamepad()
    {
        Gamepad gamepad = Gamepad.current;

        if (gamepad == null)
            return;

        // 左摇杆：移动
        Vector2 leftStick = ApplyDeadZone(gamepad.leftStick.ReadValue(), leftStickDeadZone);
        Move2D += leftStick;

        // 右摇杆：视角
        GamepadLookStick = ApplyDeadZone(gamepad.rightStick.ReadValue(), rightStickDeadZone);

        // DPad / 左摇杆：菜单导航
        Vector2 dpad = gamepad.dpad.ReadValue();
        Navigate2D += dpad;
        Navigate2D += leftStick;

        // 扳机：上下
        LeftTrigger = ApplyDeadZone(gamepad.leftTrigger.ReadValue(), triggerDeadZone);
        RightTrigger = ApplyDeadZone(gamepad.rightTrigger.ReadValue(), triggerDeadZone);
        MoveY += RightTrigger - LeftTrigger;

        // 加速
        if (gamepad.leftStickButton.isPressed)
            SprintHeld = true;

        // Xbox 布局：
        // A = buttonSouth
        // B = buttonEast
        // X = buttonWest
        // Y = buttonNorth

        if (gamepad.buttonSouth.wasPressedThisFrame)
        {
            ButtonSouthPressedThisFrame = true;
            ConfirmPressedThisFrame = true;
        }

        if (gamepad.buttonSouth.isPressed)
        {
            ButtonSouthHeld = true;
            ConfirmHeld = true;
        }

        if (gamepad.buttonEast.wasPressedThisFrame)
        {
            ButtonEastPressedThisFrame = true;
            CancelPressedThisFrame = true;
        }

        if (gamepad.buttonEast.isPressed)
        {
            ButtonEastHeld = true;
            CancelHeld = true;
        }

        if (gamepad.buttonWest.wasPressedThisFrame)
        {
            ButtonWestPressedThisFrame = true;
            InteractPressedThisFrame = true;
        }

        if (gamepad.buttonWest.isPressed)
        {
            ButtonWestHeld = true;
            InteractHeld = true;
        }

        if (gamepad.buttonNorth.wasPressedThisFrame)
            ButtonNorthPressedThisFrame = true;

        if (gamepad.buttonNorth.isPressed)
            ButtonNorthHeld = true;

        if (gamepad.startButton.wasPressedThisFrame)
            MenuPressedThisFrame = true;

        if (gamepad.rightShoulder.wasPressedThisFrame)
            NextPressedThisFrame = true;

        if (gamepad.leftShoulder.wasPressedThisFrame)
            PreviousPressedThisFrame = true;
    }

    private void ResetFrameInput()
    {
        Move2D = Vector2.zero;
        MoveY = 0f;
        MouseLookDelta = Vector2.zero;
        GamepadLookStick = Vector2.zero;
        Navigate2D = Vector2.zero;

        SprintHeld = false;

        LeftTrigger = 0f;
        RightTrigger = 0f;

        LeftMouseHeld = false;
        RightMouseHeld = false;
        MiddleMouseHeld = false;
        MouseScroll = Vector2.zero;

        InteractPressedThisFrame = false;
        InteractHeld = false;

        ConfirmPressedThisFrame = false;
        ConfirmHeld = false;

        CancelPressedThisFrame = false;
        CancelHeld = false;

        MenuPressedThisFrame = false;
        NextPressedThisFrame = false;
        PreviousPressedThisFrame = false;

        ButtonSouthPressedThisFrame = false;
        ButtonEastPressedThisFrame = false;
        ButtonWestPressedThisFrame = false;
        ButtonNorthPressedThisFrame = false;

        ButtonSouthHeld = false;
        ButtonEastHeld = false;
        ButtonWestHeld = false;
        ButtonNorthHeld = false;

        NavigateUpPressedThisFrame = false;
        NavigateDownPressedThisFrame = false;
        NavigateLeftPressedThisFrame = false;
        NavigateRightPressedThisFrame = false;
    }

    private void NormalizeAxes()
    {
        if (Move2D.sqrMagnitude > 1f)
            Move2D = Move2D.normalized;

        MoveY = Mathf.Clamp(MoveY, -1f, 1f);

        if (Navigate2D.sqrMagnitude > 1f)
            Navigate2D = Navigate2D.normalized;
    }

    private void UpdateNavigationEdges()
    {
        bool navUpHeld = Navigate2D.y > navigationPressThreshold;
        bool navDownHeld = Navigate2D.y < -navigationPressThreshold;
        bool navRightHeld = Navigate2D.x > navigationPressThreshold;
        bool navLeftHeld = Navigate2D.x < -navigationPressThreshold;

        NavigateUpPressedThisFrame = navUpHeld && !wasNavUpHeld;
        NavigateDownPressedThisFrame = navDownHeld && !wasNavDownHeld;
        NavigateRightPressedThisFrame = navRightHeld && !wasNavRightHeld;
        NavigateLeftPressedThisFrame = navLeftHeld && !wasNavLeftHeld;

        wasNavUpHeld = navUpHeld;
        wasNavDownHeld = navDownHeld;
        wasNavRightHeld = navRightHeld;
        wasNavLeftHeld = navLeftHeld;
    }

    private void InvokeEvents()
    {
        if (InteractPressedThisFrame)
        {
            OnInteractPressed?.Invoke();
            onInteractPressed.Invoke();
        }

        if (ConfirmPressedThisFrame)
        {
            OnConfirmPressed?.Invoke();
            onConfirmPressed.Invoke();
        }

        if (CancelPressedThisFrame)
        {
            OnCancelPressed?.Invoke();
            onCancelPressed.Invoke();
        }

        if (MenuPressedThisFrame)
        {
            OnMenuPressed?.Invoke();
            onMenuPressed.Invoke();
        }

        if (NextPressedThisFrame)
        {
            OnNextPressed?.Invoke();
            onNextPressed.Invoke();
        }

        if (PreviousPressedThisFrame)
        {
            OnPreviousPressed?.Invoke();
            onPreviousPressed.Invoke();
        }

        if (ButtonSouthPressedThisFrame)
        {
            OnButtonSouthPressed?.Invoke();
            onButtonSouthPressed.Invoke();
        }

        if (ButtonEastPressedThisFrame)
        {
            OnButtonEastPressed?.Invoke();
            onButtonEastPressed.Invoke();
        }

        if (ButtonWestPressedThisFrame)
        {
            OnButtonWestPressed?.Invoke();
            onButtonWestPressed.Invoke();
        }

        if (ButtonNorthPressedThisFrame)
        {
            OnButtonNorthPressed?.Invoke();
            onButtonNorthPressed.Invoke();
        }
    }

    private Vector2 ApplyDeadZone(Vector2 value, float deadZone)
    {
        if (value.magnitude < deadZone)
            return Vector2.zero;

        return value;
    }

    private float ApplyDeadZone(float value, float deadZone)
    {
        if (Mathf.Abs(value) < deadZone)
            return 0f;

        return value;
    }

    private void HandleCursorLock()
    {
        if (!lockCursorWhileRightMouseHeld)
            return;

        if (RightMouseHeld)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            UnlockCursor();
        }
    }

    public void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
