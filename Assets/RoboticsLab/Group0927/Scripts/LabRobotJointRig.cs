using System;
using UnityEngine;

namespace RoboticsLab
{
    /// <summary>维护关节角度、初始姿态和两套相互独立的姿态快照。</summary>
    public sealed class LabRobotJointRig
    {
        public JointConfig[] Joints { get; }
        public int Count => Joints.Length;

        /// <summary>供碰撞检测只读使用；在一次运动提交期间不得覆盖。</summary>
        public Quaternion[] CollisionRotations => collisionRotations;

        readonly Quaternion[] homeRotations;
        readonly float[] angles;
        readonly Quaternion[] collisionRotations;
        readonly float[] collisionAngles;
        readonly Quaternion[] trialRotations;
        readonly float[] trialAngles;

        public LabRobotJointRig(JointConfig[] joints)
        {
            Joints = joints ?? Array.Empty<JointConfig>();
            homeRotations = new Quaternion[Count];
            angles = new float[Count];
            collisionRotations = new Quaternion[Count];
            collisionAngles = new float[Count];
            trialRotations = new Quaternion[Count];
            trialAngles = new float[Count];

            for (int i = 0; i < Count; i++)
                if (HasJoint(i))
                    homeRotations[i] = Joints[i].joint.localRotation;
        }

        public bool HasJoint(int index)
        {
            return Joints[index] != null && Joints[index].joint;
        }

        public bool Affects(int index, Transform effector)
        {
            return HasJoint(index) && effector && effector.IsChildOf(Joints[index].joint);
        }

        public float GetAngle(int index) => angles[index];

        /// <summary>沿用原模型的局部铰链轴；WorldY 始终使用世界 Y 轴。</summary>
        public Vector3 GetWorldAxis(int index)
        {
            JointConfig config = Joints[index];
            if (config.axis == JointAxis.WorldY)
                return Vector3.up;

            Vector3 local = config.axis == JointAxis.X ? Vector3.right
                : config.axis == JointAxis.Z ? Vector3.forward : Vector3.up;
            return config.joint.TransformDirection(local);
        }

        /// <summary>目标角度相对于初始化姿态，以度为单位；统一在这里应用关节限位。</summary>
        public void RotateToAngle(int index, float desiredAngle, Vector3 worldAxis)
        {
            JointConfig config = Joints[index];
            float next = Mathf.Clamp(desiredAngle, config.minAngle, config.maxAngle);
            float step = next - angles[index];
            angles[index] = next;
            config.joint.rotation = Quaternion.AngleAxis(step, worldAxis) * config.joint.rotation;
        }

        public void ResetHome()
        {
            for (int i = 0; i < Count; i++)
            {
                if (!HasJoint(i))
                    continue;
                Joints[i].joint.localRotation = homeRotations[i];
                angles[i] = 0;
            }
        }

        public void CaptureCollisionPose()
        {
            Capture(collisionRotations, collisionAngles, null);
        }

        public void RestoreCollisionPose()
        {
            Restore(collisionRotations, collisionAngles, null);
        }

        // 求解器的线搜索会反复试探；这些快照不能覆盖外层碰撞事务的起点。
        internal void CaptureTrialPose(bool[] usable)
        {
            Capture(trialRotations, trialAngles, usable);
        }

        internal void RestoreTrialPose(bool[] usable)
        {
            Restore(trialRotations, trialAngles, usable);
        }

        internal float GetTrialAngle(int index) => trialAngles[index];

        void Capture(Quaternion[] rotations, float[] savedAngles, bool[] usable)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!HasJoint(i) || (usable != null && !usable[i]))
                    continue;
                rotations[i] = Joints[i].joint.localRotation;
                savedAngles[i] = angles[i];
            }
        }

        void Restore(Quaternion[] rotations, float[] savedAngles, bool[] usable)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!HasJoint(i) || (usable != null && !usable[i]))
                    continue;
                Joints[i].joint.localRotation = rotations[i];
                angles[i] = savedAngles[i];
            }
        }
    }
}
