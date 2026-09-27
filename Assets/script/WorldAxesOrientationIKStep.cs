using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 重型 DLS 姿态 IK：
/// 让指定 targetObject 的三根本地轴，对齐到指定世界坐标轴。
///
/// 例子：
/// Target Local X -> World X
/// Target Local Y -> World Y
/// Target Local Z -> World Z
///
/// 或者你可以自己指定：
/// Target Local X -> World Z
/// Target Local Y -> World Y
/// Target Local Z -> Negative World X
///
/// 接入 SequenceSignalController：
/// 1. 总控调用 StartSequence()
/// 2. 本脚本用多关节 IK 调整 targetObject 姿态
/// 3. 完成后 onCompleted 回传 completeCommand
/// 4. 失败后 onFailed 回传 failedCommand
///
/// 注意：
/// - 不在 Start() 自动执行
/// - 不私自调用下一步
/// - 不控制位置，只控制姿态
/// - joints 建议从根到末端：底座 -> 大臂 -> 小臂 -> 手腕 -> 手掌
/// </summary>
public class WorldAxesOrientationIKStep : MonoBehaviour
{
    public enum Axis
    {
        X,
        Y,
        Z,
        NegativeX,
        NegativeY,
        NegativeZ
    }

    [Serializable]
    public class IKCommandEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public class IKJoint
    {
        [Header("关节")]
        public string displayName;
        public Transform joint;

        [Header("本关节绕自己的哪根 local 轴转")]
        public Axis axis = Axis.Y;

        [Tooltip("方向反了就勾这个。")]
        public bool invertAxis = false;

        [Header("是否参与 IK")]
        public bool enabled = true;

        [Header("权重")]
        [Tooltip("底座/大臂/小臂建议 1；手腕/手掌可 0.3~1；手指第一版建议禁用。")]
        [Range(0f, 2f)]
        public float weight = 1f;

        [Header("角度限制，可先不启用")]
        public bool useLimits = false;

        [Tooltip("以 StartSequence 启动时为 0 度")]
        public float minAngle = -180f;

        [Tooltip("以 StartSequence 启动时为 0 度")]
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
            Vector3 a = WorldAxesOrientationIKStep.AxisToVector(axis);

            if (invertAxis)
            {
                a = -a;
            }

            return a.normalized;
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

    [Header("Sequence Signal Protocol")]
    [Tooltip("完成时回传给 SequenceSignalController 的信号，必须和总控 Expected Complete Command 完全一致")]
    public string completeCommand = "WORLD_AXES_ORIENTATION_DONE";

    [Tooltip("失败时回传给 SequenceSignalController 的信号")]
    public string failedCommand = "WORLD_AXES_ORIENTATION_FAILED";

    public IKCommandEvent onCompleted;
    public IKCommandEvent onFailed;

    [Header("目标物")]
    [Tooltip("要让它的三根本地轴对齐世界轴的物体。比如笔、末端执行器 Empty、部件 Empty。")]
    public Transform targetObject;

    [Header("三轴匹配：目标物 Local X/Y/Z 分别对齐哪个世界方向")]
    [Tooltip("目标物 Local X 需要对齐到哪个世界方向")]
    public Axis desiredWorldForLocalX = Axis.X;

    [Tooltip("目标物 Local Y 需要对齐到哪个世界方向")]
    public Axis desiredWorldForLocalY = Axis.Y;

    [Tooltip("目标物 Local Z 需要对齐到哪个世界方向")]
    public Axis desiredWorldForLocalZ = Axis.Z;

    [Header("IK 关节，从根到末端")]
    [Tooltip("建议顺序：底座 -> 大臂 -> 小臂 -> 手腕 -> 手掌。手指第一版建议禁用。")]
    public IKJoint[] joints;

    [Header("DLS IK 参数")]
    [Tooltip("每帧分几次小步计算")]
    public int solverSubsteps = 8;

    [Tooltip("每个关节最大角速度，单位：度/秒")]
    public float maxJointSpeedDegPerSec = 45f;

    [Tooltip("阻尼，越大越稳，越小越猛。建议 0.08 ~ 0.3")]
    public float damping = 0.12f;

    [Tooltip("姿态误差增益。太小会慢，太大容易抖。")]
    public float orientationErrorGain = 1.0f;

