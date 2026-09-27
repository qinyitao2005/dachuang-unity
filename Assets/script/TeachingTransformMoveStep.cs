using System.Collections;
using UnityEngine;

/// <summary>
/// 教学流程中的通用物体移动步骤。
///
/// 使用方式：
/// 1. 挂在“教学管理系统”下面的任意启用物体上。
/// 2. 填写要移动的物体。
/// 3. 填写目标位置与目标欧拉角。
/// 4. 教学主控通过公屏喊出 startCommand。
/// 5. 本脚本移动完成后，通过公屏回复 completeCommand。
///
/// 本脚本不会在 Start() 自动执行。
/// </summary>
[DefaultExecutionOrder(-700)]
public class TeachingTransformMoveStep : MonoBehaviour
{
    public enum CoordinateMode
    {
        World,
        Local
    }

    [Header("公屏指令")]
    [Tooltip("教学主控喊出这句话时，本步骤开始执行。")]
    public string startCommand = "教学步骤-移动物体";

    [Tooltip("移动和旋转完成后，向教学主控回复这句话。")]
    public string completeCommand = "教学步骤-移动物体完成";

    [Tooltip("执行失败后，向教学主控回复这句话。")]
    public string failedCommand = "教学步骤-移动物体失败";

    [Header("控制对象")]
    [Tooltip("需要移动和旋转的物体。")]
    public Transform objectToMove;

    [Header("坐标模式")]
    [Tooltip("World：填写世界坐标；Local：填写相对于父物体的本地坐标。")]
    public CoordinateMode coordinateMode = CoordinateMode.World;

    [Header("目标位置")]
    [Tooltip("物体最终到达的位置。")]
    public Vector3 targetPosition;

    [Header("目标角度")]
    [Tooltip("物体最终到达的欧拉角，单位为度。")]
    public Vector3 targetEulerAngles;

    [Header("执行内容")]
    [Tooltip("是否改变物体位置。")]
    public bool changePosition = true;

    [Tooltip("是否改变物体角度。")]
    public bool changeRotation = true;

    [Header("运动参数")]
    [Tooltip("从当前位置运动到目标状态所需的时间。填 0 表示瞬间到达。")]
    [Min(0f)]
    public float duration = 1.5f;

