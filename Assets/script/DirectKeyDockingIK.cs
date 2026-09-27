using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 直接 Key-to-Key 对接 IK。
///
/// 这个脚本现在只负责“对接 IK”本身。
/// 它不再负责 Attach，不再负责启动后续步骤。
///
/// 流程：
/// 1. SequenceSignalController 调用 StartDocking()
/// 2. 本脚本发出：指令-对接IK开始执行
/// 3. 机械手前往预对接点
/// 4. 沿 PointerKey 指定轴插入
/// 5. 对接完成
/// 6. 本脚本发出：指令-对接IK已经完成
///
/// 后续 Attach / 笔运动，都交给 SequenceSignalController 按顺序启动。
/// </summary>
public class DirectKeyDockingIK : MonoBehaviour
{
    [Serializable]
    public class DockingCommandEvent : UnityEvent<string>
    {
    }

    public enum LocalAxis
    {
        X,
        Y,
        Z
    }

    public enum DockingState
    {
        Idle,
        MoveToPreDock,
        Insert,
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
        [Tooltip("底座/大臂/小臂建议 1；手掌 0.2~0.5；手指第一版建议 0 或禁用。")]
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

    [Header("两个 Key")]
    [Tooltip("机械手末端接口 key。这个小方块应该挂在机械手末端下面。")]
    public Transform handKey;

    [Tooltip("指针/插座接口 key。这个小方块应该挂在指针接口上。")]
    public Transform pointerKey;

    [Header("对接轴设置")]
    [Tooltip("用 PointerKey 的哪根本地轴作为插入方向。一般选 Z，也就是蓝轴。")]
    public LocalAxis pointerDockAxis = LocalAxis.Z;

    [Tooltip("勾上：使用该轴正方向；不勾：使用该轴反方向。")]
    public bool usePositiveAxis = true;

    [Header("对接距离")]
    [Tooltip("预对接点距离最终接口点多远。")]
    public float preDockDistance = 0.03f;

    [Tooltip("当前 Offset。运行时显示：从 preDockDistance 慢慢变成 0。")]
    public float currentOffset = 0f;

    [Header("插入控制")]
    [Tooltip("插入速度。Offset 每秒减少多少。")]
    public float insertSpeed = 0.01f;

    [Tooltip("当前目标点最多领先 HandKey 多远。超过这个距离，目标点不继续往前。")]
    public float maxLeadDistance = 0.015f;

    [Header("判定误差")]
    [Tooltip("HandKey 离预对接点多近，开始进入插入阶段。")]
    public float preDockTolerance = 0.04f;

    [Tooltip("HandKey 离 PointerKey 多近，算最终对接完成。")]
    public float finalTolerance = 0.015f;

    [Header("是否追旋转")]
    [Tooltip("第一版建议先关。打开后，HandKey 会尝试追 PointerKey.rotation。")]
    public bool solveRotation = false;

    [Tooltip("进入插入阶段时的旋转误差门槛。solveRotation 关闭时无效。")]
    public float preDockRotationTolerance = 45f;

    [Tooltip("完成时的旋转误差门槛。solveRotation 关闭时无效。")]
    public float finalRotationTolerance = 30f;

    [Tooltip("姿态误差权重。太大容易乱。")]
    [Range(0f, 1f)]
    public float rotationWeight = 0.1f;

    [Header("IK 关节，从根到末端")]
    public IKJoint[] joints;

    [Header("IK 参数")]
    [Tooltip("每帧分几次小步计算。")]
    public int solverSubsteps = 4;

    [Tooltip("每个关节最大角速度，单位：度/秒。这个控制机械臂不会瞬移。")]
    public float maxJointSpeedDegPerSec = 35f;

    [Tooltip("阻尼，越大越稳，越小越猛。")]
    public float damping = 0.1f;

    [Tooltip("位置误差小于这个值时，本帧 IK 不再继续死磕。")]
    public float ikPositionTolerance = 0.006f;

    [Header("运行")]
    public bool autoStart = false;
    public DockingState state = DockingState.Idle;

    [Header("信号命名")]
    [Tooltip("只是方便你在 Inspector 里看这个步骤叫什么。")]
    public string stepDisplayName = "对接IK";

