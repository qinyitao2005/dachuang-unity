using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class PublicCommandSequenceStep
{
    [Header("步骤说明")]
    [Tooltip("只用于 Inspector 和日志，随便填写")]
    public string stepName = "新步骤";

    [Header("主控叫醒它")]
    [Tooltip("进入本步骤时，主控向公屏喊出的命令")]
    public string startCommand = "";

    [Header("等它回复")]
    [Tooltip("收到这句话，主控才会进入下一步骤")]
    public string completeCommand = "";

    [Tooltip("可选。收到这句话，本次流程立即失败")]
    public string failedCommand = "";

    [Header("超时")]
    [Tooltip("0 表示永不超时")]
    [Min(0f)]
    public float timeoutSeconds = 0f;

    [Header("可选事件")]
    [Tooltip("本步骤开始时额外执行")]
    public UnityEvent onStepStarted = new UnityEvent();

    [Tooltip("本步骤完成时额外执行")]
    public UnityEvent onStepCompleted = new UnityEvent();
}

[DefaultExecutionOrder(-800)]
public class PublicCommandSequenceController : MonoBehaviour
{
    [Header("流程入口")]
    [Tooltip("公屏出现这句话时，启动整套流程")]
    [SerializeField]
    private string sequenceStartCommand = "教学模式开启";

    [Header("流程步骤")]
    [SerializeField]
    private List<PublicCommandSequenceStep> steps =
        new List<PublicCommandSequenceStep>();

    [Header("最终叫号")]
    [Tooltip("所有步骤完成后向公屏喊出的命令，可留空")]
    [SerializeField]
    private string sequenceCompletedCommand = "教学启动流程完成";

    [Tooltip("某步骤失败或超时后向公屏喊出的命令，可留空")]
    [SerializeField]
    private string sequenceFailedCommand = "教学启动流程失败";

    [Header("重复运行")]
    [Tooltip("完成后是否允许再次收到入口命令并重新运行")]
    [SerializeField]
    private bool allowRestartAfterCompletion = true;

    [Header("流程事件")]
    [SerializeField]
    private UnityEvent onSequenceStarted = new UnityEvent();

    [SerializeField]
    private UnityEvent onSequenceCompleted = new UnityEvent();

    [SerializeField]
    private UnityEvent onSequenceFailed = new UnityEvent();

    [Header("调试")]
    [SerializeField]
    private bool printLogs = true;

    [SerializeField]
    private bool isRunning;

    [SerializeField]
    private bool hasCompleted;

    [SerializeField]
    private int currentStepIndex = -1;

    [SerializeField]
    private string currentStepName = "";

    [SerializeField]
    private string waitingForCommand = "";

    [SerializeField]
    private string lastReceivedCommand = "";

    [SerializeField]
    private float currentStepElapsed;

    private bool subscribed;
    private bool pendingStartCurrentStep;

    public bool IsRunning
    {
        get { return isRunning; }
    }

    public int CurrentStepIndex
    {
        get { return currentStepIndex; }
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // 场景初始化时若公屏还没准备好，继续尝试连接
        if (!subscribed)
            TrySubscribe();

        // 延迟到下一帧启动步骤，避免公屏广播嵌套调用
        if (pendingStartCurrentStep)
        {
            pendingStartCurrentStep = false;
            StartCurrentStep();
        }

        if (!isRunning)
            return;

        if (!IsCurrentStepValid())
            return;

        PublicCommandSequenceStep step = steps[currentStepIndex];

        if (step.timeoutSeconds <= 0f)
            return;

        currentStepElapsed += Time.deltaTime;

        if (currentStepElapsed >= step.timeoutSeconds)
        {
            FailSequence(
                "步骤超时：" + step.stepName +
                "，等待信号：" + step.completeCommand
            );
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed)
            return;

        if (公屏叫号.Instance == null)
            return;

        公屏叫号.Instance.叫号广播.AddListener(
            ReceivePublicCommand
        );

        subscribed = true;

        if (printLogs)
        {
            Debug.Log(
                "[流程主控] 已连接公屏，等待入口命令：" +
                sequenceStartCommand,
                this
            );
        }
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (公屏叫号.Instance != null)
        {
            公屏叫号.Instance.叫号广播.RemoveListener(
                ReceivePublicCommand
            );
        }

        subscribed = false;
    }

    /// <summary>
    /// 公屏广播入口。
    /// </summary>
    private void ReceivePublicCommand(string command)
    {
        lastReceivedCommand = command;

        // 当前没有运行，只检查是否收到启动整套流程的命令
        if (!isRunning)
        {
            if (command == sequenceStartCommand)
            {
                StartSequence();
            }

            return;
        }

        if (!IsCurrentStepValid())
            return;

        PublicCommandSequenceStep step = steps[currentStepIndex];

        // 收到当前步骤失败信号
        if (!string.IsNullOrWhiteSpace(step.failedCommand) &&
            command == step.failedCommand)
        {
            FailSequence(
                "步骤报告失败：" + step.stepName +
                "，信号：" + command
            );

            return;
        }

        // 不是当前步骤等待的完成信号，直接忽略
        if (command != step.completeCommand)
            return;

        CompleteCurrentStep();
    }

    /// <summary>
    /// 兼容其他脚本或 UnityEvent 直接传入信号。
    /// </summary>
    public void ReceiveSignal(string command)
    {
        ReceivePublicCommand(command);
    }

