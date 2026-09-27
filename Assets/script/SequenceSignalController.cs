using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 初始化流程管理器。
///
/// 上级关系：
/// PublicCommandSequenceController 是老板。
///
/// 工作流程：
/// 1. 平时等待老板通过公屏喊出 publicStartCommand。
/// 2. 收到命令后，调用本脚本原来的 StartSequence()。
/// 3. 内部所有初始化步骤仍按照原来的顺序执行。
/// 4. 全部初始化完成后，向公屏回复 publicCompleteReplyCommand。
/// 5. 初始化失败后，向公屏回复 publicFailedReplyCommand。
///
/// 注意：
/// 内部初始化步骤、调用方式、完成信号、控制权释放逻辑均未改变。
/// </summary>
public class SequenceSignalController : MonoBehaviour
{
    [Serializable]
    public class SequenceCommandEvent : UnityEvent<string>
    {
    }

    public enum StepState
    {
        Waiting,
        Running,
        Complete,
        Skipped,
        Error
    }

    [Serializable]
    public class SequenceStep
    {
        [Header("步骤名称，只是方便你看")]
        public string stepName = "步骤";

        [Header("是否启用这个步骤")]
        public bool enabled = true;

        [Header("挂着目标脚本的物体")]
        [Tooltip("比如：整体 - ik代码。这个物体上应该挂着目标脚本。")]
        public GameObject targetObject;

        [Header("目标脚本类名")]
        [Tooltip("必须和脚本 class 名字完全一致，比如 DirectKeyDockingIK / AttachPartToSocket / PenAsEndEffectorSequenceIK。")]
        public string targetScriptClassName = "DirectKeyDockingIK";

        [Header("要调用的方法名")]
        [Tooltip("比如 StartDocking / Attach / StartSequence / StartFromCommand。")]
        public string startMethodName = "StartSequence";

        [Header("是否给启动方法传入字符串参数")]
        [Tooltip("如果方法是 StartFromCommand(string)，就勾上；如果方法是 StartDocking() / Attach() / StartSequence()，就不要勾。")]
        public bool passStartCommand = false;

        [Header("启动时传入的字符串")]
        [Tooltip("只有 passStartCommand 勾上时才有用。")]
        public string startCommand = "指令-启动";

        [Header("等待这个完成信号")]
        [Tooltip("这个步骤完成后，目标脚本必须发回这个字符串。")]
        public string expectedCompleteCommand = "指令-xxx已经完成";

        [Header("可选：等待这个失败信号")]
        [Tooltip("如果目标脚本失败时会发失败信号，可以填。收到后流程会停止。留空则不检查失败信号。")]
        public string expectedFailedCommand = "";

        [Header("完成后是否释放本步骤控制权")]
        [Tooltip("如果这个步骤完成后还会在 Update/LateUpdate 里继续控制机械臂，就勾上。比如 PenAsEndEffectorSequenceIK。")]
        public bool releaseControlAfterComplete = false;

        [Tooltip("优先调用的释放方法名。常用：StopSequence / ReleaseControl / StopDocking / StopAxisAlign。")]
        public string releaseMethodName = "StopSequence";

        [Tooltip("如果 releaseMethodName 找不到，是否自动尝试 ReleaseControl / StopSequence / StopDocking / StopAxisAlign。")]
        public bool tryFallbackReleaseMethods = true;

        [Header("运行时显示")]
        public StepState state = StepState.Waiting;

        [NonSerialized]
        public MonoBehaviour cachedTargetScript;
    }

    [Header("是否运行时自动开始")]
    [Tooltip("作为上级流程的下属初始化管理器时，请取消勾选。")]
    public bool autoStart = false;

    // =========================================================
    // 新增：上级老板叫醒与完成汇报
    // =========================================================

    [Header("听从上级公屏指令")]
    [Tooltip("勾选后，本初始化管理器只有听到上级主控的命令才开始工作。")]
    public bool listenToPublicCommand = true;

    [Tooltip("上级 PublicCommandSequenceController 在该步骤喊出的 Start Command。")]
    public string publicStartCommand = "开始执行初始化";

    [Tooltip("内部全部初始化完成后向公屏回复。必须与上级步骤的 Complete Command 完全一致。")]
    public string publicCompleteReplyCommand = "初始化已经完成";

    [Tooltip("内部初始化失败后向公屏回复。必须与上级步骤的 Failed Command 完全一致。")]
    public string publicFailedReplyCommand = "初始化执行失败";

    [Tooltip("已经完成后，是否允许再次收到同一命令并重新执行初始化。")]
    public bool allowRestartFromPublicCommand = false;

