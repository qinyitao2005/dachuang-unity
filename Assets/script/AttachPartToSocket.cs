using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 挂载指针 / 笔 / 插头到指定父物体下面。
///
/// 总控适配版：
/// 1. SequenceSignalController 调用 Attach()
/// 2. 本脚本发出：指令-挂载指针开始执行
/// 3. 修改 partRoot 的父子关系
/// 4. 成功后发出：指令-挂载指针已经完成
/// 5. 失败后发出：指令-挂载指针执行失败
///
/// 注意：
/// 这个脚本只负责挂载。
/// 不负责对接 IK。
/// 不负责启动笔运动 IK。
/// 后续流程全部交给 SequenceSignalController。
/// </summary>
public class AttachPartToSocket : MonoBehaviour
{
    [Serializable]
    public class AttachCommandEvent : UnityEvent<string>
    {
    }

    [Header("要被挂载的物体，例如指针/插头/笔的整体根物体")]
    public Transform partRoot;

    [Header("接上之后，要挂到哪个父物体下面，例如 GripSocket / HandKey / TCP")]
    public Transform targetParent;

    [Header("挂载时是否保持世界坐标不变")]
    [Tooltip("建议第一版勾上。这样修改父子关系时，物体不会突然跳位置。")]
    public bool keepWorldPoseWhenAttach = true;

    [Header("是否在挂载后强制套用局部偏移")]
    [Tooltip("第一版建议不勾。只有你想让 partRoot 精确吸到 targetParent 的某个局部位置时再勾。")]
    public bool applyLocalOffsetAfterAttach = false;

    [Header("挂载后的局部位置偏移")]
    public Vector3 localPositionOffset = Vector3.zero;

    [Header("挂载后的局部旋转偏移")]
    public Vector3 localEulerOffset = Vector3.zero;

    [Header("测试按键")]
    [Tooltip("只用于调试。正式流程建议由 SequenceSignalController 调用 Attach()。")]
    public bool enableTestKey = false;

    public KeyCode testAttachKey = KeyCode.G;

    [Header("声音，可后期再填")]
    public AudioSource audioSource;
    public AudioClip attachSound;

    [Header("信号命名")]
    [Tooltip("只是方便你在 Inspector 里看这个步骤叫什么。")]
    public string stepDisplayName = "挂载指针";

    [Tooltip("开始执行时输出到 Console 的指令。")]
    public string startCommand = "指令-挂载指针开始执行";

    [Tooltip("完成时输出到 Console 的指令。总控等待的就是这个。")]
    public string completeCommand = "指令-挂载指针已经完成";

    [Tooltip("失败时输出到 Console 的指令。")]
    public string failedCommand = "指令-挂载指针执行失败";

    [Tooltip("开始执行时触发。一般不用接给总控，只用于调试或显示。")]
    public AttachCommandEvent onStarted;

    [Tooltip("完成时触发。这个要接给 SequenceSignalController.ReceiveSignal(string)。")]
    public AttachCommandEvent onCompleted;

    [Tooltip("失败时触发。可选。")]
    public AttachCommandEvent onFailed;

    [Header("运行时显示")]
    [SerializeField] private bool started = false;
    [SerializeField] private bool attached = false;
    [SerializeField] private bool completed = false;
    [SerializeField] private bool failed = false;

    private Transform originalParent;

    private bool startSignalAlreadySent = false;
    private bool completeSignalAlreadySent = false;
    private bool failedSignalAlreadySent = false;

    private void Awake()
    {
        if (partRoot != null)
        {
            originalParent = partRoot.parent;
        }
    }

    private void Update()
    {
        if (!enableTestKey)
        {
            return;
        }

        if (Input.GetKeyDown(testAttachKey))
        {
            Attach();
        }
    }