    [Tooltip("姿态误差小于这个角度就算完成")]
    public float toleranceDegrees = 1.0f;

    [Tooltip("最多运行多久，超过则失败")]
    public float timeoutSeconds = 10f;

    [Tooltip("连续多少帧几乎没有改善，就认为卡住。0 = 不启用卡住检测，只等 timeout。")]
    public int stuckFrameLimit = 150;

    [Tooltip("每帧误差改善小于这个值，就认为几乎没改善")]
    public float minUsefulImprovementDegrees = 0.0005f;

    [Header("Best Effort")]
    [Tooltip("超时或卡住但误差已经比较小，也算完成")]
    public bool allowBestEffortComplete = true;

    [Tooltip("Best Effort 的容差")]
    public float bestEffortToleranceDegrees = 3f;

    [Header("完成后保持姿态")]
    [Tooltip("最后一步建议勾上。完成后继续在 LateUpdate 保持本脚本算出的关节角度。")]
    public bool holdPoseAfterComplete = true;

    [Tooltip("失败后是否也保持当前姿态。一般不勾。")]
    public bool holdPoseAfterFail = false;

    [Header("Debug")]
    public bool verboseLog = true;
    public bool drawDebugGizmos = true;

    [Header("运行时显示")]
    public string state = "Idle";
    public float currentErrorDegrees = 999f;
    public float bestErrorDegrees = 999f;
    public int stuckFrames = 0;

    public Quaternion desiredWorldRotation = Quaternion.identity;

    public Vector3 desiredWorldX = Vector3.right;
    public Vector3 desiredWorldY = Vector3.up;
    public Vector3 desiredWorldZ = Vector3.forward;

    private bool isRunning = false;
    private bool hasFinished = false;
    private bool shouldHoldPose = false;
    private Coroutine runningCoroutine;

    // ============================================================
    // 总控统一启动入口
    // ============================================================

    public void StartSequence()
    {
        InternalStartSequence("");
    }

    public void StartSequence(string startCommand)
    {
        InternalStartSequence(startCommand);
    }

    [ContextMenu("Start Orientation IK")]
    public void StartOrientationIK()
    {
        StartSequence();
    }

    private void InternalStartSequence(string startCommand)
    {
        if (isRunning)
        {
            Debug.LogWarning($"[WorldAxesOrientationIKStep] 重复启动被忽略。Object = {name}", this);
            return;
        }

        hasFinished = false;
        isRunning = false;
        shouldHoldPose = false;
        state = "Checking";

        if (!ValidateSettings())
        {
            Fail("参数检查失败");
            return;
        }

        CacheCurrentPoseAsInitial();
        ResetRuntimeData();

        currentErrorDegrees = GetCurrentOrientationErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;

        if (verboseLog)
        {
            Debug.Log(
                $"[WorldAxesOrientationIKStep] 开始三轴姿态 IK。Object = {name}, " +
                $"StartCommand = [{startCommand}], StartError = {currentErrorDegrees:F3}°",
                this
            );
        }

        runningCoroutine = StartCoroutine(OrientationRoutine());
    }

    [ContextMenu("Stop Orientation IK")]
    public void StopSequence()
    {
        StopRunningCoroutineOnly();

        isRunning = false;
        hasFinished = true;
        shouldHoldPose = false;
        state = "Stopped";

        if (verboseLog)
        {
            Debug.Log($"[WorldAxesOrientationIKStep] StopSequence 被调用。Object = {name}", this);
        }
    }

