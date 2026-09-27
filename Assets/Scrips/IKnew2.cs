using UnityEngine;

public enum ToolUpAxis
{
    LocalX,
    LocalY,
    LocalZ
}

public class IKnew2 : MonoBehaviour
{
    public JointConfig[] joints;
    public Transform target;
    public Transform endEffector;
    public int iterations = 10;
    public float maxStep = 10f;

    public bool keepLevel = true;
    public ToolUpAxis toolUpAxis = ToolUpAxis.LocalY;
    public Vector3 referenceUp = Vector3.up;
    public float maxOrientStep = 5f;

    private void Update()
    {
        if (joints == null || joints.Length == 0 || target == null || endEffector == null)
            return;

        SolveCCD(target.position);
    }

    private void SolveCCD(Vector3 targetPos)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            ApplyPositionPass(targetPos);
            if (keepLevel)
                ApplyOrientationPass();
        }
    }

    private void ApplyPositionPass(Vector3 targetPos)
    {
        for (int i = 0; i < joints.Length; i++)
        {
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
            ApplyClamped(config, axis, angle, maxStep);
        }
    }

    private void ApplyOrientationPass()
    {
        for (int i = 0; i < joints.Length; i++)
        {
            JointConfig config = joints[i];
            if (config == null || config.joint == null)
                continue;

            Vector3 axis = GetAxis(config);
            if (!TryGetOrientationAngle(axis, out float angle))
                continue;

            ApplyClamped(config, axis, angle, maxOrientStep);
        }
    }

    private void ApplyClamped(JointConfig config, Vector3 axis, float angle, float step)
    {
        float current = GetEulerAngle(config.joint, config.axis);
        if (angle > 0f)
            angle = Mathf.Min(angle, config.maxAngle - current);
        else
            angle = Mathf.Max(angle, config.minAngle - current);

        angle = Mathf.Clamp(angle, -step, step);

        config.joint.rotation = Quaternion.AngleAxis(angle, axis) * config.joint.rotation;
    }

    private bool TryGetOrientationAngle(Vector3 axis, out float angle)
    {
        Vector3 toolUp = endEffector.rotation * GetLocalAxis(toolUpAxis);
        Vector3 projTool = Vector3.ProjectOnPlane(toolUp, axis);
        Vector3 projRef = Vector3.ProjectOnPlane(referenceUp, axis);

        if (projTool.sqrMagnitude < 0.0001f || projRef.sqrMagnitude < 0.0001f)
        {
            angle = 0f;
            return false;
        }

        angle = Vector3.SignedAngle(projTool, projRef, axis);
        return true;
    }

    static Vector3 GetLocalAxis(ToolUpAxis axis)
    {
        return axis switch
        {
            ToolUpAxis.LocalX => Vector3.right,
            ToolUpAxis.LocalY => Vector3.up,
            ToolUpAxis.LocalZ => Vector3.forward,
            _ => Vector3.up
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
