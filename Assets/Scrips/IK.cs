using UnityEngine;

public enum JointAxis
{
    X,      // 绕本地 X (right)
    Y,      // 绕本地 Y (up)  
    Z,      // 绕本地 Z (forward)
    WorldY  // 绕世界 Y (底座常用)
}

[System.Serializable]
public class JointConfig
{
    public Transform joint;         // 关节物体
    public JointAxis axis;          // 旋转轴
    public float minAngle;          // 最小角度限制
    public float maxAngle;          // 最大角度限制（绝对值）
}

public class IK : MonoBehaviour
{
    public JointConfig[] joints;    // 从根部到末端配置每个关节
    public Transform target;
    public int iterations = 10;
    public Transform endEffector;
    public Animator animator; // 机械臂的动画
    //public GameObject grabber;
    //public GameObject littleWrist;

    private void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }
    void Update()
    {
        //// 动画播放期间暂停IK，动画结束（animator 被禁用）后自动恢复
        //if (animator != null && animator.isActiveAndEnabled)
        //    return;

        //if (joints == null || joints.Length == 0 || target == null || endEffector == null)
        //    return;

        SolveCCD(target.position);
    }

    void SolveCCD(Vector3 targetPos)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i].joint == null)
                    continue;

                Vector3 axis = GetAxis(joints[i]);
                Vector3 jointPos = joints[i].joint.position;

                Vector3 toEnd = Vector3.ProjectOnPlane(endEffector.position - jointPos, axis);
                Vector3 toTarget = Vector3.ProjectOnPlane(targetPos - jointPos, axis);

                if (toEnd.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
                    continue;

                float angle = Vector3.SignedAngle(toEnd, toTarget, axis);

                float current = GetEulerAngle(joints[i].joint, joints[i].axis);
                if (angle > 0f)
                    angle = Mathf.Min(angle, joints[i].maxAngle - current);
                else
                    angle = Mathf.Max(angle, joints[i].minAngle - current);

                angle = Mathf.Clamp(angle, -10f, 10f);

                joints[i].joint.rotation = Quaternion.AngleAxis(angle, axis) * joints[i].joint.rotation;
            }
        }
    }

    static float GetEulerAngle(Transform joint, JointAxis axis)
    {
        float euler = axis switch
        {
            JointAxis.X => joint.localEulerAngles.x,
            JointAxis.Y => joint.localEulerAngles.y,
            JointAxis.Z => joint.localEulerAngles.z,
            JointAxis.WorldY => joint.localEulerAngles.y,
            _ => joint.localEulerAngles.y
        };
        if (euler > 180f)
            euler -= 360f;
        return euler;
    }

    Vector3 GetAxis(JointConfig config)
    {
        return config.axis switch
        {
            JointAxis.X => config.joint.right,
            JointAxis.Y => config.joint.up,
            JointAxis.Z => config.joint.forward,
            JointAxis.WorldY => Vector3.up,
            _ => config.joint.up
        };
    }
}
