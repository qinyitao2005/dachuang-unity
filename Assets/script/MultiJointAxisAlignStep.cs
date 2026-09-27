using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 多关节 DLS 轴对齐步骤。
///
/// 目标：
/// 让 targetObject 的某根本地轴，与指定世界轴平行。
///
/// 接入规范：
/// 1. 不在 Start() 自动执行
/// 2. 总控调用 StartSequence()
/// 3. 完成后 onCompleted 回传 completeCommand
/// 4. 失败后 onFailed 回传 failedCommand
/// 5. 子脚本不私自启动下一步
/// </summary>
public class MultiJointAxisAlignStep : MonoBehaviour
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
    public class AxisAlignCommandEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public class JointConfig
    {
        [Header("关节")]
        public string displayName;
        public Transform joint;

        [Header("本关节绕自己的哪根 local 轴转")]
        public Axis axis = Axis.Z;

        [Tooltip("方向反了就勾这个。")]
        public bool invertAxis = false;

        [Header("是否参与 IK")]
        public bool enabled = true;

        [Header("权重")]
        [Tooltip("底座/大臂/小臂建议 1；手掌 0.2~0.8；手指第一版建议 0 或禁用。")]
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
            Vector3 a = MultiJointAxisAlignStep.AxisToVector(axis);

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

            float nextAngle = currentAngle + deltaAngle;

            if (useLimits)
            {
                nextAngle = Mathf.Clamp(nextAngle, minAngle, maxAngle);
            }

            currentAngle = nextAngle;
            ApplyCurrentAngle();
        }
    }

    [Header("Sequence Signal Protocol")]
    [Tooltip("完成时回传给 SequenceSignalController 的信号，必须和总控 Expected Complete Command 完全一致")]
    public string completeCommand = "MULTI_AXIS_ALIGNED";

    [Tooltip("失败时回传给 SequenceSignalController 的信号")]
    public string failedCommand = "MULTI_AXIS_ALIGN_FAILED";

    public AxisAlignCommandEvent onCompleted;
    public AxisAlignCommandEvent onFailed;

    [Header("Target Axis")]
    [Tooltip("被观察轴的目标物，比如笔、笔尖 Empty、末端执行器 Empty")]
    public Transform targetObject;

    [Tooltip("目标物的哪根本地轴需要和世界轴平行")]
    public Axis targetLocalAxis = Axis.Y;

    [Tooltip("希望目标物轴平行于哪个世界坐标轴")]
    public Axis desiredWorldAxis = Axis.Y;

    [Tooltip("true = 只要求平行，正反方向都可以；false = 必须同方向")]
    public bool allowOppositeDirection = true;

    [Header("IK 关节，从根到末端")]
    [Tooltip("建议顺序：底座 -> 大臂 -> 小臂 -> 手腕 -> 手掌。手指第一版建议禁用。")]
    public JointConfig[] joints;

    [Header("DLS IK 参数")]
    [Tooltip("每帧分几次小步计算。")]
    public int solverSubsteps = 6;

    [Tooltip("每个关节最大角速度，单位：度/秒。这个控制机械臂不会瞬移。")]
    public float maxJointSpeedDegPerSec = 45f;

    [Tooltip("阻尼，越大越稳，越小越猛。建议 0.05 ~ 0.3。")]
    public float damping = 0.12f;

    [Tooltip("方向误差增益。太小会慢，太大容易抖。")]
    public float directionErrorGain = 1.0f;

    [Tooltip("误差小于这个角度就算完成。")]
    public float toleranceDegrees = 1.5f;

    [Tooltip("最多运行多久，超过则失败。")]
    public float timeoutSeconds = 10f;

    [Tooltip("连续多少帧几乎没有改善，就认为卡住。0 = 不启用卡住检测，只等 timeout。")]
    public int stuckFrameLimit = 120;

    [Tooltip("每帧误差改善小于这个值，就认为几乎没改善。")]
    public float minUsefulImprovementDegrees = 0.0005f;

    [Header("Best Effort")]
    [Tooltip("超时或卡住但误差已经比较小，也算完成。")]
    public bool allowBestEffortComplete = true;

    [Tooltip("Best Effort 的容差。")]
    public float bestEffortToleranceDegrees = 5f;

    [Header("Debug")]
    [Tooltip("只输出开始/完成/失败，不再每个关节疯狂刷屏。")]
    public bool verboseLog = true;

    public bool drawDebugGizmos = true;

    [Header("运行时显示")]
    public string state = "Idle";
    public float currentErrorDegrees = 999f;
    public float bestErrorDegrees = 999f;
    public int stuckFrames = 0;

    public Vector3 currentTargetAxisWorld;
    public Vector3 currentDesiredAxisWorld;

    private bool isRunning = false;
    private bool hasFinished = false;
    private Coroutine runningCoroutine;

    public void StartSequence()
    {
        InternalStartSequence("");
    }

    public void StartSequence(string startCommand)
    {
        InternalStartSequence(startCommand);
    }

    [ContextMenu("Start Axis Align")]
    public void StartAxisAlign()
    {
        StartSequence();
    }

    private void InternalStartSequence(string startCommand)
    {
        if (isRunning)
        {
            Debug.LogWarning($"[MultiJointAxisAlignStep] 重复启动被忽略。Object = {name}", this);
            return;
        }

        hasFinished = false;
        isRunning = false;
        state = "Checking";

        if (!ValidateSettings())
        {
            Fail("参数检查失败");
            return;
        }

        CacheCurrentPoseAsInitial();
        ResetRuntimeData();

        currentErrorDegrees = GetCurrentAxisErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;

        if (verboseLog)
        {
            Debug.Log(
                $"[MultiJointAxisAlignStep] 开始 DLS 轴对齐。Object = {name}, " +
                $"StartCommand = [{startCommand}], " +
                $"StartError = {currentErrorDegrees:F3}°",
                this
            );
        }

        runningCoroutine = StartCoroutine(AlignRoutine());
    }

    [ContextMenu("Stop Axis Align")]
    public void StopSequence()
    {
        StopRunningCoroutineOnly();

        isRunning = false;
        hasFinished = true;
        state = "Stopped";

        if (verboseLog)
        {
            Debug.Log($"[MultiJointAxisAlignStep] StopSequence 被调用。Object = {name}", this);
        }
    }

    [ContextMenu("Reset To Cached Initial Pose")]
    public void ResetToCachedInitialPose()
    {
        if (joints == null) return;

        foreach (JointConfig j in joints)
        {
            if (j == null || j.joint == null) continue;

            j.currentAngle = 0f;
            j.ApplyCurrentAngle();
        }

        state = "Idle";
        isRunning = false;
        hasFinished = false;
        currentErrorDegrees = GetCurrentAxisErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;
        stuckFrames = 0;
    }

    [ContextMenu("Cache Current Pose As Initial")]
    public void CacheCurrentPoseAsInitial()
    {
        if (joints == null) return;

        foreach (JointConfig j in joints)
        {
            if (j != null)
            {
                j.CacheInitialPose();
            }
        }
    }

    private IEnumerator AlignRoutine()
    {
        isRunning = true;
        state = "Running";

        float timer = 0f;

        while (!hasFinished)
        {
            timer += Time.deltaTime;

            float frameStartError = GetCurrentAxisErrorDegrees();
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
                SolveOneDLSDirectionIteration(subDeltaTime);

                currentErrorDegrees = GetCurrentAxisErrorDegrees();

                if (currentErrorDegrees <= toleranceDegrees)
                {
                    Complete($"迭代中进入容差：{currentErrorDegrees:F3}°");
                    yield break;
                }
            }

            float frameEndError = GetCurrentAxisErrorDegrees();
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

    private void SolveOneDLSDirectionIteration(float subDeltaTime)
    {
        List<JointConfig> activeJoints = CollectActiveJoints();

        if (activeJoints.Count == 0)
            return;

        Vector3 currentAxis = GetTargetAxisWorld();
        Vector3 desiredAxis = GetChosenDesiredAxisWorld(currentAxis);

        currentTargetAxisWorld = currentAxis;
        currentDesiredAxisWorld = desiredAxis;

        Vector3 directionError = desiredAxis - currentAxis;
        directionError *= directionErrorGain;

        double[] error = new double[3];
        error[0] = directionError.x;
        error[1] = directionError.y;
        error[2] = directionError.z;

        int rows = 3;
        int cols = activeJoints.Count;

        double[,] J = new double[rows, cols];

        for (int c = 0; c < cols; c++)
        {
            JointConfig j = activeJoints[c];

            Vector3 axisWorld = j.GetWorldAxis();

            // 方向 IK 的雅可比：
            // 关节绕 axisWorld 小转，会让目标轴 currentAxis 产生 axisWorld x currentAxis 的变化。
            Vector3 directionDerivative = Vector3.Cross(axisWorld, currentAxis);

            float w = j.weight;

            J[0, c] = directionDerivative.x * w;
            J[1, c] = directionDerivative.y * w;
            J[2, c] = directionDerivative.z * w;
        }

        double[] deltaRadians = SolveDampedLeastSquares(J, error, damping);

        if (deltaRadians == null)
            return;

        float maxDeltaThisStep = maxJointSpeedDegPerSec * subDeltaTime;

        for (int i = 0; i < activeJoints.Count; i++)
        {
            JointConfig j = activeJoints[i];

            float deltaDeg = (float)(deltaRadians[i] * Mathf.Rad2Deg);

            j.AddAngle(deltaDeg, maxDeltaThisStep);
        }
    }

    private List<JointConfig> CollectActiveJoints()
    {
        List<JointConfig> activeJoints = new List<JointConfig>();

        if (joints == null)
            return activeJoints;

        foreach (JointConfig j in joints)
        {
            if (j == null) continue;
            if (j.joint == null) continue;
            if (!j.enabled) continue;
            if (j.weight <= 0f) continue;

            activeJoints.Add(j);
        }

        return activeJoints;
    }

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

    private Vector3 GetTargetAxisWorld()
    {
        if (targetObject == null)
            return Vector3.forward;

        return targetObject.TransformDirection(AxisToVector(targetLocalAxis)).normalized;
    }

    private Vector3 GetDesiredWorldAxis()
    {
        return AxisToVector(desiredWorldAxis).normalized;
    }

    private Vector3 GetChosenDesiredAxisWorld(Vector3 currentAxis)
    {
        Vector3 desired = GetDesiredWorldAxis();

        if (!allowOppositeDirection)
            return desired.normalized;

        float sameAngle = Vector3.Angle(currentAxis, desired);
        float oppositeAngle = Vector3.Angle(currentAxis, -desired);

        if (oppositeAngle < sameAngle)
        {
            desired = -desired;
        }

        return desired.normalized;
    }

    private float GetCurrentAxisErrorDegrees()
    {
        Vector3 currentAxis = GetTargetAxisWorld();
        Vector3 desiredAxis = GetDesiredWorldAxis();

        if (currentAxis.sqrMagnitude < 0.000001f || desiredAxis.sqrMagnitude < 0.000001f)
            return 999f;

        currentAxis.Normalize();
        desiredAxis.Normalize();

        float angle = Vector3.Angle(currentAxis, desiredAxis);

        if (allowOppositeDirection)
        {
            angle = Mathf.Min(angle, 180f - angle);
        }

        return angle;
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

    private void ResetRuntimeData()
    {
        stuckFrames = 0;

        currentErrorDegrees = GetCurrentAxisErrorDegrees();
        bestErrorDegrees = currentErrorDegrees;

        currentTargetAxisWorld = GetTargetAxisWorld();
        currentDesiredAxisWorld = GetChosenDesiredAxisWorld(currentTargetAxisWorld);
    }

    private bool ValidateSettings()
    {
        if (targetObject == null)
        {
            Debug.LogError("[MultiJointAxisAlignStep] targetObject 没填。", this);
            return false;
        }

        if (joints == null || joints.Length == 0)
        {
            Debug.LogError("[MultiJointAxisAlignStep] joints 为空。至少填一个关节。", this);
            return false;
        }

        bool hasEnabledJoint = false;

        for (int i = 0; i < joints.Length; i++)
        {
            JointConfig jointConfig = joints[i];

            if (jointConfig == null)
            {
                Debug.LogError($"[MultiJointAxisAlignStep] joints[{i}] 是空的。", this);
                return false;
            }

            if (!jointConfig.enabled)
                continue;

            hasEnabledJoint = true;

            if (jointConfig.joint == null)
            {
                Debug.LogError($"[MultiJointAxisAlignStep] joints[{i}] 的 joint 没填。", this);
                return false;
            }

            if (jointConfig.useLimits && jointConfig.minAngle > jointConfig.maxAngle)
            {
                Debug.LogError($"[MultiJointAxisAlignStep] joints[{i}] 的角度限制错误：minAngle > maxAngle。", this);
                return false;
            }
        }

        if (!hasEnabledJoint)
        {
            Debug.LogError("[MultiJointAxisAlignStep] 没有任何启用的关节。", this);
            return false;
        }

        if (solverSubsteps <= 0)
        {
            Debug.LogError("[MultiJointAxisAlignStep] solverSubsteps 必须大于 0。", this);
            return false;
        }

        if (maxJointSpeedDegPerSec <= 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] maxJointSpeedDegPerSec 必须大于 0。", this);
            return false;
        }

        if (damping < 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] damping 不能小于 0。", this);
            return false;
        }

        if (directionErrorGain <= 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] directionErrorGain 必须大于 0。", this);
            return false;
        }

        if (toleranceDegrees <= 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] toleranceDegrees 必须大于 0。", this);
            return false;
        }

        if (timeoutSeconds <= 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] timeoutSeconds 必须大于 0。", this);
            return false;
        }

        if (bestEffortToleranceDegrees <= 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] bestEffortToleranceDegrees 必须大于 0。", this);
            return false;
        }

        if (minUsefulImprovementDegrees < 0f)
        {
            Debug.LogError("[MultiJointAxisAlignStep] minUsefulImprovementDegrees 不能小于 0。", this);
            return false;
        }

        return true;
    }

    private void HandleTimeoutOrStuck(string reason)
    {
        currentErrorDegrees = GetCurrentAxisErrorDegrees();

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
        state = "Completed";
        runningCoroutine = null;

        currentErrorDegrees = GetCurrentAxisErrorDegrees();

        Debug.Log(
            $"[MultiJointAxisAlignStep] 完成。Reason = {reason}, Command = [{completeCommand}], Error = {currentErrorDegrees:F3}°",
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
        state = "Failed";

        StopRunningCoroutineOnly();

        currentErrorDegrees = GetCurrentAxisErrorDegrees();

        Debug.LogWarning(
            $"[MultiJointAxisAlignStep] 失败。Reason = {reason}, Command = [{failedCommand}], Error = {currentErrorDegrees:F3}°",
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

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos) return;
        if (targetObject == null) return;

        Vector3 origin = targetObject.position;

        Vector3 currentAxis = GetTargetAxisWorld();
        Vector3 desiredAxis = GetChosenDesiredAxisWorld(currentAxis);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + currentAxis * 0.35f);
        Gizmos.DrawSphere(origin + currentAxis * 0.35f, 0.015f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + desiredAxis * 0.45f);
        Gizmos.DrawSphere(origin + desiredAxis * 0.45f, 0.015f);
    }
}