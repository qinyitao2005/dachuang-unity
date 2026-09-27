using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class OperationAreaPromptTrigger : MonoBehaviour
{
    [Header("要检测的摄像机或玩家物体")]
    [Tooltip("拖入真正移动的 Sphere、Camera 或玩家根物体")]
    [SerializeField] private Transform targetObject;

    [Header("进入操作区后显示的提示")]
    [Tooltip("拖入 Canvas 下的操作提示物体。提示文字要与下面选择的按键保持一致。")]
    [SerializeField] private GameObject teachingPrompt;

    [Header("启动按键")]
    [Tooltip("在 Inspector 下拉框中选择进入教学系统的键盘按键，建议使用 E、F 或 Enter。")]
    [SerializeField] private Key startKeyboardKey = Key.E;

    [Tooltip("勾选后，除了上面的键盘按键，手柄 A 键也能进入教学系统。")]
    [SerializeField] private bool allowGamepadSouthButton = false;

    [Header("按下启动键后发出的公屏叫号")]
    [SerializeField] private string teachingStartCommand = "教学模式开启";

    [Header("调试")]
    [SerializeField] private bool printLogs = true;

    // 防止目标身上有多个 Collider 时反复进入、退出。
    private readonly HashSet<Collider> detectedColliders =
        new HashSet<Collider>();

    // 防止按住启动键或连续帧重复叫号。
    private bool startCommandSent;

    private void Awake()
    {
        SetPromptVisible(false);
    }

    private void Update()
    {
        // 目标没有位于操作区内，或者本次进入已经启动过教学。
        if (detectedColliders.Count == 0 || startCommandSent)
            return;

        if (WasStartButtonPressedThisFrame(out string inputName))
            SendTeachingStartCommand(inputName);
    }

    private bool WasStartButtonPressedThisFrame(out string inputName)
    {
        inputName = string.Empty;

        // 键盘按键由 Inspector 中的 startKeyboardKey 决定。
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && startKeyboardKey != Key.None)
        {
            var selectedKey = keyboard[startKeyboardKey];

            if (selectedKey != null && selectedKey.wasPressedThisFrame)
            {
                inputName = startKeyboardKey.ToString();
                return true;
            }
        }

        // 手柄接口保留，但默认关闭。
        if (allowGamepadSouthButton &&
            CentralInputReceiver.Instance != null &&
            CentralInputReceiver.Instance.ButtonSouthPressedThisFrame)
        {
            inputName = "手柄 A";
            return true;
        }

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!BelongsToTarget(other))
            return;

        detectedColliders.Add(other);

        if (!startCommandSent)
            SetPromptVisible(true);

        if (printLogs)
        {
            Debug.Log(
                "[操作区] 目标进入检测区，显示教学提示。Collider = " +
                other.name,
                this
            );
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!detectedColliders.Contains(other))
            return;

        detectedColliders.Remove(other);

        if (detectedColliders.Count == 0)
        {
            SetPromptVisible(false);

            // 完全离开以后，允许下次重新进入、重新启动。
            startCommandSent = false;

            if (printLogs)
                Debug.Log("[操作区] 目标离开检测区。", this);
        }
    }

    private bool BelongsToTarget(Collider other)
    {
        if (targetObject == null || other == null)
            return false;

        Transform colliderTransform = other.transform;

        // Collider 就在指定物体上。
        if (colliderTransform == targetObject)
            return true;

        // Collider 在指定物体的子物体上。
        if (colliderTransform.IsChildOf(targetObject))
            return true;

        // 指定的是 Camera，但 Collider 在它的父物体上。
        if (targetObject.IsChildOf(colliderTransform))
            return true;

        return false;
    }

    private void SendTeachingStartCommand(string inputName)
    {
        if (公屏叫号.Instance == null)
        {
            Debug.LogWarning(
                "[操作区] 找不到公屏叫号，无法发送教学启动叫号。",
                this
            );
            return;
        }

        startCommandSent = true;
        SetPromptVisible(false);

        if (printLogs)
        {
            Debug.Log(
                "[操作区] 检测到 " + inputName +
                "，发出叫号：" + teachingStartCommand,
                this
            );
        }

        公屏叫号.Instance.喊话(teachingStartCommand);
    }

    private void SetPromptVisible(bool visible)
    {
        if (teachingPrompt != null && teachingPrompt.activeSelf != visible)
            teachingPrompt.SetActive(visible);
    }

    private void OnDisable()
    {
        detectedColliders.Clear();
        startCommandSent = false;
        SetPromptVisible(false);
    }
}