    [ContextMenu("Release Control")]
    public void ReleaseControl()
    {
        StopRunningCoroutineOnly();

        isRunning = false;
        hasFinished = true;
        shouldHoldPose = false;
        state = "Released";

        if (verboseLog)
        {
            Debug.Log($"[WorldAxesOrientationIKStep] 已释放控制权。Object = {name}", this);
        }
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

    [ContextMenu("Reset To Cached Initial Pose")]
    public void ResetToCachedInitialPose()
    {
        if (joints == null) return;

        foreach (IKJoint j in joints)
        {
            if (j == null || j.joint == null) continue;

            j.currentAngle = 0f;
            j.ApplyCurrentAngle();
        }

        state = "Idle";
        isRunning = false;
        hasFinished = false;
        shouldHoldPose = false;
        currentErrorDegrees = GetCurrentOrientationErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;
        stuckFrames = 0;
    }

    // ============================================================
    // 主协程
    // ============================================================

    private IEnumerator OrientationRoutine()
    {
        isRunning = true;
        state = "Running";

        float timer = 0f;

        while (!hasFinished)
        {
            timer += Time.deltaTime;

            float frameStartError = GetCurrentOrientationErrorDegrees();
            currentErrorDegrees = frameStartError;

            if (currentErrorDegrees < bestErrorDegrees)
            {
                bestErrorDegrees = currentErrorDegrees;
            }

            if (currentErrorDegrees <= toleranceDegrees)
            {
                Complete($"误差进入容差：{currentErrorDegrees:F3}°");
                yield break;
            }

            if (timer > timeoutSeconds)
            {
                HandleTimeoutOrStuck($"超时：{timeoutSeconds:F1} 秒内没有完成。当前误差 = {currentErrorDegrees:F3}°");
                yield break;
            }

            int steps = Mathf.Max(1, solverSubsteps);
            float subDeltaTime = Time.deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                SolveOneDLSOrientationIteration(subDeltaTime);

                currentErrorDegrees = GetCurrentOrientationErrorDegrees();

                if (currentErrorDegrees <= toleranceDegrees)
                {
                    Complete($"迭代中进入容差：{currentErrorDegrees:F3}°");
                    yield break;
                }
            }

            float frameEndError = GetCurrentOrientationErrorDegrees();
            currentErrorDegrees = frameEndError;

            if (frameStartError - frameEndError > minUsefulImprovementDegrees)
            {
                stuckFrames = 0;
            }
            else
            {
                stuckFrames++;
            }

            if (stuckFrameLimit > 0 && stuckFrames >= stuckFrameLimit)
            {
                HandleTimeoutOrStuck($"疑似卡住：连续 {stuckFrames} 帧没有明显改善。当前误差 = {currentErrorDegrees:F3}°");
                yield break;
            }

            yield return null;
        }
    }

    // ============================================================
    // DLS 姿态 IK
    // ============================================================

    private void SolveOneDLSOrientationIteration(float subDeltaTime)
    {
        List<IKJoint> activeJoints = CollectActiveJoints();

        if (activeJoints.Count == 0)
            return;

        Vector3 rotationError = GetRotationErrorVectorRadians(desiredWorldRotation, targetObject.rotation);
        rotationError *= orientationErrorGain;

        double[] error = new double[3];
        error[0] = rotationError.x;
        error[1] = rotationError.y;
        error[2] = rotationError.z;

        int rows = 3;
        int cols = activeJoints.Count;

        double[,] J = new double[rows, cols];

        for (int c = 0; c < cols; c++)
        {
            IKJoint j = activeJoints[c];

            Vector3 axisWorld = j.GetWorldAxis();
            float w = j.weight;

            // 姿态 IK 的雅可比：
            // 单自由度关节绕 axisWorld 转，会给末端提供这个世界角速度方向。
            J[0, c] = axisWorld.x * w;
            J[1, c] = axisWorld.y * w;
            J[2, c] = axisWorld.z * w;
        }

        double[] deltaRadians = SolveDampedLeastSquares(J, error, damping);

        if (deltaRadians == null)
            return;

        float maxDeltaThisStep = maxJointSpeedDegPerSec * subDeltaTime;

        for (int i = 0; i < activeJoints.Count; i++)
        {
            IKJoint j = activeJoints[i];

            float deltaDeg = (float)(deltaRadians[i] * Mathf.Rad2Deg);

            j.AddAngle(deltaDeg, maxDeltaThisStep);
        }
    }

