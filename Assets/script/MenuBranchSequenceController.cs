using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#region 通用配置类型

/// <summary>
/// 一个分支步骤采用什么方式判定完成。
/// </summary>
public enum MenuBranchStepCompleteMode
{
    /// <summary>
    /// 执行开始事件、发送开始命令后立即完成。
    /// </summary>
    Immediate,

    /// <summary>
    /// 等待公屏出现指定完成命令。
    /// </summary>
    WaitPublicCommand,

    /// <summary>
    /// 等待指定秒数后自动完成。
    /// </summary>
    WaitSeconds,

    /// <summary>
    /// 等待其他脚本手动调用完成函数。
    /// </summary>
    Manual
}

/// <summary>
/// 步骤运行状态，仅用于 Inspector 调试。
/// </summary>
public enum MenuBranchStepRuntimeState
{
    Waiting,
    Running,
    Complete,
    Skipped,
    Failed
}

/// <summary>
/// 可以在 Inspector 中显示的字符串事件。
/// </summary>
[Serializable]
public class MenuBranchStringEvent : UnityEvent<string>
{
}

/// <summary>
/// 一个完全开放式步骤。
/// </summary>
[Serializable]
public class MenuBranchStep
{
    [Header("步骤基本信息")]
    [Tooltip("只用于 Inspector 和日志。")]
    public string stepName = "新步骤";

    [Tooltip("取消勾选后，本步骤会被自动跳过。")]
    public bool enabled = true;

    [TextArea(2, 5)]
    [Tooltip("可选，仅用于记录本步骤的作用。")]
    public string description = "";

    [Header("步骤开始")]
    [Tooltip("步骤开始时向公屏喊出的命令。留空则不喊。")]
    public string startCommand = "";

    [Tooltip("步骤开始时执行。可以自由绑定任意公开函数。")]
    public UnityEvent onStepStarted = new UnityEvent();

    [Header("步骤完成方式")]
    public MenuBranchStepCompleteMode completeMode =
        MenuBranchStepCompleteMode.WaitPublicCommand;

    [Tooltip("仅 WaitPublicCommand 使用。收到这句话后完成当前步骤。")]
    public string completeCommand = "";

    [Tooltip("仅 WaitSeconds 使用。等待多少秒后自动完成。")]
    [Min(0f)]
    public float waitSeconds = 1f;

    [Header("失败设置")]
    [Tooltip("可选。当前步骤运行时，公屏出现这句话则步骤失败。")]
    public string failedCommand = "";

    [Tooltip("WaitPublicCommand 或 Manual 模式的超时时间。0 表示不超时。")]
    [Min(0f)]
    public float timeoutSeconds = 0f;

    [Header("步骤结束事件")]
    [Tooltip("步骤成功完成时执行。")]
    public UnityEvent onStepCompleted = new UnityEvent();

    [Tooltip("步骤失败时执行。")]
    public UnityEvent onStepFailed = new UnityEvent();

    [Header("运行时显示")]
    public MenuBranchStepRuntimeState runtimeState =
        MenuBranchStepRuntimeState.Waiting;
}

/// <summary>
/// 菜单中的一个分支。
/// </summary>
[Serializable]
public class MenuBranchConfig
{
    [Header("分支基本信息")]
    [Tooltip("例如：分支1、机械臂操作、信息查看。")]
    public string branchName = "新分支";

    [Tooltip("菜单输入对应的标识，例如 A、X、1、2。")]
    public string inputId = "A";

    [Header("开放式步骤列表")]
    [Tooltip("Size 就是该分支的步骤数量，可以任意增加和减少。")]
    public List<MenuBranchStep> steps =
        new List<MenuBranchStep>();

    [Header("分支结束设置")]
    [Tooltip("所有步骤完成后向公屏喊出的命令。留空则不发送。")]
    public string branchCompletedCommand = "";

    [Tooltip("分支执行失败后向公屏喊出的命令。留空则不发送。")]
    public string branchFailedCommand = "";

    [Tooltip("全部步骤完成后是否自动返回菜单。")]
    public bool autoReturnToMenuAfterComplete = false;

    [Tooltip("分支失败后是否自动返回菜单。")]
    public bool autoReturnToMenuAfterFailure = false;

    [Header("分支事件")]
    [Tooltip("进入本分支时执行。")]
    public UnityEvent onBranchEntered = new UnityEvent();