    [Tooltip("控制运动过程。默认是缓慢启动、缓慢停止。")]
    public AnimationCurve movementCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("勾选后，不受 Time.timeScale 影响。")]
    public bool useUnscaledTime = false;

    [Header("重复执行")]
    [Tooltip("完成后是否允许再次收到相同启动命令并重新执行。")]
    public bool allowRestartAfterCompletion = true;

    [Header("调试")]
    public bool printLogs = true;

    [SerializeField]
    private bool isRunning = false;

    [SerializeField]
    private bool hasCompleted = false;

    [SerializeField]
    [Range(0f, 1f)]
    private float progress = 0f;

    [SerializeField]
    private bool subscribedToPublicCommand = false;

    [SerializeField]
    private string lastReceivedCommand = "";

    private Coroutine moveCoroutine;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private Vector3 finalPosition;
    private Quaternion finalRotation;

    private bool completionSignalSent = false;
    private bool failureSignalSent = false;

    private void OnEnable()
    {
        TrySubscribeToPublicCommand();
    }

    private void Start()
    {
        TrySubscribeToPublicCommand();

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 正在等待公屏启动命令：" +
                startCommand,
                this
            );
        }
    }

    private void Update()
    {
        // 如果场景启动时公屏还没准备好，后续继续尝试连接。
        if (!subscribedToPublicCommand)
        {
            TrySubscribeToPublicCommand();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromPublicCommand();

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        isRunning = false;
    }

    private void TrySubscribeToPublicCommand()
    {
        if (subscribedToPublicCommand)
        {
            return;
        }

        if (公屏叫号.Instance == null)
        {
            return;
        }

        公屏叫号.Instance.叫号广播.AddListener(
            ReceivePublicCommand
        );

        subscribedToPublicCommand = true;

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 已连接公屏，等待：" +
                startCommand,
                this
            );
        }
    }

    private void UnsubscribeFromPublicCommand()
    {
        if (!subscribedToPublicCommand)
        {
            return;
        }

        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.RemoveListener(
                ReceivePublicCommand
            );
        }

        subscribedToPublicCommand = false;
    }

    /// <summary>
    /// 接收公屏广播。
    /// </summary>
    private void ReceivePublicCommand(string command)
    {
        lastReceivedCommand = command;

        if (command != startCommand)
        {
            return;
        }

        StartSequence();
    }

    /// <summary>
    /// 兼容 UnityEvent 或其他脚本直接传入命令。
    /// </summary>
    public void ReceiveSignal(string command)
    {
        ReceivePublicCommand(command);
    }

    /// <summary>
    /// 统一启动入口。
    /// 教学主控喊出启动命令后，会进入这里。
    /// </summary>
    [ContextMenu("Start Sequence")]
    public void StartSequence()
    {
        if (isRunning)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[TeachingTransformMoveStep] 当前正在执行，忽略重复启动。",
                    this
                );
            }

            return;
        }

        if (hasCompleted && !allowRestartAfterCompletion)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[TeachingTransformMoveStep] 本步骤已经完成，当前禁止重复执行。",
                    this
                );
            }

            return;
        }

        if (objectToMove == null)
        {
            FailSequence("没有填写 Object To Move。");
            return;
        }

        if (!changePosition && !changeRotation)
        {
            FailSequence("位置和角度都没有启用，本步骤没有任何可执行内容。");
            return;
        }

        completionSignalSent = false;
        failureSignalSent = false;

        hasCompleted = false;
        isRunning = true;
        progress = 0f;

        CaptureStartAndTargetState();

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 开始执行。" +
                "\n控制物体：" + objectToMove.name +
                "\n坐标模式：" + coordinateMode +
                "\n目标位置：" + targetPosition +
                "\n目标角度：" + targetEulerAngles +
                "\n运动时间：" + duration + " 秒",
                this
            );
        }

        if (duration <= 0f)
        {
            ApplyFinalState();
            CompleteSequence();
            return;
        }

        moveCoroutine = StartCoroutine(
            MoveRoutine()
        );
    }

    private void CaptureStartAndTargetState()
    {
        if (coordinateMode == CoordinateMode.World)
        {
            startPosition = objectToMove.position;
            startRotation = objectToMove.rotation;

            finalPosition = targetPosition;
            finalRotation = Quaternion.Euler(
                targetEulerAngles
            );
        }
        else
        {
            startPosition = objectToMove.localPosition;
            startRotation = objectToMove.localRotation;

            finalPosition = targetPosition;
            finalRotation = Quaternion.Euler(
                targetEulerAngles
            );
        }
    }

    private IEnumerator MoveRoutine()
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (!isRunning)
            {
                yield break;
            }

            float deltaTime = useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            elapsed += deltaTime;

            progress = Mathf.Clamp01(
                elapsed / duration
            );

            float curvedProgress = movementCurve != null
                ? movementCurve.Evaluate(progress)
                : progress;

            ApplyInterpolatedState(
                curvedProgress
            );

            yield return null;
        }

        ApplyFinalState();

        moveCoroutine = null;

        CompleteSequence();
    }

    private void ApplyInterpolatedState(float t)
    {
        if (coordinateMode == CoordinateMode.World)
        {
            if (changePosition)
            {
                objectToMove.position = Vector3.Lerp(
                    startPosition,
                    finalPosition,
                    t
                );
            }

            if (changeRotation)
            {
                objectToMove.rotation = Quaternion.Slerp(
                    startRotation,
                    finalRotation,
                    t
                );
            }
        }
        else
        {
            if (changePosition)
            {
                objectToMove.localPosition = Vector3.Lerp(
                    startPosition,
                    finalPosition,
                    t
                );
            }

            if (changeRotation)
            {
                objectToMove.localRotation = Quaternion.Slerp(
                    startRotation,
                    finalRotation,
                    t
                );
            }
        }
    }

    private void ApplyFinalState()
    {
        if (objectToMove == null)
        {
            return;
        }

        if (coordinateMode == CoordinateMode.World)
        {
            if (changePosition)
            {
                objectToMove.position = finalPosition;
            }

            if (changeRotation)
            {
                objectToMove.rotation = finalRotation;
            }
        }
        else
        {
            if (changePosition)
            {
                objectToMove.localPosition = finalPosition;
            }

            if (changeRotation)
            {
                objectToMove.localRotation = finalRotation;
            }
        }

        progress = 1f;
    }

    private void CompleteSequence()
    {
        if (completionSignalSent)
        {
            return;
        }

        completionSignalSent = true;

        isRunning = false;
        hasCompleted = true;
        progress = 1f;

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 移动与旋转完成，回复公屏：" +
                completeCommand,
                this
            );
        }

        if (string.IsNullOrWhiteSpace(
            completeCommand
        ))
        {
            return;
        }

        if (公屏叫号.Instance == null)
        {
            Debug.LogWarning(
                "[TeachingTransformMoveStep] 已经完成，但找不到公屏，无法发送完成信号。",
                this
            );

            return;
        }

        公屏叫号.Instance.喊话(
            completeCommand
        );
    }

    private void FailSequence(string reason)
    {
        if (failureSignalSent)
        {
            return;
        }

        failureSignalSent = true;

        isRunning = false;
        hasCompleted = false;
        progress = 0f;

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        Debug.LogError(
            "[TeachingTransformMoveStep] 执行失败：" +
            reason,
            this
        );

        if (string.IsNullOrWhiteSpace(
            failedCommand
        ))
        {
            return;
        }

        if (公屏叫号.Instance == null)
        {
            Debug.LogWarning(
                "[TeachingTransformMoveStep] 执行失败，但找不到公屏，无法发送失败信号。",
                this
            );

            return;
        }

        公屏叫号.Instance.喊话(
            failedCommand
        );
    }

    /// <summary>
    /// 手动停止，不向老板发送完成或失败信号。
    /// </summary>
    [ContextMenu("Stop Sequence")]
    public void StopSequence()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        isRunning = false;

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 已手动停止。",
                this
            );
        }
    }

    /// <summary>
    /// 清除完成状态，允许重新执行。
    /// </summary>
    [ContextMenu("Reset Sequence")]
    public void ResetSequence()
    {
        StopSequence();

        hasCompleted = false;
        progress = 0f;

        completionSignalSent = false;
        failureSignalSent = false;

        if (printLogs)
        {
            Debug.Log(
                "[TeachingTransformMoveStep] 状态已重置。",
                this
            );
        }
    }

    public bool IsRunning()
    {
        return isRunning;
    }

    public bool IsCompleted()
    {
        return hasCompleted;
    }
}