    private List<IKJoint> CollectActiveJoints()
    {
        List<IKJoint> activeJoints = new List<IKJoint>();

        if (joints == null)
            return activeJoints;

        foreach (IKJoint j in joints)
        {
            if (j == null) continue;
            if (j.joint == null) continue;
            if (!j.enabled) continue;
            if (j.weight <= 0f) continue;

            activeJoints.Add(j);
        }

        return activeJoints;
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

    // ============================================================
    // DLS 线性代数
    // delta = J^T * (J * J^T + lambda^2 I)^-1 * error
    // ============================================================

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

        if (damp < 1e-9)
        {
            damp = 1e-9;
        }

        for (int i = 0; i < rows; i++)
        {
            A[i, i] += damp;
        }

        double[] y = SolveLinearSystem(A, error);

        if (y == null)
            return null;

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

            if (bestValue < 1e-12)
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

    // ============================================================
    // Desired Rotation / Axis
    // ============================================================

    private void ComputeDesiredRotation()
    {
        desiredWorldX = AxisToVector(desiredWorldForLocalX).normalized;
        desiredWorldY = AxisToVector(desiredWorldForLocalY).normalized;
        desiredWorldZ = AxisToVector(desiredWorldForLocalZ).normalized;

        desiredWorldRotation = Quaternion.LookRotation(desiredWorldZ, desiredWorldY);
    }

    private bool ValidateDesiredAxes()
    {
        Vector3 x = AxisToVector(desiredWorldForLocalX).normalized;
        Vector3 y = AxisToVector(desiredWorldForLocalY).normalized;
        Vector3 z = AxisToVector(desiredWorldForLocalZ).normalized;

        float xy = Mathf.Abs(Vector3.Dot(x, y));
        float yz = Mathf.Abs(Vector3.Dot(y, z));
        float zx = Mathf.Abs(Vector3.Dot(z, x));

        if (xy > 0.001f || yz > 0.001f || zx > 0.001f)
        {
            Debug.LogError(
                "[WorldAxesOrientationIKStep] 三个目标世界轴不是互相垂直的。请不要重复指定同一根轴。\n" +
                $"LocalX -> {desiredWorldForLocalX}, LocalY -> {desiredWorldForLocalY}, LocalZ -> {desiredWorldForLocalZ}",
                this
            );

            return false;
        }

        // Unity 的 rotation 是右手系：Local X = Local Y × Local Z
        Vector3 expectedX = Vector3.Cross(y, z).normalized;
        float handedness = Vector3.Dot(expectedX, x);

        if (handedness < 0.99f)
        {
            Debug.LogError(
                "[WorldAxesOrientationIKStep] 三轴匹配不是一个合法旋转，可能是左右手系反了。\n" +
                "要求：WorldXDirection 应该等于 WorldYDirection × WorldZDirection。\n" +
                $"当前：LocalX -> {desiredWorldForLocalX}, LocalY -> {desiredWorldForLocalY}, LocalZ -> {desiredWorldForLocalZ}\n" +
                "举例合法组合：X,Y,Z 或 Z,Y,NegativeX 或 NegativeX,Y,NegativeZ。",
                this
            );

            return false;
        }

        return true;
    }

    private float GetCurrentOrientationErrorDegrees()
    {
        if (targetObject == null)
            return 999f;

        return Quaternion.Angle(targetObject.rotation, desiredWorldRotation);
    }

    public static Vector3 AxisToVector(Axis axis)
    {
        switch (axis)
        {
            case Axis.X:
                return Vector3.right;

            case Axis.Y:
                return Vector3.up;

            case Axis.Z:
                return Vector3.forward;

            case Axis.NegativeX:
                return Vector3.left;

            case Axis.NegativeY:
                return Vector3.down;

            case Axis.NegativeZ:
                return Vector3.back;

            default:
                return Vector3.forward;
        }
    }

    // ============================================================
    // Runtime / Validate
    // ============================================================

    private void ResetRuntimeData()
    {
        stuckFrames = 0;
        ComputeDesiredRotation();

        currentErrorDegrees = GetCurrentOrientationErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;
    }

    private bool ValidateSettings()
    {
        if (targetObject == null)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] targetObject 没填。", this);
            return false;
        }

        if (!ValidateDesiredAxes())
        {
            return false;
        }

        ComputeDesiredRotation();