    [Tooltip("离开本分支、返回菜单时执行。")]
    public UnityEvent onBranchExited = new UnityEvent();

    [Tooltip("本分支所有步骤完成时执行。")]
    public UnityEvent onBranchCompleted = new UnityEvent();

    [Tooltip("本分支执行失败时执行。")]
    public UnityEvent onBranchFailed = new UnityEvent();
}

#endregion

/// <summary>
/// 菜单 + 分支式通用顺序总控制器。
///
/// 本脚本可以直接读取 CentralInputReceiver。
/// 是否读取输入由公屏命令控制。
/// </summary>
[DefaultExecutionOrder(-700)]
public class MenuBranchSequenceController : MonoBehaviour
{
    public static MenuBranchSequenceController Instance
    {
        get;
        private set;
    }

    public enum ControllerState
    {
        WaitingForStart,
        Menu,
        RunningBranch,
        BranchCompleted,
        BranchFailed,
        Ended
    }

    // =========================================================
    // Inspector 设置
    // =========================================================

    [Header("总控启动与结束")]
    [Tooltip("公屏出现这句话时，总控开始运行并进入菜单。")]
    public string startCommand = "菜单分支总控开启";

    [Tooltip("玩家最终退出后，向公屏回复这句话。")]
    public string completeCommand = "菜单分支总控结束";

    [Header("菜单输入设置")]
    [Tooltip("菜单状态下，收到这个输入标识时结束整个总控。")]
    public string exitInputId = "B";

    [Header("中央输入接收开关")]
    [Tooltip(
        "公屏出现这句话时，本脚本开始读取 CentralInputReceiver。\n" +
        "可与总控启动命令填写成同一句，实现一次叫号同时启动总控和输入。"
    )]
    public string startReceivingInputCommand =
        "输入给菜单分支总控";

    [Tooltip("公屏出现这句话时，本脚本停止读取 CentralInputReceiver。")]
    public string stopReceivingInputCommand =
        "停止输入给菜单分支总控";

    [Tooltip(
        "总控启动时是否自动开始接收输入。\n" +
        "关闭时，必须等待公屏的开始接收输入命令。"
    )]
    public bool autoStartReceivingInputWhenControllerStarts =
        false;

    [Tooltip("整个总控结束时是否自动停止接收输入。")]
    public bool autoStopReceivingInputWhenControllerEnds =
        true;

    [Header("中央输入按钮映射")]
    [Tooltip("手柄 A / 键盘 Enter 被转换成的菜单输入标识。")]
    public string buttonSouthInputId = "A";

    [Tooltip("手柄 B / 键盘 Esc 被转换成的菜单输入标识。")]
    public string buttonEastInputId = "B";

    [Tooltip("手柄 X / 键盘 X 被转换成的菜单输入标识。")]
    public string buttonWestInputId = "X";

    [Tooltip("手柄 Y / 键盘 Y 被转换成的菜单输入标识。")]
    public string buttonNorthInputId = "Y";

    [Header("输入接收事件")]
    [Tooltip("开始接收中央输入时执行一次。")]
    public UnityEvent onInputReceivingStarted =
        new UnityEvent();

    [Tooltip("停止接收中央输入时执行一次。")]
    public UnityEvent onInputReceivingStopped =
        new UnityEvent();

    [Header("总控级公屏命令")]
    [Tooltip("公屏出现这句话时，从当前分支返回菜单。")]
    public string returnToMenuCommand = "返回菜单";

    [Tooltip("公屏出现这句话时，尝试结束整个总控。")]
    public string exitControllerCommand =
        "退出菜单分支总控";

    [Header("分支列表")]
    [Tooltip("Size 就是菜单中的分支数量。")]
    public List<MenuBranchConfig> branches =
        new List<MenuBranchConfig>();

    [Header("返回规则")]
    [Tooltip("勾选后，分支步骤尚未执行完成时也允许返回菜单。")]
    public bool allowReturnWhileBranchRunning = true;

    [Tooltip("分支完成或失败后是否允许返回菜单。")]
    public bool allowReturnAfterBranchFinished = true;

    [Header("重复运行")]
    [Tooltip("整个总控结束后，是否允许再次收到启动命令并重新运行。")]
    public bool allowRestartAfterEnd = true;

    [Header("总控事件")]
    public UnityEvent onControllerStarted =
        new UnityEvent();

    public UnityEvent onMenuEntered =
        new UnityEvent();

    public UnityEvent onMenuExited =
        new UnityEvent();