    [Tooltip("开始执行时输出到 Console 的指令。")]
    public string startCommand = "指令-对接IK开始执行";

    [Tooltip("完成时输出到 Console 的指令。总控等待的就是这个。")]
    public string completeCommand = "指令-对接IK已经完成";

    [Tooltip("失败时输出到 Console 的指令。")]
    public string failedCommand = "指令-对接IK执行失败";

    [Tooltip("开始执行时触发。一般不用接给总控，只用于调试或显示。")]
    public DockingCommandEvent onStarted;

    [Tooltip("完成时触发。这个要接给 SequenceSignalController.ReceiveSignal(string)。")]
    public DockingCommandEvent onCompleted;

    [Tooltip("失败时触发。可选。")]
    public DockingCommandEvent onFailed;

    [Header("调试")]
    public bool drawDebugGizmos = true;

    [Header("运行时显示")]
    [SerializeField] private bool started = false;
    [SerializeField] private bool completed = false;
    [SerializeField] private bool failed = false;

    private Vector3 currentGoalPosition;
    private Quaternion currentGoalRotation;

    private bool startSignalAlreadySent = false;
    private bool completeSignalAlreadySent = false;
    private bool failedSignalAlreadySent = false;

    private void Start()
    {
        CacheCurrentPoseAsInitial();

        if (autoStart)
        {
            StartDocking();
        }
    }

    private void Update()
    {
        if (!CheckReferences()) return;

        switch (state)
        {
            case DockingState.Idle:
                break;

            case DockingState.MoveToPreDock:
                UpdateMoveToPreDock();
                break;

            case DockingState.Insert:
                UpdateInsert();
                break;

            case DockingState.Complete:
                break;
        }
    }

    [ContextMenu("Start Docking")]
    public void StartDocking()
    {
        ResetSignalRuntimeState();

        if (!CheckReferences())
        {
            Debug.LogWarning("[DirectKeyDockingIK] 引用没填完整，无法开始。");
            FailDocking();
            return;
        }

        completed = false;
        failed = false;
        started = true;

        currentOffset = preDockDistance;
        ComputeGoalFromOffset(currentOffset);

        state = DockingState.MoveToPreDock;

        Debug.Log("[DirectKeyDockingIK] 开始：机械手前往预对接点。");

        SendStartSignal();
    }

    [ContextMenu("Stop Docking")]
    public void StopDocking()
    {
        state = DockingState.Idle;
    }