    [Tooltip("是否打印接收老板命令和回复老板的日志。")]
    public bool printPublicCommandLogs = true;

    [Header("步骤列表")]
    public SequenceStep[] steps;

    [Header("总控信号命名")]
    public string sequenceStartCommand = "指令-总流程开始执行";
    public string sequenceCompleteCommand = "指令-总流程已经完成";
    public string sequenceFailedCommand = "指令-总流程执行失败";

    [Header("总控事件")]
    [Tooltip("总流程开始时触发。一般不用接。")]
    public SequenceCommandEvent onSequenceStarted;

    [Tooltip("总流程全部完成时触发。")]
    public SequenceCommandEvent onSequenceCompleted;

    [Tooltip("总流程失败时触发。")]
    public SequenceCommandEvent onSequenceFailed;

    [Header("运行时显示")]
    [SerializeField]
    private int currentStepIndex = -1;

    [SerializeField]
    private bool isRunning = false;

    [SerializeField]
    private bool allCompleted = false;

    [SerializeField]
    private bool failed = false;

    [Header("上级公屏通信运行时显示")]
    [SerializeField]
    private bool subscribedToPublicCommand = false;

    [SerializeField]
    private string lastPublicCommand = "";

    private bool sequenceStartSignalSent = false;
    private bool sequenceCompleteSignalSent = false;
    private bool sequenceFailedSignalSent = false;

    // =========================================================
    // 新增：生命周期和公屏监听
    // =========================================================

    private void Awake()
    {
        ResetRuntimeState();
    }

    private void OnEnable()
    {
        TrySubscribeToPublicCommand();
    }

    private void Start()
    {
        TrySubscribeToPublicCommand();

        // 使用公屏控制时，不允许自动启动。
        if (autoStart && !listenToPublicCommand)
        {
            StartSequence();
        }

        if (listenToPublicCommand && printPublicCommandLogs)
        {
            Debug.Log(
                "[SequenceSignalController] 初始化管理器正在等待上级命令：" +
                publicStartCommand,
                this
            );
        }
    }