    public UnityEvent onControllerEnded =
        new UnityEvent();

    public MenuBranchStringEvent onBranchEntered =
        new MenuBranchStringEvent();

    public MenuBranchStringEvent onStepStarted =
        new MenuBranchStringEvent();

    public MenuBranchStringEvent onStepCompleted =
        new MenuBranchStringEvent();

    [Header("调试")]
    public bool printLogs = true;

    // =========================================================
    // 运行时状态
    // =========================================================

    [Header("运行时状态")]
    [SerializeField]
    private ControllerState currentState =
        ControllerState.WaitingForStart;

    [SerializeField]
    private bool subscribedToPublicCommand = false;

    [SerializeField]
    private bool isRunning = false;

    [SerializeField]
    private bool hasEnded = false;

    [SerializeField]
    private bool isReceivingCentralInput = false;

    [SerializeField]
    private string lastCentralInputId = "";

    [SerializeField]
    private bool warnedMissingCentralInputReceiver = false;

    [SerializeField]
    private int currentBranchIndex = -1;

    [SerializeField]
    private int currentStepIndex = -1;

    [SerializeField]
    private string currentBranchName = "等待启动";

    [SerializeField]
    private string currentStepName = "";

    [SerializeField]
    private string waitingForCommand = "";

    [SerializeField]
    private string lastMenuInputId = "";

    [SerializeField]
    private string lastPublicCommand = "";

    [SerializeField]
    private float currentStepElapsed = 0f;

    [SerializeField]
    private float currentStepWaitTarget = 0f;

    private bool pendingStartCurrentStep = false;
    private bool pendingReturnToMenu = false;

    // =========================================================
    // 对外只读属性
    // =========================================================

    public bool IsRunning
    {
        get { return isRunning; }
    }

    public bool IsInMenu
    {
        get { return currentState == ControllerState.Menu; }
    }

    public bool IsReceivingCentralInput
    {
        get { return isReceivingCentralInput; }
    }

    public ControllerState CurrentState
    {
        get { return currentState; }
    }

    public string CurrentBranchName
    {
        get { return currentBranchName; }
    }

    public string CurrentStepName
    {
        get { return currentStepName; }
    }

    public int CurrentBranchIndex
    {
        get { return currentBranchIndex; }
    }

    public int CurrentStepIndex
    {
        get { return currentStepIndex; }
    }

