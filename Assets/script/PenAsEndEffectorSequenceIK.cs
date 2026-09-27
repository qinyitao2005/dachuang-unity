using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 笔作为末端执行器的序列 IK。
///
/// 使用场景：
/// 1. 第一阶段 DirectKeyDockingIK 完成对接。
/// 2. AttachPartToSocket 把笔 / 指针挂到机械臂下面。
/// 3. 从这一刻开始，“笔”就是机械臂末端。
/// 4. 本脚本直接把 controlledPen 当成 IK 的 endEffector。
///
/// 本版修正：
/// - 位置目标：controlledPen 追 desiredPenPosition。
/// - 笔轴约束：controlledPen 的指定本地轴，强制平行 / 垂直于指定世界轴。
/// - 完成后可以 Hold 最后目标姿态。
/// - 新增 ReleaseControl()：允许总控在进入下一步前释放本脚本控制权，防止后续 IK 被本脚本拉回。
///
/// 每段完成输出：
/// 指令-PenAsEndEffectorSequenceIK-段X-段名已经完成
///
/// 全部完成输出：
/// 指令-PenAsEndEffectorSequenceIK已经完成
/// </summary>
public class PenAsEndEffectorSequenceIK : MonoBehaviour
{
    [Serializable]
    public class CommandEvent : UnityEvent<string>
    {
    }

    public enum LocalAxis
    {
        X,
        Y,
        Z
    }

    public enum WorldAxis
    {
        WorldX,
        WorldY,
        WorldZ
    }

    public enum MoveAxisSpace
    {
        World,
        PenLocal
    }

    public enum AxisRelation
    {
        Parallel,
        Perpendicular
    }

    public enum MotionState
    {
        Idle,
        Running,
        Complete
    }

    [Serializable]
    public class IKJoint
    {
        [Header("关节")]
        public string displayName;
        public Transform joint;

        [Header("本关节绕自己的哪根 local 轴转")]
        public LocalAxis axis = LocalAxis.Y;

        [Tooltip("方向反了就勾这个。")]
        public bool invertAxis = false;

        [Header("是否参与 IK")]
        public bool enabled = true;

        [Header("权重")]
        [Tooltip("底座/大臂/小臂建议 1；手腕 0.2~0.5；手指第一版建议 0 或禁用。")]
        [Range(0f, 2f)]
        public float weight = 1f;

        [Header("角度限制，可先不启用")]
        public bool useLimits = false;
        public float minAngle = -180f;
        public float maxAngle = 180f;

        [Header("运行时显示")]
        public float currentAngle = 0f;

        [NonSerialized] public Quaternion initialLocalRotation;

        public void CacheInitialPose()
        {
            if (joint == null) return;

            initialLocalRotation = joint.localRotation;
            currentAngle = 0f;
        }

        public Vector3 GetLocalAxisVector()
        {
            Vector3 a = Vector3.up;

            switch (axis)
            {
                case LocalAxis.X:
                    a = Vector3.right;
                    break;

                case LocalAxis.Y:
                    a = Vector3.up;
                    break;

                case LocalAxis.Z:
                    a = Vector3.forward;
                    break;
            }

            return invertAxis ? -a : a;
        }

        public Vector3 GetWorldAxis()
        {
            if (joint == null) return Vector3.up;

            return joint.TransformDirection(GetLocalAxisVector()).normalized;
        }

        public void ApplyCurrentAngle()
        {
            if (joint == null) return;

            Quaternion delta = Quaternion.AngleAxis(currentAngle, GetLocalAxisVector());
            joint.localRotation = initialLocalRotation * delta;
        }

        public void AddAngle(float deltaAngle, float maxAbsDelta)
        {
            if (joint == null) return;
            if (!enabled) return;

            deltaAngle = Mathf.Clamp(deltaAngle, -maxAbsDelta, maxAbsDelta);

            float next = currentAngle + deltaAngle;

            if (useLimits)
            {
                next = Mathf.Clamp(next, minAngle, maxAngle);
            }

            currentAngle = next;
            ApplyCurrentAngle();
        }
    }

