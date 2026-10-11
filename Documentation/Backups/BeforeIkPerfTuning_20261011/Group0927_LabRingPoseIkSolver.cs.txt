using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>套圈姿态 DLS 求解器：约束圆心和环面法向，保留绕法向的自由转动。</summary>
    public sealed class LabRingPoseIkSolver
    {
        // 重用矩阵，避免每次运动子步创建垃圾；姿态快照由 JointRig 独立维护。
        sealed class WorkBuffers
        {
            public readonly int count;
            public readonly double[,] jacobian, system;
            public readonly double[] error, delta;
            public readonly bool[] usable;

            public WorkBuffers(int count)
            {
                this.count = count;
                jacobian = new double[6, count];
                system = new double[count, count + 1];
                error = new double[6];
                delta = new double[count];
                usable = new bool[count];
            }
        }

        WorkBuffers buffers;

        /// <param name="target">期望圆心的世界坐标；初始化时内部换算到圈缘 TCP。</param>
        /// <param name="frame">世界姿态：forward 为钢丝切向，up 为初始化引导方向。</param>
        /// <param name="radius">圆心到夹持 TCP 的刚性偏移距离。</param>
        /// <param name="initializePose">仅用于原有初始姿态预热，不用于日常移动。</param>
        public void Solve(LabRobotJointRig rig, Transform tcp, Vector3 target,
            Quaternion frame, float radius, bool initializePose, float dt)
        {
            if (rig == null || rig.Count == 0 || !tcp)
                return;
            if (buffers == null || buffers.count != rig.Count)
                buffers = new WorkBuffers(rig.Count);
            WorkBuffers work = buffers;

            Vector3 desiredPosition = target + (initializePose ? frame * Vector3.up * radius : Vector3.zero);
            Vector3 desiredNormal = frame * Vector3.forward;
            Quaternion desiredRotation = Quaternion.LookRotation(-(frame * Vector3.right), frame * Vector3.up);

            // TCP 局部 X 为夹爪闭合轴，局部 Y 指向手腕。普通移动解五自由度，
            // 初始化才解完整朝向；frame.up 只引导反平行法向，不强制世界竖直。
            const float orientationLength = .10f, positionTolerance = .00008f;
            const float angleTolerance = .5f * Mathf.Deg2Rad;
            float maximumStep = Mathf.Max(.1f, dt * 80) * Mathf.Deg2Rad;
            double damping = .0015;

            for (int pass = 0; pass < 32; pass++)
            {
                Vector3 actualCenter = tcp.position - (initializePose ? Vector3.zero : tcp.up * radius);
                Vector3 actualNormal = tcp.right;
                Vector3 positionError = desiredPosition - actualCenter;
                Vector3 rotationError = initializePose
                    ? RotationError(desiredRotation, tcp.rotation)
                    : NormalError(desiredNormal, actualNormal, frame * Vector3.up);
                if (positionError.sqrMagnitude < positionTolerance * positionTolerance &&
                    rotationError.sqrMagnitude < angleTolerance * angleTolerance)
                    return;

                work.error[0] = positionError.x;
                work.error[1] = positionError.y;
                work.error[2] = positionError.z;
                work.error[3] = rotationError.x * orientationLength;
                work.error[4] = rotationError.y * orientationLength;
                work.error[5] = rotationError.z * orientationLength;
                float previousCost = positionError.sqrMagnitude +
                    rotationError.sqrMagnitude * orientationLength * orientationLength;

                for (int i = 0; i < rig.Count; i++)
                {
                    JointConfig config = rig.Joints[i];
                    work.usable[i] = rig.Affects(i, tcp);
                    for (int row = 0; row < 6; row++)
                        work.jacobian[row, i] = 0;
                    if (!work.usable[i])
                        continue;

                    Vector3 axis = rig.GetWorldAxis(i).normalized;
                    // 点雅可比包含圈缘到圆心的刚性偏移；绕法向转动不改变法向。
                    Vector3 linear = Vector3.Cross(axis, actualCenter - config.joint.position);
                    Vector3 angular = initializePose
                        ? axis : axis - actualNormal * Vector3.Dot(axis, actualNormal);
                    work.jacobian[0, i] = linear.x;
                    work.jacobian[1, i] = linear.y;
                    work.jacobian[2, i] = linear.z;
                    work.jacobian[3, i] = angular.x * orientationLength;
                    work.jacobian[4, i] = angular.y * orientationLength;
                    work.jacobian[5, i] = angular.z * orientationLength;

                    // 已到限位的关节，不再沿超限方向参与求解。
                    double gradient = 0;
                    for (int row = 0; row < 6; row++)
                        gradient += work.jacobian[row, i] * work.error[row];
                    float angle = rig.GetAngle(i);
                    if ((angle <= config.minAngle + .0001f && gradient < 0) ||
                        (angle >= config.maxAngle - .0001f && gradient > 0))
                        for (int row = 0; row < 6; row++)
                            work.jacobian[row, i] = 0;
                }
                rig.CaptureTrialPose(work.usable);

                for (int row = 0; row < rig.Count; row++)
                {
                    for (int col = 0; col < rig.Count; col++)
                    {
                        double value = 0;
                        for (int k = 0; k < 6; k++)
                            value += work.jacobian[k, row] * work.jacobian[k, col];
                        work.system[row, col] = value + (row == col ? damping * damping : 0);
                    }
                    double right = 0;
                    for (int k = 0; k < 6; k++)
                        right += work.jacobian[k, row] * work.error[k];
                    work.system[row, rig.Count] = right;
                }
                if (!SolveLinearSystem(work))
                    return;

                double largest = 0;
                for (int i = 0; i < rig.Count; i++)
                    largest = Math.Max(largest, Math.Abs(work.delta[i]));
                if (largest < 1e-10)
                    return;
                double scale = largest > maximumStep ? maximumStep / largest : 1;

                bool accepted = false;
                for (int trial = 0; trial < 7; trial++)
                {
                    rig.RestoreTrialPose(work.usable);
                    double fraction = scale / Math.Pow(2, trial);
                    for (int i = 0; i < rig.Count; i++)
                    {
                        if (!work.usable[i])
                            continue;
                        float desiredAngle = rig.GetTrialAngle(i) +
                            (float)(work.delta[i] * fraction) * Mathf.Rad2Deg;
                        rig.RotateToAngle(i, desiredAngle, rig.GetWorldAxis(i).normalized);
                    }

                    Vector3 trialPosition = desiredPosition -
                        (tcp.position - (initializePose ? Vector3.zero : tcp.up * radius));
                    Vector3 trialRotation = initializePose
                        ? RotationError(desiredRotation, tcp.rotation)
                        : NormalError(desiredNormal, tcp.right, frame * Vector3.up);
                    float cost = trialPosition.sqrMagnitude +
                        trialRotation.sqrMagnitude * orientationLength * orientationLength;
                    if (cost < previousCost - 1e-14f)
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
                    if (damping > .08)
                        return;
                }
            }
        }

        static Vector3 NormalError(Vector3 desired, Vector3 actual, Vector3 startingGuide)
        {
            Vector3 cross = Vector3.Cross(actual, desired);
            float sine = cross.magnitude;
            float cosine = Mathf.Clamp(Vector3.Dot(actual, desired), -1, 1);
            if (sine > 1e-7f)
                return cross * (Mathf.Atan2(sine, cosine) / sine);
            if (cosine >= 0)
                return Vector3.zero;

            Vector3 guide = Vector3.ProjectOnPlane(startingGuide, actual);
            if (guide.sqrMagnitude < 1e-10f)
                guide = Vector3.ProjectOnPlane(Vector3.right, actual);
            if (guide.sqrMagnitude < 1e-10f)
                guide = Vector3.ProjectOnPlane(Vector3.forward, actual);
            return guide.normalized * Mathf.PI;
        }

        static Vector3 RotationError(Quaternion desired, Quaternion actual)
        {
            Quaternion difference = desired * Quaternion.Inverse(actual);
            if (difference.w < 0)
                difference = new Quaternion(-difference.x, -difference.y, -difference.z, -difference.w);
            Vector3 vector = new Vector3(difference.x, difference.y, difference.z);
            float length = vector.magnitude;
            return length < 1e-7f ? vector * 2
                : vector * (2 * Mathf.Atan2(length, Mathf.Clamp(difference.w, -1, 1)) / length);
        }

        static bool SolveLinearSystem(WorkBuffers work)
        {
            int count = work.count;
            double[,] matrix = work.system;

            // 双精度部分选主元，保留原实现对接近奇异姿态的处理。
            for (int col = 0; col < count; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < count; row++)
                    if (Math.Abs(matrix[row, col]) > Math.Abs(matrix[pivot, col]))
                        pivot = row;
                if (Math.Abs(matrix[pivot, col]) < 1e-16)
                    return false;

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
                    for (int k = col + 1; k <= count; k++)
                        matrix[row, k] -= factor * matrix[col, k];
                }
            }
            for (int row = count - 1; row >= 0; row--)
            {
                double value = matrix[row, count];
                for (int col = row + 1; col < count; col++)
                    value -= matrix[row, col] * work.delta[col];
                work.delta[row] = value / matrix[row, row];
                if (double.IsNaN(work.delta[row]) || double.IsInfinity(work.delta[row]))
                    return false;
            }
            return true;
        }
    }
}