    // =========================================================
    // Unity 生命周期
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 场景中存在多个菜单分支总控，已销毁重复对象。",
                this
            );

            Destroy(gameObject);
            return;
        }

        Instance = this;

        ResetAllRuntimeStepStates();
    }

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
                "[MenuBranchSequenceController] 等待公屏启动命令：" +
                startCommand,
                this
            );
        }
    }

    private void Update()
    {
        if (!subscribedToPublicCommand)
        {
            TrySubscribeToPublicCommand();
        }

        if (pendingStartCurrentStep)
        {
            pendingStartCurrentStep = false;
            StartCurrentStep();
        }

        if (pendingReturnToMenu)
        {
            pendingReturnToMenu = false;
            ReturnToMenu();
        }

        ReadCentralInput();
        UpdateCurrentStep();
    }

    private void OnDisable()
    {
        UnsubscribeFromPublicCommand();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Reset()
    {
        branches = new List<MenuBranchConfig>
        {
            new MenuBranchConfig
            {
                branchName = "分支1",
                inputId = "A"
            },

            new MenuBranchConfig
            {
                branchName = "分支2",
                inputId = "X"
            },

            new MenuBranchConfig
            {
                branchName = "分支3",
                inputId = "Y"
            }
        };

        exitInputId = "B";
    }

    // =========================================================
    // 公屏通信
    // =========================================================

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
                "[MenuBranchSequenceController] 已连接公屏，等待：" +
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

    private void ReceivePublicCommand(string command)
    {
        lastPublicCommand = command;

        // 开始接收输入命令。
        if (!string.IsNullOrWhiteSpace(
                startReceivingInputCommand
            ) &&
            command == startReceivingInputCommand)
        {
            StartReceivingCentralInput();
        }

        // 停止接收输入命令。
        if (!string.IsNullOrWhiteSpace(
                stopReceivingInputCommand
            ) &&
            command == stopReceivingInputCommand)
        {
            StopReceivingCentralInput();
        }

        // 尚未运行时，只识别总控启动命令。
        if (!isRunning)
        {
            if (command == startCommand)
            {
                StartController();
            }

            return;
        }

        if (command == returnToMenuCommand)
        {
            ReturnToMenu();
            return;
        }

        if (command == exitControllerCommand)
        {
            EndController();
            return;
        }

        if (currentState == ControllerState.RunningBranch)
        {
            HandleCurrentStepPublicCommand(command);
        }
    }

    public void ReceiveSignal(string command)
    {
        ReceivePublicCommand(command);
    }

    private void SendPublicCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (公屏叫号.Instance == null)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 找不到公屏，无法发送：" +
                command,
                this
            );

            return;
        }

        公屏叫号.Instance.喊话(command);
    }

    // =========================================================
    // 中央输入接收
    // =========================================================

    /// <summary>
    /// 开始读取 CentralInputReceiver。
    /// </summary>
    public void StartReceivingCentralInput()
    {
        SetCentralInputReceiving(true);
    }

    /// <summary>
    /// 停止读取 CentralInputReceiver。
    /// </summary>
    public void StopReceivingCentralInput()
    {
        SetCentralInputReceiving(false);
    }

    /// <summary>
    /// 外部代码也可以直接设置是否接收输入。
    /// </summary>
    public void SetCentralInputReceiving(bool shouldReceive)
    {
        if (isReceivingCentralInput == shouldReceive)
        {
            return;
        }

        isReceivingCentralInput = shouldReceive;
        warnedMissingCentralInputReceiver = false;

        if (printLogs)
        {
            Debug.Log(
                isReceivingCentralInput
                    ? "[MenuBranchSequenceController] 开始接收中央输入。"
                    : "[MenuBranchSequenceController] 停止接收中央输入。",
                this
            );
        }

        if (isReceivingCentralInput)
        {
            onInputReceivingStarted.Invoke();
        }
        else
        {
            onInputReceivingStopped.Invoke();
        }
    }

    /// <summary>
    /// 每帧读取中央输入。
    ///
    /// 只有以下条件全部满足才会处理：
    /// 1. 已开启输入接收；
    /// 2. 总控正在运行；
    /// 3. 当前处于菜单。
    /// </summary>
    private void ReadCentralInput()
    {
        if (!isReceivingCentralInput ||
            !isRunning ||
            currentState != ControllerState.Menu)
        {
            return;
        }

        CentralInputReceiver input =
            CentralInputReceiver.Instance;

        if (input == null)
        {
            if (!warnedMissingCentralInputReceiver)
            {
                Debug.LogWarning(
                    "[MenuBranchSequenceController] 已允许接收输入，但场景中找不到 CentralInputReceiver。",
                    this
                );

                warnedMissingCentralInputReceiver = true;
            }

            return;
        }

        warnedMissingCentralInputReceiver = false;

        // 同一帧多个按钮一起按下时，只处理一个。
        // 优先级：A → B → X → Y。

        if (input.ButtonSouthPressedThisFrame)
        {
            SubmitCentralInputId(
                buttonSouthInputId
            );

            return;
        }

        if (input.ButtonEastPressedThisFrame)
        {
            SubmitCentralInputId(
                buttonEastInputId
            );

            return;
        }

        if (input.ButtonWestPressedThisFrame)
        {
            SubmitCentralInputId(
                buttonWestInputId
            );

            return;
        }

        if (input.ButtonNorthPressedThisFrame)
        {
            SubmitCentralInputId(
                buttonNorthInputId
            );
        }
    }

    private void SubmitCentralInputId(string inputId)
    {
        if (string.IsNullOrWhiteSpace(inputId))
        {
            return;
        }

        lastCentralInputId = inputId.Trim();

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 收到中央输入：" +
                lastCentralInputId,
                this
            );
        }

        SubmitMenuInput(lastCentralInputId);
    }

    // =========================================================
    // 总控启动与菜单输入
    // =========================================================

    [ContextMenu("Start Controller")]
    public void StartController()
    {
        if (isRunning)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[MenuBranchSequenceController] 总控正在运行，忽略重复启动。",
                    this
                );
            }

            return;
        }

        if (hasEnded && !allowRestartAfterEnd)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 总控已经结束，当前禁止重新启动。",
                this
            );

            return;
        }

        ResetAllRuntimeStepStates();
        ClearPendingOperations();

        isRunning = true;
        hasEnded = false;

        currentBranchIndex = -1;
        currentStepIndex = -1;

        currentBranchName = "菜单";
        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        currentState = ControllerState.Menu;

        if (autoStartReceivingInputWhenControllerStarts)
        {
            StartReceivingCentralInput();
        }

        onControllerStarted.Invoke();
        onMenuEntered.Invoke();

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 总控开启，进入菜单。",
                this
            );
        }
    }

    public void SubmitMenuInput(string inputId)
    {
        lastMenuInputId = inputId;

        if (!isRunning)
        {
            return;
        }

        if (currentState != ControllerState.Menu)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[MenuBranchSequenceController] 当前不在菜单，忽略菜单输入：" +
                    inputId,
                    this
                );
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(inputId))
        {
            return;
        }

        string cleanedInputId =
            inputId.Trim();

        if (cleanedInputId == exitInputId)
        {
            EndController();
            return;
        }

        int targetBranchIndex =
            FindBranchIndexByInputId(
                cleanedInputId
            );

        if (targetBranchIndex < 0)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[MenuBranchSequenceController] 没有分支绑定输入：" +
                    cleanedInputId,
                    this
                );
            }

            return;
        }

        EnterBranchByIndex(
            targetBranchIndex
        );
    }

    // =========================================================
    // 进入分支
    // =========================================================

    public void EnterBranchByIndex(int branchIndex)
    {
        if (!isRunning)
        {
            return;
        }

        if (currentState != ControllerState.Menu)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 当前不在菜单，不能进入新分支。",
                this
            );

            return;
        }

        if (!IsBranchIndexValid(branchIndex))
        {
            Debug.LogError(
                "[MenuBranchSequenceController] 分支下标无效：" +
                branchIndex,
                this
            );

            return;
        }

        MenuBranchConfig branch =
            branches[branchIndex];

        if (branch == null)
        {
            Debug.LogError(
                "[MenuBranchSequenceController] 目标分支配置为空：" +
                branchIndex,
                this
            );

            return;
        }

        onMenuExited.Invoke();

        currentBranchIndex = branchIndex;
        currentStepIndex = 0;

        currentBranchName = branch.branchName;
        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        currentState =
            ControllerState.RunningBranch;

        ResetBranchRuntimeStepStates(branch);
        ClearPendingOperations();

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 进入分支：" +
                branch.branchName +
                "；步骤数量：" +
                (
                    branch.steps == null
                        ? 0
                        : branch.steps.Count
                ),
                this
            );
        }

        branch.onBranchEntered.Invoke();
        onBranchEntered.Invoke(
            branch.branchName
        );

        pendingStartCurrentStep = true;
    }

    public void EnterBranchByName(string branchName)
    {
        int branchIndex =
            FindBranchIndexByName(
                branchName
            );

        if (branchIndex < 0)
        {
            Debug.LogError(
                "[MenuBranchSequenceController] 找不到分支：" +
                branchName,
                this
            );

            return;
        }

        EnterBranchByIndex(branchIndex);
    }

    // =========================================================
    // 开放式步骤执行
    // =========================================================

    private void StartCurrentStep()
    {
        if (!isRunning ||
            currentState !=
            ControllerState.RunningBranch)
        {
            return;
        }

        MenuBranchConfig branch =
            GetCurrentBranch();

        if (branch == null)
        {
            FailCurrentBranch(
                "找不到当前分支。"
            );

            return;
        }

        if (branch.steps == null ||
            currentStepIndex >=
            branch.steps.Count)
        {
            CompleteCurrentBranch();
            return;
        }

        if (currentStepIndex < 0)
        {
            currentStepIndex = 0;
        }

        MenuBranchStep step =
            branch.steps[currentStepIndex];

        if (step == null)
        {
            currentStepIndex++;
            pendingStartCurrentStep = true;
            return;
        }

        if (!step.enabled)
        {
            step.runtimeState =
                MenuBranchStepRuntimeState.Skipped;

            if (printLogs)
            {
                Debug.Log(
                    "[MenuBranchSequenceController] 跳过步骤：" +
                    step.stepName,
                    this
                );
            }

            currentStepIndex++;
            pendingStartCurrentStep = true;
            return;
        }

        step.runtimeState =
            MenuBranchStepRuntimeState.Running;

        currentStepName = step.stepName;
        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        waitingForCommand =
            step.completeMode ==
            MenuBranchStepCompleteMode.WaitPublicCommand
                ? step.completeCommand
                : "";

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 分支“" +
                branch.branchName +
                "”启动 Step " +
                (currentStepIndex + 1) +
                "：" +
                step.stepName +
                "；完成方式：" +
                step.completeMode +
                "；开始命令：" +
                step.startCommand +
                "；等待命令：" +
                waitingForCommand,
                this
            );
        }

        step.onStepStarted.Invoke();
        onStepStarted.Invoke(
            step.stepName
        );

        if (!IsThisStillCurrentRunningStep(step))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                step.startCommand
            ))
        {
            SendPublicCommand(
                step.startCommand
            );
        }

        if (!IsThisStillCurrentRunningStep(step))
        {
            return;
        }

        switch (step.completeMode)
        {
            case MenuBranchStepCompleteMode.Immediate:
                CompleteCurrentStep();
                break;

            case MenuBranchStepCompleteMode.WaitPublicCommand:
                if (string.IsNullOrWhiteSpace(
                        step.completeCommand
                    ))
                {
                    FailCurrentStep(
                        "步骤“" +
                        step.stepName +
                        "”选择了 WaitPublicCommand，" +
                        "但没有填写 Complete Command。"
                    );
                }
                break;

            case MenuBranchStepCompleteMode.WaitSeconds:
                currentStepWaitTarget =
                    Mathf.Max(
                        0f,
                        step.waitSeconds
                    );

                if (currentStepWaitTarget <= 0f)
                {
                    CompleteCurrentStep();
                }
                break;

            case MenuBranchStepCompleteMode.Manual:
                break;
        }
    }

    private void UpdateCurrentStep()
    {
        if (!isRunning ||
            currentState !=
            ControllerState.RunningBranch)
        {
            return;
        }

        MenuBranchStep step =
            GetCurrentStep();

        if (step == null ||
            step.runtimeState !=
            MenuBranchStepRuntimeState.Running)
        {
            return;
        }

        currentStepElapsed +=
            Time.deltaTime;

        switch (step.completeMode)
        {
            case MenuBranchStepCompleteMode.WaitSeconds:
                if (currentStepElapsed >=
                    currentStepWaitTarget)
                {
                    CompleteCurrentStep();
                }
                break;

            case MenuBranchStepCompleteMode.WaitPublicCommand:
            case MenuBranchStepCompleteMode.Manual:
                if (step.timeoutSeconds > 0f &&
                    currentStepElapsed >=
                    step.timeoutSeconds)
                {
                    FailCurrentStep(
                        "步骤超时：" +
                        step.stepName
                    );
                }
                break;
        }
    }

    private void HandleCurrentStepPublicCommand(
        string command
    )
    {
        MenuBranchStep step =
            GetCurrentStep();

        if (step == null ||
            step.runtimeState !=
            MenuBranchStepRuntimeState.Running)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(
                step.failedCommand
            ) &&
            command == step.failedCommand)
        {
            FailCurrentStep(
                "步骤收到失败命令：" +
                command
            );

            return;
        }

        if (step.completeMode !=
            MenuBranchStepCompleteMode.WaitPublicCommand)
        {
            return;
        }

        if (command != step.completeCommand)
        {
            return;
        }

        CompleteCurrentStep();
    }

    public void CompleteCurrentStepManually()
    {
        if (!isRunning ||
            currentState !=
            ControllerState.RunningBranch)
        {
            return;
        }

        MenuBranchStep step =
            GetCurrentStep();

        if (step == null)
        {
            return;
        }

        if (step.completeMode !=
            MenuBranchStepCompleteMode.Manual)
        {
            if (printLogs)
            {
                Debug.LogWarning(
                    "[MenuBranchSequenceController] 当前步骤不是 Manual 类型，忽略手动完成请求。",
                    this
                );
            }

            return;
        }

        CompleteCurrentStep();
    }

    public void FailCurrentStepManually(
        string reason
    )
    {
        if (!isRunning ||
            currentState !=
            ControllerState.RunningBranch)
        {
            return;
        }

        FailCurrentStep(
            string.IsNullOrWhiteSpace(reason)
                ? "外部脚本报告当前步骤失败。"
                : reason
        );
    }

    public void FailCurrentStepManually()
    {
        FailCurrentStepManually(
            "外部脚本报告当前步骤失败。"
        );
    }

    private void CompleteCurrentStep()
    {
        MenuBranchStep step =
            GetCurrentStep();

        if (step == null ||
            step.runtimeState !=
            MenuBranchStepRuntimeState.Running)
        {
            return;
        }

        int branchIndexBefore =
            currentBranchIndex;

        int stepIndexBefore =
            currentStepIndex;

        step.runtimeState =
            MenuBranchStepRuntimeState.Complete;

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 步骤完成：" +
                step.stepName,
                this
            );
        }

        step.onStepCompleted.Invoke();
        onStepCompleted.Invoke(
            step.stepName
        );

        if (!isRunning ||
            currentState !=
            ControllerState.RunningBranch ||
            currentBranchIndex !=
            branchIndexBefore ||
            currentStepIndex !=
            stepIndexBefore ||
            GetCurrentStep() != step)
        {
            return;
        }

        currentStepIndex++;

        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        pendingStartCurrentStep = true;
    }

    private void FailCurrentStep(
        string reason
    )
    {
        MenuBranchStep step =
            GetCurrentStep();

        if (step != null &&
            step.runtimeState ==
            MenuBranchStepRuntimeState.Running)
        {
            int branchIndexBefore =
                currentBranchIndex;

            int stepIndexBefore =
                currentStepIndex;

            step.runtimeState =
                MenuBranchStepRuntimeState.Failed;

            step.onStepFailed.Invoke();

            if (!isRunning ||
                currentState !=
                ControllerState.RunningBranch ||
                currentBranchIndex !=
                branchIndexBefore ||
                currentStepIndex !=
                stepIndexBefore)
            {
                return;
            }
        }

        FailCurrentBranch(reason);
    }

    private bool IsThisStillCurrentRunningStep(
        MenuBranchStep step
    )
    {
        return isRunning &&
               currentState ==
               ControllerState.RunningBranch &&
               GetCurrentStep() == step &&
               step.runtimeState ==
               MenuBranchStepRuntimeState.Running;
    }

    // =========================================================
    // 分支完成与失败
    // =========================================================

    private void CompleteCurrentBranch()
    {
        MenuBranchConfig branch =
            GetCurrentBranch();

        if (branch == null)
        {
            return;
        }

        pendingStartCurrentStep = false;

        currentState =
            ControllerState.BranchCompleted;

        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 分支全部步骤完成：" +
                branch.branchName,
                this
            );
        }

        branch.onBranchCompleted.Invoke();

        if (!string.IsNullOrWhiteSpace(
                branch.branchCompletedCommand
            ))
        {
            SendPublicCommand(
                branch.branchCompletedCommand
            );
        }

        if (branch.autoReturnToMenuAfterComplete &&
            currentState ==
            ControllerState.BranchCompleted)
        {
            pendingReturnToMenu = true;
        }
    }

    private void FailCurrentBranch(
        string reason
    )
    {
        MenuBranchConfig branch =
            GetCurrentBranch();

        pendingStartCurrentStep = false;

        currentState =
            ControllerState.BranchFailed;

        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        Debug.LogError(
            "[MenuBranchSequenceController] 分支执行失败：" +
            reason,
            this
        );

        if (branch == null)
        {
            return;
        }

        branch.onBranchFailed.Invoke();

        if (!string.IsNullOrWhiteSpace(
                branch.branchFailedCommand
            ))
        {
            SendPublicCommand(
                branch.branchFailedCommand
            );
        }

        if (branch.autoReturnToMenuAfterFailure &&
            currentState ==
            ControllerState.BranchFailed)
        {
            pendingReturnToMenu = true;
        }
    }

    // =========================================================
    // 返回菜单与结束
    // =========================================================

    [ContextMenu("Return To Menu")]
    public void ReturnToMenu()
    {
        if (!isRunning)
        {
            return;
        }

        if (currentState ==
            ControllerState.Menu)
        {
            return;
        }

        if (currentState ==
            ControllerState.RunningBranch &&
            !allowReturnWhileBranchRunning)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 当前分支仍在执行，不允许返回菜单。",
                this
            );

            return;
        }

        if ((currentState ==
                 ControllerState.BranchCompleted ||
             currentState ==
                 ControllerState.BranchFailed) &&
            !allowReturnAfterBranchFinished)
        {
            return;
        }

        MenuBranchConfig branch =
            GetCurrentBranch();

        ClearPendingOperations();

        if (branch != null)
        {
            if (printLogs)
            {
                Debug.Log(
                    "[MenuBranchSequenceController] 离开分支：" +
                    branch.branchName,
                    this
                );
            }

            branch.onBranchExited.Invoke();
        }

        currentBranchIndex = -1;
        currentStepIndex = -1;

        currentBranchName = "菜单";
        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        currentState =
            ControllerState.Menu;

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 返回菜单。",
                this
            );
        }

        onMenuEntered.Invoke();
    }

    [ContextMenu("End Controller")]
    public void EndController()
    {
        if (!isRunning)
        {
            return;
        }

        if (currentState !=
            ControllerState.Menu)
        {
            Debug.LogWarning(
                "[MenuBranchSequenceController] 当前不在菜单，不能结束总控。请先返回菜单。",
                this
            );

            return;
        }

        ClearPendingOperations();

        isRunning = false;
        hasEnded = true;

        currentState =
            ControllerState.Ended;

        currentBranchIndex = -1;
        currentStepIndex = -1;

        currentBranchName = "已结束";
        currentStepName = "";
        waitingForCommand = "";

        if (autoStopReceivingInputWhenControllerEnds)
        {
            StopReceivingCentralInput();
        }

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 总控结束，向公屏回复：" +
                completeCommand,
                this
            );
        }

        onControllerEnded.Invoke();

        SendPublicCommand(
            completeCommand
        );
    }

    // =========================================================
    // 查询与辅助
    // =========================================================

    private MenuBranchConfig GetCurrentBranch()
    {
        if (!IsBranchIndexValid(
                currentBranchIndex
            ))
        {
            return null;
        }

        return branches[
            currentBranchIndex
        ];
    }

    private MenuBranchStep GetCurrentStep()
    {
        MenuBranchConfig branch =
            GetCurrentBranch();

        if (branch == null ||
            branch.steps == null ||
            currentStepIndex < 0 ||
            currentStepIndex >=
            branch.steps.Count)
        {
            return null;
        }

        return branch.steps[
            currentStepIndex
        ];
    }

    private int FindBranchIndexByInputId(
        string inputId
    )
    {
        if (branches == null)
        {
            return -1;
        }

        for (int i = 0;
             i < branches.Count;
             i++)
        {
            MenuBranchConfig branch =
                branches[i];

            if (branch == null)
            {
                continue;
            }

            if (branch.inputId ==
                inputId)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindBranchIndexByName(
        string branchName
    )
    {
        if (branches == null)
        {
            return -1;
        }

        for (int i = 0;
             i < branches.Count;
             i++)
        {
            MenuBranchConfig branch =
                branches[i];

            if (branch == null)
            {
                continue;
            }

            if (branch.branchName ==
                branchName)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsBranchIndexValid(
        int index
    )
    {
        return branches != null &&
               index >= 0 &&
               index < branches.Count;
    }

    private void ResetAllRuntimeStepStates()
    {
        if (branches == null)
        {
            return;
        }

        for (int i = 0;
             i < branches.Count;
             i++)
        {
            ResetBranchRuntimeStepStates(
                branches[i]
            );
        }
    }

    private void ResetBranchRuntimeStepStates(
        MenuBranchConfig branch
    )
    {
        if (branch == null ||
            branch.steps == null)
        {
            return;
        }

        for (int i = 0;
             i < branch.steps.Count;
             i++)
        {
            MenuBranchStep step =
                branch.steps[i];

            if (step == null)
            {
                continue;
            }

            step.runtimeState =
                MenuBranchStepRuntimeState.Waiting;
        }
    }

    private void ClearPendingOperations()
    {
        pendingStartCurrentStep = false;
        pendingReturnToMenu = false;
    }

    // =========================================================
    // 重置
    // =========================================================

    [ContextMenu("Reset Controller")]
    public void ResetController()
    {
        ClearPendingOperations();

        isRunning = false;
        hasEnded = false;

        currentState =
            ControllerState.WaitingForStart;

        currentBranchIndex = -1;
        currentStepIndex = -1;

        currentBranchName = "等待启动";
        currentStepName = "";
        waitingForCommand = "";

        currentStepElapsed = 0f;
        currentStepWaitTarget = 0f;

        lastMenuInputId = "";
        lastCentralInputId = "";

        warnedMissingCentralInputReceiver =
            false;

        StopReceivingCentralInput();

        ResetAllRuntimeStepStates();

        if (printLogs)
        {
            Debug.Log(
                "[MenuBranchSequenceController] 已重置，等待公屏启动命令：" +
                startCommand,
                this
            );
        }
    }
}