    [ContextMenu("Attach")]
    public void Attach()
    {
        ResetSignalRuntimeState();

        started = true;
        completed = false;
        failed = false;

        SendStartSignal();

        if (partRoot == null || targetParent == null)
        {
            Debug.LogWarning("[AttachPartToSocket] Attach failed：partRoot 或 targetParent 没填。");
            FailAttach();
            return;
        }

        // 如果已经挂载过，不要让总控卡死。
        // 这里直接视为本步骤完成。
        if (attached)
        {
            Debug.Log("[AttachPartToSocket] 已经挂载过了，直接发送完成信号。");
            completed = true;
            SendCompleteSignal();
            return;
        }

        // 核心：真正修改父子关系。
        // keepWorldPoseWhenAttach = true 时，挂过去后世界位置不跳。
        partRoot.SetParent(targetParent, keepWorldPoseWhenAttach);

        // 第一版通常不需要开这个。
        // 只有你想让物体挂上去后立刻吸到某个局部坐标，才勾 applyLocalOffsetAfterAttach。
        if (applyLocalOffsetAfterAttach)
        {
            partRoot.localPosition = localPositionOffset;
            partRoot.localRotation = Quaternion.Euler(localEulerOffset);
        }

        attached = true;
        completed = true;
        failed = false;

        if (audioSource != null && attachSound != null)
        {
            audioSource.PlayOneShot(attachSound);
        }

        Debug.Log("[AttachPartToSocket] Attach success：" + partRoot.name + " 已挂到 " + targetParent.name + " 下面。");

        SendCompleteSignal();
    }

    /// <summary>
    /// 备用启动接口。
    /// 如果以后总控要调用 StartFromCommand(string)，也能用。
    /// </summary>
    public void StartFromCommand(string command)
    {
        Debug.Log("[AttachPartToSocket] 收到开始指令：" + command);
        Attach();
    }

    [ContextMenu("Detach To Original Parent")]
    public void DetachToOriginalParent()
    {
        if (!attached)
        {
            Debug.Log("[AttachPartToSocket] 当前没有挂载，不需要 Detach。");
            return;
        }

        if (partRoot == null)
        {
            Debug.LogWarning("[AttachPartToSocket] Detach failed：partRoot 没填。");
            return;
        }

        partRoot.SetParent(originalParent, true);

        attached = false;
        completed = false;
        failed = false;

        Debug.Log("[AttachPartToSocket] Detach success：已恢复到原父物体。");
    }

    [ContextMenu("Reset Runtime State")]
    public void ResetRuntimeState()
    {
        started = false;
        completed = false;
        failed = false;

        startSignalAlreadySent = false;
        completeSignalAlreadySent = false;
        failedSignalAlreadySent = false;

        // 注意：这里不把 attached 改成 false。
        // 因为 attached 表示真实父子关系状态，不应该被纯运行状态重置乱改。
    }

    private void ResetSignalRuntimeState()
    {
        startSignalAlreadySent = false;
        completeSignalAlreadySent = false;
        failedSignalAlreadySent = false;
    }

    private void FailAttach()
    {
        failed = true;
        completed = false;

        SendFailedSignal();
    }

    private void SendStartSignal()
    {
        if (startSignalAlreadySent)
        {
            return;
        }

        startSignalAlreadySent = true;

        Debug.Log(startCommand);

        if (onStarted != null)
        {
            onStarted.Invoke(startCommand);
        }
    }

    private void SendCompleteSignal()
    {
        if (completeSignalAlreadySent)
        {
            return;
        }

        completeSignalAlreadySent = true;

        Debug.Log(completeCommand);

        if (onCompleted != null)
        {
            onCompleted.Invoke(completeCommand);
        }
    }

    private void SendFailedSignal()
    {
        if (failedSignalAlreadySent)
        {
            return;
        }

        failedSignalAlreadySent = true;

        Debug.Log(failedCommand);

        if (onFailed != null)
        {
            onFailed.Invoke(failedCommand);
        }
    }

    public bool IsStarted()
    {
        return started;
    }

    public bool IsAttached()
    {
        return attached;
    }

    public bool IsCompleted()
    {
        return completed;
    }

    public bool IsFailed()
    {
        return failed;
    }
}