using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>
    /// 绘图五自由度 DLS 求解器：控制真实笔尖位置及一根工具轴的方向，
    /// 保留绕该轴转动的自由度，让夹具保持水平而不强制机械臂的偏航角。
    /// 不处理碰撞、画线或工位状态；调用者负责运动提交与碰撞回退。
    /// </summary>
    public sealed class LabDrawingPoseIkSolver
    {
        public float PositionTolerance { get; set; } = .0004f;
        public float OrientationTolerance { get; set; } = .5f;
        public float JointSpeedDegrees { get; set; } = 80f;
        public float PositionError { get; private set; } = float.PositiveInfinity;
        public float OrientationError { get; private set; } = float.PositiveInfinity;
        public bool Converged { get; private set; }
        public int LastIterations { get; private set; }

        const float OrientationLength = .12f;
        const int MaximumIterations = 48;

        sealed class WorkBuffers
        {
            public readonly int count;
            public readonly double[,] jacobian, system;
            public readonly double[] error, delta;
            public readonly float[] minimumAngles, maximumAngles;
            public readonly bool[] usable;

            public WorkBuffers(int count)
            {
                this.count = count;
                jacobian = new double[6, count];
                system = new double[count, count + 1];
                error = new double[6];
                delta = new double[count];
                minimumAngles = new float[count];
                maximumAngles = new float[count];
                usable = new bool[count];
            }
        }

        WorkBuffers buffers;

        /// <param name="drawingTcp">真实笔尖，必须位于关节层级下；不能使用独立的目标或显示替身。</param>
        /// <param name="target">笔尖的目标世界位置。</param>
        /// <param name="localToolNormal">相对于 drawingTcp 的工具轴。例如 Vector3.up。</param>
        /// <param name="desiredWorldNormal">工具轴期望的世界方向。例如 Vector3.up。</param>
        /// <param name="dt">本次运动所占时间。所有迭代共用一个角度预算，不逐迭代累加速度。</param>
        /// <param name="fixedToolRoot">可选固定工具件；即使旧模型误把它列为关节，也不让求解器旋转它。</param>
        /// <returns>
        /// 当前姿态是否同时达到位置和方向公差。false 也可能表示本帧角速度预算用尽；
        /// 此时保留误差较小的姿态，是否接受或恢复由外层运动控制器决定。
        /// </returns>
        public bool Solve(LabRobotJointRig rig, Transform drawingTcp, Vector3 target,
            Vector3 localToolNormal, Vector3 desiredWorldNormal, float dt, Transform fixedToolRoot = null,
            float maximumPositionTolerance = float.PositiveInfinity)
        {
            Converged = false;
            LastIterations = 0;
            PositionError = OrientationError = float.PositiveInfinity;
            if (rig == null || rig.Count == 0 || !drawingTcp || !Finite(target) ||
                !Finite(localToolNormal) || !Finite(desiredWorldNormal) ||
                localToolNormal.sqrMagnitude < 1e-12f || desiredWorldNormal.sqrMagnitude < 1e-12f)
                return false;

            float positionTolerance = Mathf.Max(.000001f, Mathf.Min(PositionTolerance, maximumPositionTolerance));
            localToolNormal.Normalize();
            desiredWorldNormal.Normalize();
            if (UpdateDiagnostics(drawingTcp, target, localToolNormal, desiredWorldNormal, positionTolerance))
                return true;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0 ||
                float.IsNaN(JointSpeedDegrees) || float.IsInfinity(JointSpeedDegrees) || JointSpeedDegrees <= 0)
                return false;

            if (buffers == null || buffers.count != rig.Count)
                buffers = new WorkBuffers(rig.Count);
            WorkBuffers work = buffers;
            float angleBudget = JointSpeedDegrees * dt;
            for (int i = 0; i < rig.Count; i++)
            {
                work.usable[i] = rig.Affects(i, drawingTcp) &&
                    (!fixedToolRoot || rig.Joints[i].joint != fixedToolRoot);
                if (!work.usable[i]) continue;
                JointConfig config = rig.Joints[i];
                float startAngle = rig.GetAngle(i);
                work.minimumAngles[i] = Mathf.Max(config.minAngle, startAngle - angleBudget);
                work.maximumAngles[i] = Mathf.Min(config.maxAngle, startAngle + angleBudget);
            }

            // 法向反平行时选择固定的翻转轴；不把额外的偏航约束加入五自由度任务。
            Vector3 guide = Vector3.ProjectOnPlane(Vector3.right, desiredWorldNormal);
            if (guide.sqrMagnitude < 1e-8f)
                guide = Vector3.ProjectOnPlane(Vector3.forward, desiredWorldNormal);
            guide.Normalize();
            double damping = .0015;

            for (int pass = 0; pass < MaximumIterations; pass++)
            {
                LastIterations = pass + 1;
                Vector3 actualPosition = drawingTcp.position;
                Vector3 actualNormal = drawingTcp.TransformDirection(localToolNormal).normalized;
                Vector3 positionError = target - actualPosition;
                Vector3 normalError = NormalError(desiredWorldNormal, actualNormal, guide);
                if (!Finite(positionError) || !Finite(normalError)) break;
                if (UpdateDiagnostics(drawingTcp, target, localToolNormal, desiredWorldNormal, positionTolerance))
                    return true;

                work.error[0] = positionError.x;
                work.error[1] = positionError.y;
                work.error[2] = positionError.z;
                work.error[3] = normalError.x * OrientationLength;
                work.error[4] = normalError.y * OrientationLength;
                work.error[5] = normalError.z * OrientationLength;
                float previousCost = Cost(positionError, normalError);

                for (int i = 0; i < rig.Count; i++)
                {
                    for (int row = 0; row < 6; row++) work.jacobian[row, i] = 0;
                    if (!work.usable[i]) continue;
                    Vector3 axis = rig.GetWorldAxis(i).normalized;
                    Vector3 linear = Vector3.Cross(axis, actualPosition - rig.Joints[i].joint.position);
                    Vector3 angular = axis - actualNormal * Vector3.Dot(axis, actualNormal);
                    work.jacobian[0, i] = linear.x;
                    work.jacobian[1, i] = linear.y;
                    work.jacobian[2, i] = linear.z;
                    work.jacobian[3, i] = angular.x * OrientationLength;
                    work.jacobian[4, i] = angular.y * OrientationLength;
                    work.jacobian[5, i] = angular.z * OrientationLength;

                    double gradient = 0;
                    for (int row = 0; row < 6; row++) gradient += work.jacobian[row, i] * work.error[row];
                    float angle = rig.GetAngle(i);
                    if ((angle <= work.minimumAngles[i] + .00001f && gradient < 0) ||
                        (angle >= work.maximumAngles[i] - .00001f && gradient > 0))
                        for (int row = 0; row < 6; row++) work.jacobian[row, i] = 0;
                }

                for (int row = 0; row < rig.Count; row++)
                {
                    for (int col = 0; col < rig.Count; col++)
                    {
                        double value = 0;
                        for (int k = 0; k < 6; k++) value += work.jacobian[k, row] * work.jacobian[k, col];
                        work.system[row, col] = value + (row == col ? damping * damping : 0);
                    }
                    double right = 0;
                    for (int k = 0; k < 6; k++) right += work.jacobian[k, row] * work.error[k];
                    work.system[row, rig.Count] = right;
                }
                if (!SolveLinearSystem(work)) break;

                double largest = 0;
                for (int i = 0; i < rig.Count; i++) largest = Math.Max(largest, Math.Abs(work.delta[i]));
                if (largest < 1e-10) break;
                // 单次试探最多 12 度，整次 Solve 还受初始姿态累计角度预算限制。
                double stepLimit = Math.Min(angleBudget, 12f) * Mathf.Deg2Rad;
                double scale = largest > stepLimit ? stepLimit / largest : 1;
                rig.CaptureTrialPose(work.usable);

                bool accepted = false;
                for (int trial = 0; trial < 8; trial++)
                {
                    rig.RestoreTrialPose(work.usable);
                    double fraction = scale / (1 << trial);
                    for (int i = 0; i < rig.Count; i++)
                    {
                        if (!work.usable[i]) continue;
                        float next = rig.GetTrialAngle(i) + (float)(work.delta[i] * fraction) * Mathf.Rad2Deg;
                        next = Mathf.Clamp(next, work.minimumAngles[i], work.maximumAngles[i]);
                        rig.RotateToAngle(i, next, rig.GetWorldAxis(i).normalized);
                    }
                    Vector3 trialPosition = target - drawingTcp.position;
                    Vector3 trialNormal = NormalError(desiredWorldNormal,
                        drawingTcp.TransformDirection(localToolNormal).normalized, guide);
                    float cost = Cost(trialPosition, trialNormal);
                    if (Finite(trialPosition) && Finite(trialNormal) && cost < previousCost - 1e-14f)
                    {
                        accepted = true;
                        damping = Math.Max(.00005, damping * .6);
                        break;
                    }
                }
                if (!accepted)
                {
                    rig.RestoreTrialPose(work.usable);
                    damping *= 4;
                    if (damping > .08) break;
                }
            }
            return UpdateDiagnostics(drawingTcp, target, localToolNormal, desiredWorldNormal, positionTolerance);
        }

        bool UpdateDiagnostics(Transform tcp, Vector3 target, Vector3 localNormal, Vector3 desiredNormal, float positionTolerance)
        {
            PositionError = Vector3.Distance(tcp.position, target);
            OrientationError = Vector3.Angle(tcp.TransformDirection(localNormal), desiredNormal);
            Converged = PositionError <= positionTolerance &&
                OrientationError <= Mathf.Max(.001f, OrientationTolerance);
            return Converged;
        }

        static float Cost(Vector3 positionError, Vector3 normalError)
        {
            return positionError.sqrMagnitude + normalError.sqrMagnitude * OrientationLength * OrientationLength;
        }

        static bool Finite(Vector3 vector)
        {
            return !float.IsNaN(vector.x) && !float.IsInfinity(vector.x) &&
                !float.IsNaN(vector.y) && !float.IsInfinity(vector.y) &&
                !float.IsNaN(vector.z) && !float.IsInfinity(vector.z);
        }

        static Vector3 NormalError(Vector3 desired, Vector3 actual, Vector3 guide)
        {
            Vector3 cross = Vector3.Cross(actual, desired);
            float sine = cross.magnitude;
            float cosine = Mathf.Clamp(Vector3.Dot(actual, desired), -1, 1);
            if (sine > 1e-7f) return cross * (Mathf.Atan2(sine, cosine) / sine);
            if (cosine >= 0) return Vector3.zero;
            Vector3 axis = Vector3.ProjectOnPlane(guide, actual);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.ProjectOnPlane(Vector3.right, actual);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.ProjectOnPlane(Vector3.forward, actual);
            return axis.normalized * Mathf.PI;
        }

        static bool SolveLinearSystem(WorkBuffers work)
        {
            int count = work.count;
            double[,] matrix = work.system;
            for (int col = 0; col < count; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < count; row++)
                    if (Math.Abs(matrix[row, col]) > Math.Abs(matrix[pivot, col])) pivot = row;
                if (Math.Abs(matrix[pivot, col]) < 1e-16) return false;
                if (pivot != col)
                    for (int k = col; k <= count; k++)
                    {
                        double swap = matrix[col, k];
                        matrix[col, k] = matrix[pivot, k];
                        matrix[pivot, k] = swap;
                    }
                double diagonal = matrix[col, col];
                for (int row = col + 1; row < count; row++)
                {
                    double factor = matrix[row, col] / diagonal;
                    matrix[row, col] = 0;
                    for (int k = col + 1; k <= count; k++) matrix[row, k] -= factor * matrix[col, k];
                }
            }
            for (int row = count - 1; row >= 0; row--)
            {
                double value = matrix[row, count];
                for (int col = row + 1; col < count; col++) value -= matrix[row, col] * work.delta[col];
                work.delta[row] = value / matrix[row, row];
                if (double.IsNaN(work.delta[row]) || double.IsInfinity(work.delta[row])) return false;
            }
            return true;
        }
    }
}
