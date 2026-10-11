using UnityEngine;

namespace RoboticsLab
{
    /// <summary>只求解末端位置的 CCD；从腕部到肩部依次调整原模型铰链。</summary>
    public sealed class LabPositionIkSolver
    {
        /// <param name="target">世界坐标目标。</param>
        /// <param name="tolerance">世界单位位置容差，由使用方按训练模式设置。</param>
        public void Solve(LabRobotJointRig rig, Transform effector, Vector3 target,
            float dt, float tolerance)
        {
            if (rig == null || rig.Count == 0 || !effector)
                return;

            for (int pass = 0; pass < 5; pass++)
            {
                if ((effector.position - target).sqrMagnitude < tolerance * tolerance)
                    break;

                for (int i = rig.Count - 1; i >= 0; i--)
                {
                    if (!rig.Affects(i, effector))
                        continue;

                    Transform joint = rig.Joints[i].joint;
                    Vector3 axis = rig.GetWorldAxis(i);
                    Vector3 current = Vector3.ProjectOnPlane(effector.position - joint.position, axis);
                    Vector3 desired = Vector3.ProjectOnPlane(target - joint.position, axis);
                    if (current.sqrMagnitude < 1e-8f || desired.sqrMagnitude < 1e-8f)
                        continue;

                    float maximumStep = Mathf.Max(.1f, dt * 80);
                    float step = Mathf.Clamp(Vector3.SignedAngle(current, desired, axis),
                        -maximumStep, maximumStep);
                    rig.RotateToAngle(i, rig.GetAngle(i) + step, axis);
                }
            }
        }
    }
}