    [Serializable]
    public class MotionSegment
    {
        [Header("段名称，只是方便你看")]
        public string segmentName = "运动段";

        [Header("是否启用这一段")]
        public bool enabled = true;

        [Header("这一段沿哪种坐标轴运动")]
        [Tooltip("World = 沿世界坐标轴；PenLocal = 沿笔当前目标姿态的局部轴。")]
        public MoveAxisSpace moveAxisSpace = MoveAxisSpace.World;

        [Header("这一段沿哪根轴运动")]
        public LocalAxis moveAxis = LocalAxis.Z;

        [Header("正方向还是反方向")]
        public bool positiveDirection = true;

        [Header("运动距离，单位是 Unity 世界单位")]
        public float distance = 0.1f;

        [Header("运动速度，每秒多少 Unity 世界单位")]
        public float speed = 0.03f;

        [Header("本段结束后暂停几秒")]
        public float pauseAfter = 0f;

        [Header("运行时显示")]
        [NonSerialized] public float movedDistance = 0f;

        [NonSerialized] public bool targetMovementFinished = false;
    }

    [Header("核心对象")]
    [Tooltip("已经 Attach 到机械臂上的笔 / 指针。它现在就是 IK 末端。")]
    public Transform controlledPen;

    [Header("笔轴约束")]
    [Tooltip("是否启用笔轴约束。")]
    public bool enableAxisConstraint = true;

    [Tooltip("Parallel = 平行；Perpendicular = 垂直。")]
    public AxisRelation axisRelation = AxisRelation.Parallel;

    [Tooltip("笔自己的哪根本地轴参与约束。比如笔杆长轴是 Y，就选 Y。")]
    public LocalAxis penAxisToConstrain = LocalAxis.Y;

    [Tooltip("如果笔这根轴方向反了，就勾这个。")]
    public bool invertPenAxis = false;

    [Tooltip("要和哪根世界轴保持平行/垂直。通常世界垂直轴是 WorldY。")]
    public WorldAxis worldAxisToConstrain = WorldAxis.WorldY;

    [Tooltip("使用世界轴正方向；方向反了就取消。")]
    public bool usePositiveWorldAxis = true;

    [Header("垂直关系时的方向处理")]
    [Tooltip("当 Axis Relation = Perpendicular 时，用这个方向决定笔轴朝哪边。")]
    public bool usePreferredDirectionWhenPerpendicular = true;

    [Tooltip("笔轴要尽量朝向哪个世界方向。系统会自动投影到垂直平面里。")]
    public WorldAxis preferredPerpendicularWorldDirection = WorldAxis.WorldX;

    [Tooltip("Preferred Direction 使用正方向；方向反了就取消。")]
    public bool preferredPositiveDirection = true;

    [Header("运动段列表")]
    public MotionSegment[] segments;

    [Header("目标别甩开笔太远")]
    [Tooltip("勾上后，如果虚拟目标笔离真实笔太远，就先等机械臂追上。")]
    public bool waitForPenWhenTooFar = true;

    [Tooltip("虚拟目标笔最多领先真实笔多远。")]
    public float maxLeadDistance = 0.03f;

    [Header("每段完成判定")]
    [Tooltip("虚拟目标走完后，真实笔离目标多近，才算这一段完成。")]
    public float segmentFinishPositionTolerance = 0.01f;

    [Tooltip("轴约束误差小于这个角度才算完成。")]
    public float segmentFinishRotationTolerance = 20f;

    [Header("是否追旋转")]
    [Tooltip("建议打开。启用笔轴约束时，会优先用笔轴硬约束。")]
    public bool solveRotation = true;

    [Tooltip("普通 Quaternion 姿态追踪权重。启用笔轴约束时，这个不是主控制量。")]
    [Range(0f, 1f)]
    public float rotationWeight = 0.1f;

    [Header("笔轴硬约束强度")]
    [Tooltip("控制笔轴和世界轴对齐的强度。1~3 常用。太小压不住，太大可能抖。")]
    [Range(0f, 5f)]
    public float axisConstraintWeight = 2f;

