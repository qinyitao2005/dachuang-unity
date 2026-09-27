using UnityEngine;

public enum LinkAlignAxis
{
    LocalX,
    LocalY,
    LocalZ
}

public class IKnew : MonoBehaviour
{
    public JointConfig[] joints;
    public Transform target;
    public Transform endEffector;
    public int iterations = 10;

    public bool keepLinkVertical = true;
    public Transform link;
    public Transform linkConstraintJoint;
    public JointAxis linkConstraintAxis = JointAxis.Z;
    public LinkAlignAxis linkAlignAxis = LinkAlignAxis.LocalZ;
    public Vector3 planeNormal = Vector3.up;
    public float maxConstraintStep = 10f;

    private int linkIndex = -1;
    private bool skipWarned;

    private void Start()
    {
        FindLinkJoint();
    }

    private void Update()
    {
        if (joints == null || joints.Length == 0 || target == null || endEffector == null)
            return;

        SolveCCD(target.position);
    }

    private void FindLinkJoint()
    {
        linkIndex = -1;
        if (joints == null || linkConstraintJoint == null)
            return;

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null && joints[i].joint == linkConstraintJoint)
            {
                linkIndex = i;
                return;
            }
        }
    }

    private void SolveCCD(Vector3 targetPos)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            if (keepLinkVertical && link != null && linkConstraintJoint != null)
                ApplyLinkConstraint();

            for (int i = 0; i < joints.Length; i++)
            {
                if (i == linkIndex)
                    continue;

                JointConfig config = joints[i];
                if (config == null || config.joint == null)
                    continue;

                Vector3 axis = GetAxis(config);
                Vector3 jointPos = config.joint.position;

                Vector3 toEnd = Vector3.ProjectOnPlane(endEffector.position - jointPos, axis);
                Vector3 toTarget = Vector3.ProjectOnPlane(targetPos - jointPos, axis);

                if (toEnd.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
                    continue;

                float angle = Vector3.SignedAngle(toEnd, toTarget, axis);

                float current = GetEulerAngle(config.joint, config.axis);
                if (angle > 0f)
                    angle = Mathf.Min(angle, config.maxAngle - current);
                else
                    angle = Mathf.Max(angle, config.minAngle - current);

                angle = Mathf.Clamp(angle, -10f, 10f);

                config.joint.rotation = Quaternion.AngleAxis(angle, axis) * config.joint.rotation;
            }
        }
    }

    private void ApplyLinkConstraint()
    {
        if (linkIndex >= 0)
            ApplyConstraintOnJoint(linkConstraintJoint, GetAxis(joints[linkIndex]));
        else
            ApplyConstraintOnJoint(linkConstraintJoint, GetLinkAxis(linkConstraintJoint, linkConstraintAxis));
    }

    private void ApplyConstraintOnJoint(Transform joint, Vector3 axis)
    {
        Vector3 linkDir = link.rotation * GetLocalAxis(linkAlignAxis);

        Vector3 projLink = Vector3.ProjectOnPlane(linkDir, axis);
        Vector3 projNormal = Vector3.ProjectOnPlane(planeNormal, axis);

        if (projLink.sqrMagnitude < 0.0001f || projNormal.sqrMagnitude < 0.0001f)
        {
            if (!skipWarned)
            {
                skipWarned = true;
                Debug.LogWarning(
                    "IKnew: 垂直约束被跳过。约束轴=" + axis +
                    ", 连杆长度方向=" + linkDir +
                    ", projLink.sqrMagnitude=" + projLink.sqrMagnitude.ToString("F6") +
                    ", projNormal.sqrMagnitude=" + projNormal.sqrMagnitude.ToString("F6") +
                    ". 若 projNormal≈0 说明约束轴平行于世界Y(竖直)，需改 linkConstraintAxis；若 projLink≈0 说明连杆方向与约束轴平行，需改 linkAlignAxis。");
            }
            return;
        }

        float angle = Vector3.SignedAngle(projLink, projNormal, axis);
        angle = Mathf.Clamp(angle, -maxConstraintStep, maxConstraintStep);

        joint.rotation = Quaternion.AngleAxis(angle, axis) * joint.rotation;
    }

    static Vector3 GetLinkAxis(Transform joint, JointAxis axis)
    {
        return axis switch
        {
            JointAxis.X => joint.right,
            JointAxis.Y => joint.up,
            JointAxis.Z => joint.forward,
            JointAxis.WorldY => Vector3.up,
            _ => joint.up
        };
    }

    static Vector3 GetLocalAxis(LinkAlignAxis axis)
    {
        return axis switch
        {
            LinkAlignAxis.LocalX => Vector3.right,
            LinkAlignAxis.LocalY => Vector3.up,
            LinkAlignAxis.LocalZ => Vector3.forward,
            _ => Vector3.forward
        };
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