        if (joints == null || joints.Length == 0)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] joints 为空。至少填一个关节。", this);
            return false;
        }

        bool hasEnabledJoint = false;

        for (int i = 0; i < joints.Length; i++)
        {
            IKJoint jointConfig = joints[i];

            if (jointConfig == null)
            {
                Debug.LogError($"[WorldAxesOrientationIKStep] joints[{i}] 是空的。", this);
                return false;
            }

            if (!jointConfig.enabled)
                continue;

            hasEnabledJoint = true;

            if (jointConfig.joint == null)
            {
                Debug.LogError($"[WorldAxesOrientationIKStep] joints[{i}] 的 joint 没填。", this);
                return false;
            }

            if (jointConfig.useLimits && jointConfig.minAngle > jointConfig.maxAngle)
            {
                Debug.LogError($"[WorldAxesOrientationIKStep] joints[{i}] 的角度限制错误：minAngle > maxAngle。", this);
                return false;
            }
        }

        if (!hasEnabledJoint)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] 没有任何启用的关节。", this);
            return false;
        }

        if (solverSubsteps <= 0)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] solverSubsteps 必须大于 0。", this);
            return false;
        }

        if (maxJointSpeedDegPerSec <= 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] maxJointSpeedDegPerSec 必须大于 0。", this);
            return false;
        }

        if (damping < 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] damping 不能小于 0。", this);
            return false;
        }

        if (orientationErrorGain <= 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] orientationErrorGain 必须大于 0。", this);
            return false;
        }

        if (toleranceDegrees <= 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] toleranceDegrees 必须大于 0。", this);
            return false;
        }

        if (timeoutSeconds <= 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] timeoutSeconds 必须大于 0。", this);
            return false;
        }

        if (bestEffortToleranceDegrees <= 0f)
        {
            Debug.LogError("[WorldAxesOrientationIKStep] bestEffortToleranceDegrees 必须大于 0。", this);
            return false;
        }

        return true;
    }

    // ============================================================
    // Complete / Fail
    // ============================================================

    private void HandleTimeoutOrStuck(string reason)
    {
        currentErrorDegrees = GetCurrentOrientationErrorDegrees();

        if (allowBestEffortComplete && currentErrorDegrees <= bestEffortToleranceDegrees)
        {
            Complete(reason + $" 但达到 Best Effort：{currentErrorDegrees:F3}°");
            return;
        }

        Fail(reason);
    }

    private void Complete(string reason)
    {
        if (hasFinished)
            return;

        hasFinished = true;
        isRunning = false;
        shouldHoldPose = holdPoseAfterComplete;
        state = "Completed";
        runningCoroutine = null;

        currentErrorDegrees = GetCurrentOrientationErrorDegrees();

        Debug.Log(
            $"[WorldAxesOrientationIKStep] 完成。Reason = {reason}, Command = [{completeCommand}], Error = {currentErrorDegrees:F3}°",
            this
        );

        onCompleted?.Invoke(completeCommand);
    }

    private void Fail(string reason)
    {
        if (hasFinished)
            return;

        hasFinished = true;
        isRunning = false;
        shouldHoldPose = holdPoseAfterFail;
        state = "Failed";

        StopRunningCoroutineOnly();

        currentErrorDegrees = GetCurrentOrientationErrorDegrees();

        Debug.LogWarning(
            $"[WorldAxesOrientationIKStep] 失败。Reason = {reason}, Command = [{failedCommand}], Error = {currentErrorDegrees:F3}°",
            this
        );

        onFailed?.Invoke(failedCommand);
    }

    private void StopRunningCoroutineOnly()
    {
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
    }

    private void LateUpdate()
    {
        if (!shouldHoldPose)
            return;

        if (joints == null)
            return;

        foreach (IKJoint j in joints)
        {
            if (j == null) continue;
            if (j.joint == null) continue;
            if (!j.enabled) continue;

            j.ApplyCurrentAngle();
        }
    }

    // ============================================================
    // Gizmos
    // ============================================================

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
            return;

        if (targetObject == null)
            return;

        ComputeDesiredRotation();

        Vector3 origin = targetObject.position;

        // 当前三轴
        Gizmos.DrawLine(origin, origin + targetObject.right * 0.25f);
        Gizmos.DrawLine(origin, origin + targetObject.up * 0.32f);
        Gizmos.DrawLine(origin, origin + targetObject.forward * 0.39f);

        // 目标三轴
        Gizmos.DrawWireSphere(origin + desiredWorldX * 0.28f, 0.015f);
        Gizmos.DrawWireSphere(origin + desiredWorldY * 0.35f, 0.015f);
        Gizmos.DrawWireSphere(origin + desiredWorldZ * 0.42f, 0.015f);
    }
}