    [Header("完成后保持最后姿态")]
    [Tooltip("勾上后，序列完成后仍然继续 IK Hold 最后目标。注意：如果后面还有别的 IK 步骤，总控应调用 ReleaseControl()。")]
    public bool holdFinalPoseAfterComplete = true;

    [Header("控制权释放")]
    [Tooltip("运行时显示：true 表示本脚本已经释放控制权，不再 Hold 最后姿态。")]
    public bool controlReleased = false;

    [Header("IK 关节，从根到末端")]
    public IKJoint[] joints;

    [Header("IK 参数")]
    [Tooltip("每帧分几次小步计算。")]
    public int solverSubsteps = 4;

    [Tooltip("每个关节最大角速度，单位：度/秒。")]
    public float maxJointSpeedDegPerSec = 35f;

    [Tooltip("阻尼，越大越稳，越小越慢。")]
    public float damping = 0.1f;

    [Tooltip("位置误差小于这个值时，本帧 IK 不再继续死磕。")]
    public float ikPositionTolerance = 0.006f;

    [Header("运行")]
    public bool autoStart = false;
    public MotionState state = MotionState.Idle;

    [Header("外部开始信号")]
    [Tooltip("现在由 SequenceSignalController 调 StartSequence 时，这个一般不用管。")]
    public string requiredStartCommand = "指令-AttachPartToSocket已经完成";

    [Header("完成信号")]
    public string completeCommand = "指令-PenAsEndEffectorSequenceIK已经完成";

    [Tooltip("每段完成时发出的事件。")]
    public CommandEvent onSegmentCompleted;

    [Tooltip("全部完成时发出的事件。")]
    public CommandEvent onCompleted;

    [Header("调试")]
    public bool drawDebugGizmos = true;

    [Header("运行时显示")]
    [SerializeField] private int currentSegmentIndex = -1;
    [SerializeField] private bool waitingAfterSegment = false;
    [SerializeField] private float waitTimer = 0f;
    [SerializeField] private bool completed = false;

    [SerializeField] private Vector3 desiredPenPosition;
    [SerializeField] private Quaternion desiredPenRotation = Quaternion.identity;

    private bool desiredPoseValid = false;
    private bool completeSignalAlreadySent = false;

    private void Start()
    {
        CacheCurrentPoseAsInitial();

        if (autoStart)
        {
            StartSequence();
        }
    }

    private void Update()
    {
        if (!CheckReferences()) return;

        if (state == MotionState.Running)
        {
            UpdateRunning();
            return;
        }

        if (state == MotionState.Complete)
        {
            if (!controlReleased && desiredPoseValid && holdFinalPoseAfterComplete)
            {
                SolveIKToDesiredPen();
            }

            return;
        }
    }

    [ContextMenu("Start Sequence")]
    public void StartSequence()
    {
        if (!CheckReferences())
        {
            Debug.LogWarning("[PenAsEndEffectorSequenceIK] 引用没填完整，无法开始。");
            return;
        }

        // 新一轮启动时，重新拿回控制权。
        controlReleased = false;

        // 第二阶段通常接在 Attach 后面。
        // 所以开始时，把当前机械臂姿态缓存为新的初始姿态，避免跳回老状态。
        CacheCurrentPoseAsInitial();

        desiredPenPosition = controlledPen.position;
        desiredPenRotation = controlledPen.rotation;
        desiredPoseValid = true;

        if (enableAxisConstraint)
        {
            ApplyAxisConstraintToDesiredPen();
        }

        ResetRuntimeSegmentData();

        currentSegmentIndex = FindNextEnabledSegment(-1);
        waitingAfterSegment = false;
        waitTimer = 0f;

        completed = false;
        completeSignalAlreadySent = false;

        if (currentSegmentIndex < 0)
        {
            Debug.LogWarning("[PenAsEndEffectorSequenceIK] 没有任何启用的运动段。");
            return;
        }

        state = MotionState.Running;

        Debug.Log("[PenAsEndEffectorSequenceIK] 开始序列运动。当前段：" + GetCurrentSegmentDisplayName());
    }