    private void Update()
    {
        // 场景加载时，公屏可能还没有完成初始化。
        // 如果第一次没连接成功，之后继续尝试。
        if (listenToPublicCommand && !subscribedToPublicCommand)
        {
            TrySubscribeToPublicCommand();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromPublicCommand();
    }

    private void TrySubscribeToPublicCommand()
    {
        if (!listenToPublicCommand)
        {
            return;
        }

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

        if (printPublicCommandLogs)
        {
            Debug.Log(
                "[SequenceSignalController] 已连接公屏，等待上级启动命令：" +
                publicStartCommand,
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
    /// 这里只负责检查老板有没有叫醒本初始化管理器。
    /// 不干涉内部初始化步骤的完成信号。
    /// </summary>
    private void ReceivePublicCommand(string command)
    {
        lastPublicCommand = command;

        if (!listenToPublicCommand)
        {
            return;
        }

        // 不是老板叫醒初始化管理器的命令，直接忽略。
        if (command != publicStartCommand)
        {
            return;
        }

        // 防止同一句命令连续广播，导致初始化被重复启动。
        if (isRunning)
        {
            if (printPublicCommandLogs)
            {
                Debug.LogWarning(
                    "[SequenceSignalController] 初始化正在运行，忽略重复启动命令：" +
                    command,
                    this
                );
            }

            return;
        }

        if (allCompleted && !allowRestartFromPublicCommand)
        {
            if (printPublicCommandLogs)
            {
                Debug.LogWarning(
                    "[SequenceSignalController] 初始化已经完成，当前不允许重复运行。",
                    this
                );
            }

            return;
        }

        if (printPublicCommandLogs)
        {
            Debug.Log(
                "[SequenceSignalController] 收到上级命令，开始执行全部初始化：" +
                command,
                this
            );
        }

        // 只调用原来的初始化入口。
        // 后续内部步骤管理逻辑完全不变。
        StartSequence();
    }

    // =========================================================
    // 原初始化流程入口
    // =========================================================

    [ContextMenu("Start Sequence")]
    public void StartSequence()
    {
        if (steps == null || steps.Length == 0)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 没有填写任何步骤。"
            );

            return;
        }

        ResetRuntimeState();

        if (!CacheAllTargetScripts())
        {
            FailSequence(
                "[SequenceSignalController] 有步骤找不到目标脚本，流程不启动。"
            );

            return;
        }

        isRunning = true;
        allCompleted = false;
        failed = false;

        Debug.Log("[SequenceSignalController] 流程开始。");

        SendSequenceStartSignal();

        StartNextStep();
    }

    [ContextMenu("Stop Sequence")]
    public void StopSequence()
    {
        if (currentStepIndex >= 0 &&
            currentStepIndex < steps.Length)
        {
            ReleaseStepControlIfNeeded(
                steps[currentStepIndex]
            );
        }

        isRunning = false;

        Debug.Log(
            "[SequenceSignalController] 流程已停止。"
        );
    }

    [ContextMenu("Reset Runtime State")]
    public void ResetRuntimeState()
    {
        currentStepIndex = -1;
        isRunning = false;
        allCompleted = false;
        failed = false;

        sequenceStartSignalSent = false;
        sequenceCompleteSignalSent = false;
        sequenceFailedSignalSent = false;

        if (steps == null)
        {
            return;
        }

        foreach (SequenceStep step in steps)
        {
            if (step == null)
            {
                continue;
            }

            step.state = StepState.Waiting;
            step.cachedTargetScript = null;
        }
    }

    private bool CacheAllTargetScripts()
    {
        bool allOk = true;

        if (steps == null)
        {
            return false;
        }

        for (int i = 0; i < steps.Length; i++)
        {
            SequenceStep step = steps[i];

            if (step == null)
            {
                continue;
            }

            if (!step.enabled)
            {
                step.state = StepState.Skipped;
                continue;
            }

            MonoBehaviour script = FindTargetScript(step);

            if (script == null)
            {
                allOk = false;

                Debug.LogError(
                    "[SequenceSignalController] Step " +
                    (i + 1) +
                    " 找不到目标脚本。\n" +
                    "Step Name = " +
                    step.stepName +
                    "\n" +
                    "Target Object = " +
                    (
                        step.targetObject == null
                            ? "空"
                            : step.targetObject.name
                    ) +
                    "\n" +
                    "Target Script Class Name = " +
                    step.targetScriptClassName
                );
            }
            else
            {
                step.cachedTargetScript = script;

                Debug.Log(
                    "[SequenceSignalController] Step " +
                    (i + 1) +
                    " 已绑定脚本：" +
                    script.GetType().Name
                );
            }
        }

        return allOk;
    }

    private MonoBehaviour FindTargetScript(
        SequenceStep step
    )
    {
        if (step == null)
        {
            return null;
        }

        if (step.targetObject == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(
            step.targetScriptClassName
        ))
        {
            return null;
        }

        MonoBehaviour[] scripts =
            step.targetObject.GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour script in scripts)
        {
            if (script == null)
            {
                continue;
            }

            Type type = script.GetType();

            if (type.Name == step.targetScriptClassName)
            {
                return script;
            }
        }

        return null;
    }

    /// <summary>
    /// 内部目标脚本完成后，把完成信号发到这里。
    ///
    /// 此部分仍然是原来的内部初始化信号入口。
    /// </summary>
    public void ReceiveSignal(string command)
    {
        Debug.Log(
            "[SequenceSignalController] 收到信号：" +
            command
        );

        if (!isRunning)
        {
            Debug.Log(
                "[SequenceSignalController] 当前流程没有运行，忽略信号：" +
                command
            );

            return;
        }

        if (currentStepIndex < 0 ||
            currentStepIndex >= steps.Length)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 当前步骤索引异常，忽略信号：" +
                command
            );

            return;
        }

        SequenceStep currentStep =
            steps[currentStepIndex];