    /// <summary>
    /// 也可以不通过入口叫号，直接由按钮或 UnityEvent 启动。
    /// </summary>
    public void StartSequence()
    {
        if (isRunning)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[流程主控] 流程正在运行，忽略重复启动。",
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
                    "[流程主控] 流程已经完成，当前禁止重新启动。",
                    this
                );
            }

            return;
        }

        if (steps == null || steps.Count == 0)
        {
            FailSequence("步骤列表为空。");
            return;
        }

        isRunning = true;
        hasCompleted = false;
        currentStepIndex = 0;
        currentStepElapsed = 0f;
        pendingStartCurrentStep = true;

        onSequenceStarted.Invoke();

        if (printLogs)
        {
            Debug.Log(
                "[流程主控] 收到启动命令，流程开始，共 " +
                steps.Count + " 个步骤。",
                this
            );
        }
    }

    /// <summary>从指定命令恢复教学，保留已经完成的机械臂初始化姿态。</summary>
    public bool StartSequenceFromCommand(string command)
    {
        if (isRunning || steps == null) return false;
        int index = steps.FindIndex(step => step != null && step.startCommand == command);
        if (index < 0) return false;
        StartSequence();
        if (!isRunning) return false;
        // StartSequence 将真正启动延迟到 Update，因此此处不会先执行第零步。
        currentStepIndex = index;
        return true;
    }

    private void StartCurrentStep()
    {
        if (!isRunning)
            return;

        if (currentStepIndex >= steps.Count)
        {
            CompleteSequence();
            return;
        }

        if (!IsCurrentStepValid())
        {
            FailSequence(
                "步骤索引无效：" + currentStepIndex
            );

            return;
        }

        PublicCommandSequenceStep step = steps[currentStepIndex];

        if (string.IsNullOrWhiteSpace(step.startCommand))
        {
            FailSequence(
                "步骤“" + step.stepName +
                "”没有填写 Start Command。"
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(step.completeCommand))
        {
            FailSequence(
                "步骤“" + step.stepName +
                "”没有填写 Complete Command。"
            );

            return;
        }

        if (公屏叫号.Instance == null)
        {
            FailSequence("找不到公屏叫号。");
            return;
        }

        currentStepName = step.stepName;
        waitingForCommand = step.completeCommand;
        currentStepElapsed = 0f;

        if (printLogs)
        {
            Debug.Log(
                "[流程主控] 启动 Step " +
                (currentStepIndex + 1) +
                "：" + step.stepName +
                "；叫号：" + step.startCommand +
                "；等待：" + step.completeCommand,
                this
            );
        }

        step.onStepStarted.Invoke();

        // 主控叫醒当前步骤
        公屏叫号.Instance.喊话(step.startCommand);
    }

    private void CompleteCurrentStep()
    {
        if (!isRunning || !IsCurrentStepValid())
            return;

        PublicCommandSequenceStep step = steps[currentStepIndex];

        if (printLogs)
        {
            Debug.Log(
                "[流程主控] Step " +
                (currentStepIndex + 1) +
                " 完成：" + step.stepName +
                "；收到：" + step.completeCommand,
                this
            );
        }

        step.onStepCompleted.Invoke();

        currentStepIndex++;
        currentStepElapsed = 0f;
        waitingForCommand = "";

        if (currentStepIndex >= steps.Count)
        {
            CompleteSequence();
            return;
        }

        // 下一帧再叫醒下一个，避免广播递归
        pendingStartCurrentStep = true;
    }

    private void CompleteSequence()
    {
        isRunning = false;
        hasCompleted = true;
        currentStepName = "";
        waitingForCommand = "";
        currentStepElapsed = 0f;
        pendingStartCurrentStep = false;

        if (printLogs)
        {
            Debug.Log("[流程主控] 所有步骤已完成。", this);
        }

        onSequenceCompleted.Invoke();

        if (!string.IsNullOrWhiteSpace(sequenceCompletedCommand) &&
            公屏叫号.Instance != null)
        {
            公屏叫号.Instance.喊话(
                sequenceCompletedCommand
            );
        }
    }

    private void FailSequence(string reason)
    {
        isRunning = false;
        currentStepName = "";
        waitingForCommand = "";
        currentStepElapsed = 0f;
        pendingStartCurrentStep = false;

        Debug.LogError(
            "[流程主控] 流程失败：" + reason,
            this
        );

        onSequenceFailed.Invoke();

        if (!string.IsNullOrWhiteSpace(sequenceFailedCommand) &&
            公屏叫号.Instance != null)
        {
            公屏叫号.Instance.喊话(
                sequenceFailedCommand
            );
        }
    }

    private bool IsCurrentStepValid()
    {
        return steps != null &&
               currentStepIndex >= 0 &&
               currentStepIndex < steps.Count &&
               steps[currentStepIndex] != null;
    }

    /// <summary>
    /// 停止当前流程，但不发送失败信号。
    /// </summary>
    public void StopSequence()
    {
        isRunning = false;
        currentStepIndex = -1;
        currentStepName = "";
        waitingForCommand = "";
        currentStepElapsed = 0f;
        pendingStartCurrentStep = false;

        if (printLogs)
        {
            Debug.Log("[流程主控] 流程已手动停止。", this);
        }
    }

    /// <summary>
    /// 清空完成状态，允许从头重新运行。
    /// </summary>
    public void ResetSequence()
    {
        StopSequence();
        hasCompleted = false;

        if (printLogs)
        {
            Debug.Log("[流程主控] 流程状态已重置。", this);
        }
    }
}