    public void StartFromCommand(string command)
    {
        if (!string.IsNullOrEmpty(requiredStartCommand) && command != requiredStartCommand)
        {
            Debug.Log("[PenAsEndEffectorSequenceIK] 收到指令，但不是目标开始指令，忽略：" + command);
            return;
        }

        Debug.Log("[PenAsEndEffectorSequenceIK] 收到开始指令：" + command);
        StartSequence();
    }

    [ContextMenu("Stop Sequence")]
    public void StopSequence()
    {
        ReleaseControl();
    }

    [ContextMenu("Release Control")]
    public void ReleaseControl()
    {
        // 停止本脚本继续在 Update 里 Hold 最后姿态。
        // 注意：这里不重置关节角度，不会把机械臂拉回初始。
        controlReleased = true;
        state = MotionState.Idle;
        desiredPoseValid = false;

        Debug.Log("[PenAsEndEffectorSequenceIK] 已释放控制权，不再继续 Hold 最后姿态。", this);
    }

    [ContextMenu("Reset IK Runtime Angles")]
    public void ResetIKRuntimeAngles()
    {
        if (joints == null) return;

        foreach (IKJoint j in joints)
        {
            if (j == null || j.joint == null) continue;

            j.currentAngle = 0f;
            j.ApplyCurrentAngle();
        }

        completed = false;
        completeSignalAlreadySent = false;
        desiredPoseValid = false;
        controlReleased = false;
        state = MotionState.Idle;
    }

    [ContextMenu("Cache Current Pose As Initial")]
    public void CacheCurrentPoseAsInitial()
    {
        if (joints == null) return;

        foreach (IKJoint j in joints)
        {
            if (j != null)
            {
                j.CacheInitialPose();
            }
        }
    }

    private void UpdateRunning()
    {
        if (currentSegmentIndex < 0 || currentSegmentIndex >= segments.Length)
        {
            CompleteSequence();
            return;
        }

        MotionSegment segment = segments[currentSegmentIndex];

        if (segment == null || !segment.enabled)
        {
            MoveToNextSegment();
            return;
        }

        if (enableAxisConstraint)
        {
            ApplyAxisConstraintToDesiredPen();
        }

        // 暂停阶段：目标不动，但 IK 继续追。
        if (waitingAfterSegment)
        {
            SolveIKToDesiredPen();

            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
            {
                waitingAfterSegment = false;
                MoveToNextSegment();
            }

            return;
        }

        UpdateCurrentSegmentMovement(segment);

        SolveIKToDesiredPen();

        if (IsCurrentSegmentActuallyFinished(segment))
        {
            CompleteCurrentSegment();
        }
    }

    private void UpdateCurrentSegmentMovement(MotionSegment segment)
    {
        if (segment.targetMovementFinished)
        {
            return;
        }

        float totalDistance = Mathf.Abs(segment.distance);

        if (totalDistance <= 0.00001f)
        {
            segment.targetMovementFinished = true;
            return;
        }

        bool allowTargetMove = true;

        if (waitForPenWhenTooFar)
        {
            float leadDistance = Vector3.Distance(controlledPen.position, desiredPenPosition);

            if (leadDistance > maxLeadDistance)
            {
                allowTargetMove = false;
            }
        }

        if (!allowTargetMove)
        {
            return;
        }

        float speed = Mathf.Max(0.0001f, segment.speed);
        float remaining = totalDistance - segment.movedDistance;
        float stepDistance = Mathf.Min(speed * Time.deltaTime, remaining);

        Vector3 direction = GetSegmentMoveDirection(segment);

        desiredPenPosition += direction * stepDistance;
        segment.movedDistance += stepDistance;

        if (enableAxisConstraint)
        {
            ApplyAxisConstraintToDesiredPen();
        }

        if (segment.movedDistance >= totalDistance - 0.0001f)
        {
            segment.targetMovementFinished = true;
        }
    }