    [ContextMenu("Reset To Initial Pose")]
    public void ResetToInitialPose()
    {
        if (joints == null) return;

        foreach (IKJoint j in joints)
        {
            if (j == null || j.joint == null) continue;

            j.currentAngle = 0f;
            j.ApplyCurrentAngle();
        }

        ResetSignalRuntimeState();

        started = false;
        completed = false;
        failed = false;

        state = DockingState.Idle;
        currentOffset = preDockDistance;
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

    private void ResetSignalRuntimeState()
    {
        startSignalAlreadySent = false;
        completeSignalAlreadySent = false;
        failedSignalAlreadySent = false;
    }

    private void UpdateMoveToPreDock()
    {
        currentOffset = preDockDistance;
        ComputeGoalFromOffset(currentOffset);

        SolveIKToGoal(currentGoalPosition, currentGoalRotation);

        float posError = Vector3.Distance(handKey.position, currentGoalPosition);
        bool posOk = posError <= preDockTolerance;

        bool rotOk = true;

        if (solveRotation)
        {
            float rotError = Quaternion.Angle(handKey.rotation, currentGoalRotation);
            rotOk = rotError <= preDockRotationTolerance;
        }

        if (posOk && rotOk)
        {
            state = DockingState.Insert;
            Debug.Log("[DirectKeyDockingIK] 已到预对接点，开始沿轴插入。");
        }
    }

    private void UpdateInsert()
    {
        ComputeGoalFromOffset(currentOffset);

        float distanceToCurrentGoal = Vector3.Distance(handKey.position, currentGoalPosition);

        if (distanceToCurrentGoal <= maxLeadDistance)
        {
            currentOffset = Mathf.MoveTowards(
                currentOffset,
                0f,
                insertSpeed * Time.deltaTime
            );
        }

        ComputeGoalFromOffset(currentOffset);

        SolveIKToGoal(currentGoalPosition, currentGoalRotation);

        Vector3 finalPosition = pointerKey.position;
        float finalPosError = Vector3.Distance(handKey.position, finalPosition);

        bool posOk = finalPosError <= finalTolerance;

        bool rotOk = true;

        if (solveRotation)
        {
            float rotError = Quaternion.Angle(handKey.rotation, pointerKey.rotation);
            rotOk = rotError <= finalRotationTolerance;
        }

        if (currentOffset <= 0.0001f && posOk && rotOk)
        {
            currentOffset = 0f;
            ComputeGoalFromOffset(currentOffset);

            state = DockingState.Complete;
            completed = true;

            Debug.Log("[DirectKeyDockingIK] Dock Complete：对接完成。");

            SendCompleteSignal();
        }
    }

    private void FailDocking()
    {
        state = DockingState.Idle;
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

    private void ComputeGoalFromOffset(float offset)
    {
        Vector3 axis = GetPointerDockAxisWorld();

        currentGoalPosition = pointerKey.position + axis * offset;
        currentGoalRotation = pointerKey.rotation;
    }

    private Vector3 GetPointerDockAxisWorld()
    {
        Vector3 localAxis = GetAxisVector(pointerDockAxis);
        Vector3 worldAxis = pointerKey.TransformDirection(localAxis).normalized;

        if (!usePositiveAxis)
        {
            worldAxis = -worldAxis;
        }

        return worldAxis;
    }

    private static Vector3 GetAxisVector(LocalAxis axis)
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

    private void SolveIKToGoal(Vector3 goalPosition, Quaternion goalRotation)
    {
        if (joints == null || joints.Length == 0) return;

        int steps = Mathf.Max(1, solverSubsteps);
        float subDeltaTime = Time.deltaTime / steps;

        for (int i = 0; i < steps; i++)
        {
            float positionError = Vector3.Distance(handKey.position, goalPosition);

            if (!solveRotation && positionError <= ikPositionTolerance)
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

        int rows = solveRotation ? 6 : 3;
        int cols = activeJoints.Count;

        double[,] J = new double[rows, cols];
        double[] error = new double[rows];

        Vector3 posError = goalPosition - handKey.position;

        error[0] = posError.x;
        error[1] = posError.y;
        error[2] = posError.z;

        if (solveRotation)
        {
            Vector3 rotError = GetRotationErrorVectorRadians(goalRotation, handKey.rotation);

            error[3] = rotError.x * rotationWeight;
            error[4] = rotError.y * rotationWeight;
            error[5] = rotError.z * rotationWeight;
        }

        for (int c = 0; c < cols; c++)
        {
            IKJoint j = activeJoints[c];

            Vector3 axisWorld = j.GetWorldAxis();
            Vector3 r = handKey.position - j.joint.position;

            Vector3 linear = Vector3.Cross(axisWorld, r);

            float w = j.weight;

            J[0, c] = linear.x * w;
            J[1, c] = linear.y * w;
            J[2, c] = linear.z * w;

            if (solveRotation)
            {
                J[3, c] = axisWorld.x * rotationWeight * w;
                J[4, c] = axisWorld.y * rotationWeight * w;
                J[5, c] = axisWorld.z * rotationWeight * w;
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
        return handKey != null &&
               pointerKey != null &&
               joints != null &&
               joints.Length > 0;
    }

    public bool IsStarted()
    {
        return started;
    }

    public bool IsCompleted()
    {
        return completed;
    }

    public bool IsFailed()
    {
        return failed;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos) return;
        if (pointerKey == null) return;

        Vector3 axis = GetPointerDockAxisWorld();

        Vector3 finalPos = pointerKey.position;
        Vector3 prePos = pointerKey.position + axis * preDockDistance;

        Gizmos.DrawWireSphere(prePos, 0.04f);
        Gizmos.DrawWireSphere(finalPos, 0.035f);
        Gizmos.DrawLine(prePos, finalPos);

        Gizmos.DrawWireSphere(currentGoalPosition, 0.025f);

        if (handKey != null)
        {
            Gizmos.DrawLine(handKey.position, currentGoalPosition);
        }
    }
}