        if (currentStep == null)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 当前步骤为空，忽略信号：" +
                command
            );

            return;
        }

        if (!string.IsNullOrEmpty(
                currentStep.expectedFailedCommand
            ) &&
            command ==
            currentStep.expectedFailedCommand)
        {
            currentStep.state = StepState.Error;

            ReleaseStepControlIfNeeded(currentStep);

            FailSequence(
                "[SequenceSignalController] 当前步骤失败：" +
                "Step " +
                (currentStepIndex + 1) +
                " - " +
                currentStep.stepName +
                "\n失败信号：" +
                command
            );

            return;
        }

        if (command !=
            currentStep.expectedCompleteCommand)
        {
            Debug.Log(
                "[SequenceSignalController] 信号不是当前步骤需要的完成信号，忽略。\n" +
                "当前步骤：" +
                "Step " +
                (currentStepIndex + 1) +
                " - " +
                currentStep.stepName +
                "\n收到：" +
                command +
                "\n需要：" +
                currentStep.expectedCompleteCommand
            );

            return;
        }

        currentStep.state = StepState.Complete;

        Debug.Log(
            "[SequenceSignalController] 步骤完成：" +
            "Step " +
            (currentStepIndex + 1) +
            " - " +
            currentStep.stepName
        );

        ReleaseStepControlIfNeeded(currentStep);

        StartNextStep();
    }

    /// <summary>
    /// ReceiveSignal 的别名。
    /// </summary>
    public void ReportComplete(string command)
    {
        ReceiveSignal(command);
    }

    private void StartNextStep()
    {
        int nextIndex =
            FindNextEnabledStep(currentStepIndex);

        if (nextIndex < 0)
        {
            CompleteAllSteps();
            return;
        }

        currentStepIndex = nextIndex;

        SequenceStep step =
            steps[currentStepIndex];

        if (step == null)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 步骤为空，跳过。"
            );

            StartNextStep();
            return;
        }

        step.state = StepState.Running;

        Debug.Log(
            "[SequenceSignalController] 启动步骤：" +
            "Step " +
            (currentStepIndex + 1) +
            " - " +
            step.stepName
        );

        bool success =
            InvokeStepStartMethod(step);

        if (!success)
        {
            step.state = StepState.Error;

            ReleaseStepControlIfNeeded(step);

            FailSequence(
                "[SequenceSignalController] 启动步骤失败，流程停止：" +
                "Step " +
                (currentStepIndex + 1) +
                " - " +
                step.stepName
            );
        }
    }

    private int FindNextEnabledStep(int afterIndex)
    {
        if (steps == null)
        {
            return -1;
        }

        for (
            int i = afterIndex + 1;
            i < steps.Length;
            i++
        )
        {
            SequenceStep step = steps[i];

            if (step == null)
            {
                continue;
            }

            if (!step.enabled)
            {
                step.state = StepState.Skipped;
                continue;
            }

            return i;
        }

        return -1;
    }

    private bool InvokeStepStartMethod(
        SequenceStep step
    )
    {
        MonoBehaviour targetScript =
            step.cachedTargetScript;

        if (targetScript == null)
        {
            targetScript = FindTargetScript(step);
            step.cachedTargetScript = targetScript;
        }

        if (targetScript == null)
        {
            Debug.LogError(
                "[SequenceSignalController] targetScript 没找到：" +
                step.stepName +
                " / " +
                step.targetScriptClassName
            );

            return false;
        }

        if (string.IsNullOrEmpty(
            step.startMethodName
        ))
        {
            Debug.LogError(
                "[SequenceSignalController] startMethodName 没填：" +
                step.stepName
            );

            return false;
        }

        Type scriptType =
            targetScript.GetType();

        try
        {
            if (step.passStartCommand)
            {
                MethodInfo methodWithString =
                    scriptType.GetMethod(
                        step.startMethodName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic,
                        null,
                        new Type[]
                        {
                            typeof(string)
                        },
                        null
                    );

                if (methodWithString == null)
                {
                    Debug.LogError(
                        "[SequenceSignalController] 找不到带 string 参数的方法：" +
                        scriptType.Name +
                        "." +
                        step.startMethodName +
                        "(string)"
                    );

                    return false;
                }

                Debug.Log(
                    "[SequenceSignalController] 发出启动指令：" +
                    step.startCommand
                );

                methodWithString.Invoke(
                    targetScript,
                    new object[]
                    {
                        step.startCommand
                    }
                );

                return true;
            }
            else
            {
                MethodInfo methodNoParam =
                    scriptType.GetMethod(
                        step.startMethodName,
                        BindingFlags.Instance |
                        BindingFlags.Public |
                        BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null
                    );

                if (methodNoParam == null)
                {
                    Debug.LogError(
                        "[SequenceSignalController] 找不到无参数方法：" +
                        scriptType.Name +
                        "." +
                        step.startMethodName +
                        "()"
                    );

                    return false;
                }

                methodNoParam.Invoke(
                    targetScript,
                    null
                );

                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[SequenceSignalController] 调用步骤方法时出错：" +
                step.stepName +
                "\n" +
                e
            );

            return false;
        }
    }

    private void ReleaseStepControlIfNeeded(
        SequenceStep step
    )
    {
        if (step == null)
        {
            return;
        }

        if (!step.releaseControlAfterComplete)
        {
            return;
        }

        MonoBehaviour targetScript =
            step.cachedTargetScript;

        if (targetScript == null)
        {
            targetScript = FindTargetScript(step);
            step.cachedTargetScript = targetScript;
        }

        if (targetScript == null)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 想释放控制权，但找不到目标脚本：" +
                step.stepName +
                " / " +
                step.targetScriptClassName
            );

            return;
        }

        bool released = false;

        if (!string.IsNullOrEmpty(
            step.releaseMethodName
        ))
        {
            released = TryInvokeNoParamMethod(
                targetScript,
                step.releaseMethodName,
                "[SequenceSignalController] 释放控制权"
            );
        }

        if (!released &&
            step.tryFallbackReleaseMethods)
        {
            string[] fallbackMethods =
            {
                "ReleaseControl",
                "StopSequence",
                "StopDocking",
                "StopAxisAlign",
                "Stop"
            };

            for (
                int i = 0;
                i < fallbackMethods.Length;
                i++
            )
            {
                released =
                    TryInvokeNoParamMethod(
                        targetScript,
                        fallbackMethods[i],
                        "[SequenceSignalController] 自动释放控制权"
                    );

                if (released)
                {
                    break;
                }
            }
        }

        if (!released)
        {
            Debug.LogWarning(
                "[SequenceSignalController] 没有找到可用的释放方法。" +
                "\nStep = " +
                step.stepName +
                "\nScript = " +
                targetScript.GetType().Name +
                "\n你可以在目标脚本里添加 ReleaseControl() 或 StopSequence()。"
            );
        }
    }

    private bool TryInvokeNoParamMethod(
        MonoBehaviour targetScript,
        string methodName,
        string logPrefix
    )
    {
        if (targetScript == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        Type scriptType =
            targetScript.GetType();

        MethodInfo method =
            scriptType.GetMethod(
                methodName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null
            );

        if (method == null)
        {
            return false;
        }

        try
        {
            method.Invoke(targetScript, null);

            Debug.Log(
                logPrefix +
                "：已调用 " +
                scriptType.Name +
                "." +
                methodName +
                "()"
            );

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(
                logPrefix +
                "失败：" +
                scriptType.Name +
                "." +
                methodName +
                "()\n" +
                e
            );

            return false;
        }
    }

    private void SendSequenceStartSignal()
    {
        if (sequenceStartSignalSent)
        {
            return;
        }

        sequenceStartSignalSent = true;

        Debug.Log(sequenceStartCommand);

        if (onSequenceStarted != null)
        {
            onSequenceStarted.Invoke(
                sequenceStartCommand
            );
        }
    }

    private void CompleteAllSteps()
    {
        isRunning = false;
        allCompleted = true;
        failed = false;

        SendSequenceCompleteSignal();
    }

    private void SendSequenceCompleteSignal()
    {
        if (sequenceCompleteSignalSent)
        {
            return;
        }

        sequenceCompleteSignalSent = true;

        Debug.Log(
            "[SequenceSignalController] 所有步骤已经完成。"
        );

        Debug.Log(sequenceCompleteCommand);

        if (onSequenceCompleted != null)
        {
            onSequenceCompleted.Invoke(
                sequenceCompleteCommand
            );
        }

        // 新增：
        // 内部所有初始化步骤完成后，向老板汇报。
        ReplyToPublicCommandBoss(
            publicCompleteReplyCommand,
            "初始化全部完成"
        );
    }

    private void FailSequence(string reason)
    {
        isRunning = false;
        allCompleted = false;
        failed = true;

        Debug.LogError(reason);

        SendSequenceFailedSignal();
    }

    private void SendSequenceFailedSignal()
    {
        if (sequenceFailedSignalSent)
        {
            return;
        }

        sequenceFailedSignalSent = true;

        Debug.Log(sequenceFailedCommand);

        if (onSequenceFailed != null)
        {
            onSequenceFailed.Invoke(
                sequenceFailedCommand
            );
        }

        // 新增：
        // 内部初始化失败后，向老板汇报。
        ReplyToPublicCommandBoss(
            publicFailedReplyCommand,
            "初始化执行失败"
        );
    }

    /// <summary>
    /// 向上级 PublicCommandSequenceController 汇报结果。
    /// </summary>
    private void ReplyToPublicCommandBoss(
        string replyCommand,
        string description
    )
    {
        if (!listenToPublicCommand)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
            replyCommand
        ))
        {
            return;
        }

        if (公屏叫号.Instance == null)
        {
            Debug.LogWarning(
                "[SequenceSignalController] " +
                description +
                "，但找不到公屏，无法回复上级。",
                this
            );

            return;
        }

        if (printPublicCommandLogs)
        {
            Debug.Log(
                "[SequenceSignalController] " +
                description +
                "，向上级回复：" +
                replyCommand,
                this
            );
        }

        公屏叫号.Instance.喊话(
            replyCommand
        );
    }

    public int GetCurrentStepIndex()
    {
        return currentStepIndex;
    }

    public bool IsRunning()
    {
        return isRunning;
    }

    public bool IsAllCompleted()
    {
        return allCompleted;
    }

    public bool IsFailed()
    {
        return failed;
    }
}