    private bool IsCurrentSegmentActuallyFinished(MotionSegment segment)
    {
        if (!segment.targetMovementFinished)
        {
            return false;
        }

        float posError = Vector3.Distance(controlledPen.position, desiredPenPosition);

        if (posError > segmentFinishPositionTolerance)
        {
            return false;
        }

        if (enableAxisConstraint)
        {
            Vector3 localPenAxis = GetLocalAxisVector(penAxisToConstrain);

            if (invertPenAxis)
            {
                localPenAxis = -localPenAxis;
            }

            Vector3 currentPenAxisWorld = controlledPen.TransformDirection(localPenAxis).normalized;
            Vector3 targetAxisWorld = GetConstrainedTargetAxisWorld(currentPenAxisWorld).normalized;

            float axisAngleError = Vector3.Angle(currentPenAxisWorld, targetAxisWorld);

            if (axisAngleError > segmentFinishRotationTolerance)
            {
                return false;
            }
        }
        else if (solveRotation)
        {
            float rotError = Quaternion.Angle(controlledPen.rotation, desiredPenRotation);

            if (rotError > segmentFinishRotationTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private void CompleteCurrentSegment()
    {
        MotionSegment segment = segments[currentSegmentIndex];

        string command = "指令-PenAsEndEffectorSequenceIK-段" + (currentSegmentIndex + 1) + "已经完成";

        if (segment != null && !string.IsNullOrEmpty(segment.segmentName))
        {
            command = "指令-PenAsEndEffectorSequenceIK-段" +
                      (currentSegmentIndex + 1) + "-" +
                      segment.segmentName + "已经完成";
        }

        Debug.Log(command);

        if (onSegmentCompleted != null)
        {
            onSegmentCompleted.Invoke(command);
        }

        float pause = 0f;

        if (segment != null)
        {
            pause = Mathf.Max(0f, segment.pauseAfter);
        }

        if (pause > 0f)
        {
            waitingAfterSegment = true;
            waitTimer = pause;
            Debug.Log("[PenAsEndEffectorSequenceIK] 段后暂停：" + pause + " 秒。");
        }
        else
        {
            MoveToNextSegment();
        }
    }

    private void MoveToNextSegment()
    {
        currentSegmentIndex = FindNextEnabledSegment(currentSegmentIndex);

        if (currentSegmentIndex < 0)
        {
            CompleteSequence();
            return;
        }

        Debug.Log("[PenAsEndEffectorSequenceIK] 进入下一段：" + GetCurrentSegmentDisplayName());
    }

    private void CompleteSequence()
    {
        state = MotionState.Complete;
        completed = true;

        SendCompleteSignal();

        // 不在这里自动 ReleaseControl。
        // 由 SequenceSignalController 在该 Step 完成后统一决定是否释放控制权。
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

    private int FindNextEnabledSegment(int afterIndex)
    {
        if (segments == null || segments.Length == 0)
        {
            return -1;
        }

        for (int i = afterIndex + 1; i < segments.Length; i++)
        {
            if (segments[i] != null && segments[i].enabled)
            {
                return i;
            }
        }

        return -1;
    }

    private void ResetRuntimeSegmentData()
    {
        if (segments == null) return;

        foreach (MotionSegment segment in segments)
        {
            if (segment == null) continue;

            segment.movedDistance = 0f;
            segment.targetMovementFinished = false;
        }
    }

    private string GetCurrentSegmentDisplayName()
    {
        if (segments == null) return "无";
        if (currentSegmentIndex < 0 || currentSegmentIndex >= segments.Length) return "无";

        MotionSegment segment = segments[currentSegmentIndex];

        if (segment == null) return "空段";

        return "段" + (currentSegmentIndex + 1) + " - " + segment.segmentName;
    }

    private Vector3 GetSegmentMoveDirection(MotionSegment segment)
    {
        Vector3 axis;

        if (segment.moveAxisSpace == MoveAxisSpace.World)
        {
            axis = GetWorldAxisFromLocalAxis(segment.moveAxis);
        }
        else
        {
            // 沿“目标笔姿态”的局部轴运动。
            // 如果笔轴被硬约束到世界 Y，则这里的 PenLocal 会跟随那个被约束后的目标姿态。
            axis = desiredPenRotation * GetLocalAxisVector(segment.moveAxis);
        }

        float sign = segment.positiveDirection ? 1f : -1f;

        if (segment.distance < 0f)
        {
            sign *= -1f;
        }

        return axis.normalized * sign;
    }

    private void ApplyAxisConstraintToDesiredPen()
    {
        if (!enableAxisConstraint) return;

        Vector3 localAxis = GetLocalAxisVector(penAxisToConstrain);

        if (invertPenAxis)
        {
            localAxis = -localAxis;
        }

        Vector3 currentAxis = desiredPenRotation * localAxis;

        if (currentAxis.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        currentAxis.Normalize();

        Vector3 targetAxis = GetConstrainedTargetAxisWorld(currentAxis);

        if (targetAxis.sqrMagnitude < 0.0000001f)
        {
            return;
        }

        targetAxis.Normalize();

        Quaternion correction = Quaternion.FromToRotation(currentAxis, targetAxis);
        desiredPenRotation = correction * desiredPenRotation;
    }

    private Vector3 GetConstrainedTargetAxisWorld(Vector3 currentPenAxisWorld)
    {
        Vector3 worldAxis = GetWorldAxisVector(worldAxisToConstrain);

        if (!usePositiveWorldAxis)
        {
            worldAxis = -worldAxis;
        }

        if (axisRelation == AxisRelation.Parallel)
        {
            return worldAxis.normalized;
        }

        return GetTargetAxisForPerpendicularConstraint(worldAxis, currentPenAxisWorld).normalized;
    }

    private Vector3 GetTargetAxisForPerpendicularConstraint(Vector3 worldAxis, Vector3 currentAxis)
    {
        Vector3 targetAxis;

        if (usePreferredDirectionWhenPerpendicular)
        {
            Vector3 preferred = GetWorldAxisVector(preferredPerpendicularWorldDirection);

            if (!preferredPositiveDirection)
            {
                preferred = -preferred;
            }

            targetAxis = Vector3.ProjectOnPlane(preferred, worldAxis);

            if (targetAxis.sqrMagnitude < 0.0000001f)
            {
                targetAxis = GetSafePerpendicular(worldAxis);
            }
        }
        else
        {
            targetAxis = Vector3.ProjectOnPlane(currentAxis, worldAxis);

            if (targetAxis.sqrMagnitude < 0.0000001f)
            {
                targetAxis = GetSafePerpendicular(worldAxis);
            }
        }

        return targetAxis.normalized;
    }

    private static Vector3 GetSafePerpendicular(Vector3 axis)
    {
        axis = axis.normalized;

        Vector3 helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.right;

        return Vector3.Cross(axis, helper).normalized;
    }

    private void SolveIKToDesiredPen()
    {
        SolveIKToGoal(desiredPenPosition, desiredPenRotation);
    }

    private void SolveIKToGoal(Vector3 goalPosition, Quaternion goalRotation)
    {
        if (joints == null || joints.Length == 0) return;

        int steps = Mathf.Max(1, solverSubsteps);
        float subDeltaTime = Time.deltaTime / steps;

        for (int i = 0; i < steps; i++)
        {
            float positionError = Vector3.Distance(controlledPen.position, goalPosition);

            if (!solveRotation && !enableAxisConstraint && positionError <= ikPositionTolerance)
            {
                break;
            }

            SolveOneDLSIteration(goalPosition, goalRotation, subDeltaTime);
        }
    }

    private void SolveOneDLSIteration(Vector3 goalPosition, Quaternion goalRotation, float subDeltaTime)
    {
        List<IKJoint> activeJoints = new List<IKJoint>();

        foreach (IKJoint j in joints)
        {
            if (j == null) continue;
            if (j.joint == null) continue;
            if (!j.enabled) continue;
            if (j.weight <= 0f) continue;

            activeJoints.Add(j);
        }

        if (activeJoints.Count == 0) return;

        // 位置 3 行 + 角度 3 行。
        // 如果 enableAxisConstraint 开启，角度行专门用于笔轴硬约束。
        bool useAngularRows = solveRotation || enableAxisConstraint;

        int rows = useAngularRows ? 6 : 3;
        int cols = activeJoints.Count;

        double[,] J = new double[rows, cols];
        double[] error = new double[rows];

        Vector3 posError = goalPosition - controlledPen.position;

        error[0] = posError.x;
        error[1] = posError.y;
        error[2] = posError.z;

        if (useAngularRows)
        {
            Vector3 angularError;

            if (enableAxisConstraint)
            {
                Vector3 localPenAxis = GetLocalAxisVector(penAxisToConstrain);

                if (invertPenAxis)
                {
                    localPenAxis = -localPenAxis;
                }

                Vector3 currentPenAxisWorld = controlledPen.TransformDirection(localPenAxis).normalized;
                Vector3 targetAxisWorld = GetConstrainedTargetAxisWorld(currentPenAxisWorld).normalized;

                // 最短旋转方向：把当前笔轴转到目标轴。
                angularError = Vector3.Cross(currentPenAxisWorld, targetAxisWorld);

                angularError *= axisConstraintWeight;
            }
            else
            {
                angularError = GetRotationErrorVectorRadians(goalRotation, controlledPen.rotation);
                angularError *= rotationWeight;
            }

            error[3] = angularError.x;
            error[4] = angularError.y;
            error[5] = angularError.z;
        }

        for (int c = 0; c < cols; c++)
        {
            IKJoint j = activeJoints[c];

            Vector3 axisWorld = j.GetWorldAxis();

            // 末端就是 controlledPen。
            Vector3 r = controlledPen.position - j.joint.position;

            Vector3 linear = Vector3.Cross(axisWorld, r);

            float w = j.weight;

            J[0, c] = linear.x * w;
            J[1, c] = linear.y * w;
            J[2, c] = linear.z * w;

            if (useAngularRows)
            {
                float angularWeight = enableAxisConstraint ? axisConstraintWeight : rotationWeight;

                J[3, c] = axisWorld.x * angularWeight * w;
                J[4, c] = axisWorld.y * angularWeight * w;
                J[5, c] = axisWorld.z * angularWeight * w;
            }
        }

        double[] deltaRadians = SolveDampedLeastSquares(J, error, damping);

        if (deltaRadians == null) return;

        float maxDeltaThisStep = maxJointSpeedDegPerSec * subDeltaTime;

        for (int i = 0; i < activeJoints.Count; i++)
        {
            IKJoint j = activeJoints[i];

            float deltaDeg = (float)(deltaRadians[i] * Mathf.Rad2Deg);

            j.AddAngle(deltaDeg, maxDeltaThisStep);
        }
    }

    private Vector3 GetRotationErrorVectorRadians(Quaternion targetRotation, Quaternion currentRotation)
    {
        Quaternion q = targetRotation * Quaternion.Inverse(currentRotation);

        q.ToAngleAxis(out float angleDeg, out Vector3 axis);

        if (float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsNaN(axis.z))
        {
            return Vector3.zero;
        }

        if (angleDeg > 180f)
        {
            angleDeg -= 360f;
        }

        if (Mathf.Abs(angleDeg) < 0.0001f)
        {
            return Vector3.zero;
        }

        float angleRad = angleDeg * Mathf.Deg2Rad;
        return axis.normalized * angleRad;
    }

    /// <summary>
    /// DLS:
    /// delta = J^T * (J * J^T + lambda^2 I)^-1 * error
    /// </summary>
    private double[] SolveDampedLeastSquares(double[,] J, double[] error, float lambda)
    {
        int rows = J.GetLength(0);
        int cols = J.GetLength(1);

        double[,] A = new double[rows, rows];

        for (int r = 0; r < rows; r++)
        {
            for (int s = 0; s < rows; s++)
            {
                double sum = 0.0;

                for (int c = 0; c < cols; c++)
                {
                    sum += J[r, c] * J[s, c];
                }

                A[r, s] = sum;
            }
        }

        double damp = lambda * lambda;

        for (int i = 0; i < rows; i++)
        {
            A[i, i] += damp;
        }

        double[] y = SolveLinearSystem(A, error);

        if (y == null) return null;

        double[] delta = new double[cols];

        for (int c = 0; c < cols; c++)
        {
            double sum = 0.0;

            for (int r = 0; r < rows; r++)
            {
                sum += J[r, c] * y[r];
            }

            delta[c] = sum;
        }

        return delta;
    }

    /// <summary>
    /// 高斯消元，解 A x = b。
    /// </summary>
    private double[] SolveLinearSystem(double[,] A, double[] b)
    {
        int n = b.Length;

        double[,] M = new double[n, n + 1];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                M[r, c] = A[r, c];
            }

            M[r, n] = b[r];
        }

        for (int pivot = 0; pivot < n; pivot++)
        {
            int bestRow = pivot;
            double bestValue = Math.Abs(M[pivot, pivot]);

            for (int r = pivot + 1; r < n; r++)
            {
                double value = Math.Abs(M[r, pivot]);

                if (value > bestValue)
                {
                    bestValue = value;
                    bestRow = r;
                }
            }

            if (bestValue < 1e-9)
            {
                return null;
            }

            if (bestRow != pivot)
            {
                for (int c = pivot; c <= n; c++)
                {
                    double temp = M[pivot, c];
                    M[pivot, c] = M[bestRow, c];
                    M[bestRow, c] = temp;
                }
            }

            double div = M[pivot, pivot];

            for (int c = pivot; c <= n; c++)
            {
                M[pivot, c] /= div;
            }

            for (int r = 0; r < n; r++)
            {
                if (r == pivot) continue;

                double factor = M[r, pivot];

                for (int c = pivot; c <= n; c++)
                {
                    M[r, c] -= factor * M[pivot, c];
                }
            }
        }

        double[] x = new double[n];

        for (int r = 0; r < n; r++)
        {
            x[r] = M[r, n];
        }

        return x;
    }

    private bool CheckReferences()
    {
        return controlledPen != null &&
               joints != null &&
               joints.Length > 0;
    }

    private static Vector3 GetLocalAxisVector(LocalAxis axis)
    {
        switch (axis)
        {
            case LocalAxis.X:
                return Vector3.right;

            case LocalAxis.Y:
                return Vector3.up;

            case LocalAxis.Z:
                return Vector3.forward;

            default:
                return Vector3.forward;
        }
    }

    private static Vector3 GetWorldAxisFromLocalAxis(LocalAxis axis)
    {
        switch (axis)
        {
            case LocalAxis.X:
                return Vector3.right;

            case LocalAxis.Y:
                return Vector3.up;

            case LocalAxis.Z:
                return Vector3.forward;

            default:
                return Vector3.forward;
        }
    }

    private static Vector3 GetWorldAxisVector(WorldAxis axis)
    {
        switch (axis)
        {
            case WorldAxis.WorldX:
                return Vector3.right;

            case WorldAxis.WorldY:
                return Vector3.up;

            case WorldAxis.WorldZ:
                return Vector3.forward;

            default:
                return Vector3.forward;
        }
    }

    public bool IsCompleted()
    {
        return completed;
    }

    public bool IsControlReleased()
    {
        return controlReleased;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos) return;

        if (controlledPen != null)
        {
            Gizmos.DrawWireSphere(controlledPen.position, 0.025f);

            Vector3 localPenAxis = GetLocalAxisVector(penAxisToConstrain);

            if (invertPenAxis)
            {
                localPenAxis = -localPenAxis;
            }

            Vector3 penAxisWorld = controlledPen.TransformDirection(localPenAxis).normalized;
            Gizmos.DrawLine(controlledPen.position, controlledPen.position + penAxisWorld * 0.2f);
        }

        if (desiredPoseValid)
        {
            Gizmos.DrawWireSphere(desiredPenPosition, 0.035f);

            if (controlledPen != null)
            {
                Gizmos.DrawLine(controlledPen.position, desiredPenPosition);
            }
        }
    }
}